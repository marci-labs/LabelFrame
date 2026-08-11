using System.Text.Json;

namespace LabelFrame.WinHost;

/// <summary>机器级配置存取（迭代 18）：serverUrl 持久化到 %ProgramData%\LabelFrame\Client\settings.json，供前端 /api/host/config 读写。</summary>
public sealed class HostConfigStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public HostConfigStore(string path)
    {
        _path = path;
    }

    /// <summary>读取已保存的 serverUrl；文件缺失 / 损坏返回 null（调用方回退默认值）。</summary>
    public string? LoadServerUrl()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var stream = File.OpenRead(_path);
            using var doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("serverUrl", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            // 配置损坏时兜底默认值，不影响启动
            return null;
        }
    }

    /// <summary>保存 serverUrl（先写临时文件再原子替换，避免写一半）。</summary>
    public void SaveServerUrl(string serverUrl)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { serverUrl }), new System.Text.UTF8Encoding(false));
            File.Move(temp, _path, overwrite: true);
        }
    }
}
