using LabelFrame.Core.Transport;

namespace LabelFrame.WinHost.Transport;

/// <summary>连接管理结果：ok + 中文消息 + 当前生效连接。</summary>
public sealed record TransportChangeResult(bool Ok, string Message, TransportConfig Config);

/// <summary>运行时连接管理：持有当前唯一生效的连接配置与传输实例，支持「先测试后生效」切换并持久化。</summary>
public interface ITransportManager
{
    /// <summary>当前生效连接配置。</summary>
    TransportConfig CurrentConfig { get; }

    /// <summary>当前生效传输实例（作业 Worker / 状态 / 测试页统一从这里取）。</summary>
    IPrintTransport CurrentTransport { get; }

    /// <summary>connection.json 路径（用户数据目录）。</summary>
    string ConfigFilePath { get; }

    /// <summary>应用连接：校验 → 测试 → 切换 + 持久化；testOnly 只测试不保存不切换。</summary>
    Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default);
}

/// <summary>
/// 连接管理器实现：启动时按 connection.json &gt; appsettings / 环境变量 &gt; 默认 Log 初始化；
/// 同一时间只有单一连接生效；切换成功才持久化到 %LOCALAPPDATA%\LabelFrame\connection.json（用户可写）。
/// </summary>
public sealed class TransportManager : ITransportManager
{
    private readonly TextWriter _hostLogWriter;
    private TransportConfig _config;
    private IPrintTransport _transport;

    /// <summary>创建连接管理器。</summary>
    /// <param name="configFilePath">connection.json 路径（默认 %LOCALAPPDATA%\\LabelFrame\\connection.json；测试可注入临时路径）。</param>
    public TransportManager(HostOptions options, TextWriter hostLogWriter, string? configFilePath = null)
    {
        _hostLogWriter = hostLogWriter ?? throw new ArgumentNullException(nameof(hostLogWriter));
        ConfigFilePath = configFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LabelFrame",
            "connection.json");

        var baseConfig = new TransportConfig
        {
            Mode = options.Transport,
            TcpHost = options.TcpHost,
            TcpPort = options.TcpPort,
            PrinterName = options.PrinterName,
            ZebraKind = options.ZebraKind,
            ZebraUsbName = options.ZebraUsbName,
        };
        _config = LoadPersisted(baseConfig);
        _transport = CreateTransport(_config);
    }

    /// <inheritdoc />
    public TransportConfig CurrentConfig => _config;

    /// <inheritdoc />
    public IPrintTransport CurrentTransport => _transport;

    /// <inheritdoc />
    public string ConfigFilePath { get; }

    /// <inheritdoc />
    public async Task<TransportChangeResult> ApplyAsync(TransportConfig config, bool testOnly, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var validationMessage = Validate(config);
        if (validationMessage is not null)
        {
            return new TransportChangeResult(false, validationMessage, _config);
        }

        IPrintTransport candidate;
        try
        {
            candidate = CreateTransport(config);
        }
        catch (Exception ex)
        {
            return new TransportChangeResult(false, $"连接配置无效：{ex.Message}", _config);
        }

        var testError = await TestAsync(candidate, cancellationToken);
        if (testError is not null)
        {
            return new TransportChangeResult(false, testError, _config);
        }

        if (testOnly)
        {
            return new TransportChangeResult(true, $"连接测试成功：{config.Describe()}。", _config);
        }

        _config = config;
        _transport = candidate;
        Persist(_config);
        return new TransportChangeResult(true, $"已切换为 {_config.Describe()}。", _config);
    }

    /// <summary>参数校验，返回错误消息（null = 通过）。</summary>
    private static string? Validate(TransportConfig config) => config.Mode switch
    {
        TransportMode.Tcp or TransportMode.Zebra when config.ZebraKind == ZebraTransportKind.Tcp => ValidateHostPort(config),
        TransportMode.WindowsDriver => string.IsNullOrWhiteSpace(config.PrinterName)
            ? "必须指定 Windows 打印机名（printerName）。"
            : null,
        TransportMode.Zebra when config.ZebraKind == ZebraTransportKind.Driver => string.IsNullOrWhiteSpace(config.PrinterName)
            ? "Zebra 驱动模式必须指定打印机名（printerName）。"
            : null,
        TransportMode.Zebra when config.ZebraKind == ZebraTransportKind.Usb => null,
        TransportMode.Log => null,
        _ => $"不支持的连接方式：{config.Mode}。",
    };

    private static string? ValidateHostPort(TransportConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.TcpHost))
        {
            return "必须指定打印机地址（tcpHost）。";
        }

        if (config.TcpPort is < 1 or > 65535)
        {
            return "端口必须在 1-65535 之间（tcpPort）。";
        }

        return null;
    }

    /// <summary>连接测试：Tcp / Zebra / Windows 驱动按各自方式探测；Log 恒成功。</summary>
    private static async Task<string?> TestAsync(IPrintTransport transport, CancellationToken cancellationToken)
    {
        switch (transport)
        {
            case Tcp9100PrintTransport tcp:
                return await tcp.TestConnectionAsync(cancellationToken)
                    ? null
                    : "连接测试失败：无法连接打印机（超时或地址不可达）。";
            case RawPrinterTransport raw:
                return raw.TestConnection()
                    ? null
                    : "连接测试失败：无法打开打印机（请检查打印机名是否与系统一致、驱动是否已安装）。";
            case ZebraPrinterTransport zebra:
                return await zebra.TestConnectionAsync(cancellationToken)
                    ? null
                    : "连接测试失败：Zebra 打印机不可达（请检查连接方式与地址）。";
            case LogPrintTransport:
                return null;
            default:
                return null;
        }
    }

    /// <summary>按配置创建传输实例。</summary>
    private IPrintTransport CreateTransport(TransportConfig config) => config.Mode switch
    {
        // 复用宿主日志写入器（同一文件不能再开第二个写入器，否则文件锁导致写入被静默丢弃）
        TransportMode.Log => new LogPrintTransport(_hostLogWriter),
        TransportMode.Tcp => new Tcp9100PrintTransport(config.TcpHost, config.TcpPort),
        TransportMode.WindowsDriver => new RawPrinterTransport(config.PrinterName),
        TransportMode.Zebra => new ZebraPrinterTransport(config.ZebraKind, config.TcpHost, config.TcpPort, config.PrinterName, config.ZebraUsbName),
        _ => throw new InvalidOperationException($"不支持的传输模式：{config.Mode}。"),
    };

    private TransportConfig LoadPersisted(TransportConfig baseConfig)
    {
        if (!File.Exists(ConfigFilePath))
        {
            return baseConfig;
        }

        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var persisted = TransportConfig.FromJson(json);
            if (persisted is null)
            {
                return baseConfig;
            }

            // connection.json 优先级最高：存在即以它为当前连接（缺失字段回退 baseConfig）
            if (persisted.Mode == TransportMode.Tcp || (persisted.Mode == TransportMode.Zebra && persisted.ZebraKind == ZebraTransportKind.Tcp))
            {
                persisted.TcpHost = string.IsNullOrWhiteSpace(persisted.TcpHost) ? baseConfig.TcpHost : persisted.TcpHost;
                persisted.TcpPort = persisted.TcpPort is < 1 or > 65535 ? baseConfig.TcpPort : persisted.TcpPort;
            }

            if (string.IsNullOrWhiteSpace(persisted.PrinterName))
            {
                persisted.PrinterName = baseConfig.PrinterName;
            }

            if (string.IsNullOrWhiteSpace(persisted.ZebraUsbName))
            {
                persisted.ZebraUsbName = baseConfig.ZebraUsbName;
            }

            return persisted;
        }
        catch (Exception)
        {
            // 读取 / 解析失败回退默认
            return baseConfig;
        }
    }

    private void Persist(TransportConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ConfigFilePath, config.ToJson());
        }
        catch (Exception ex)
        {
            // 持久化失败不阻断本次切换（下次启动回退 appsettings 默认）
            _hostLogWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 连接配置持久化失败（{ConfigFilePath}）：{ex.Message}");
        }
    }
}