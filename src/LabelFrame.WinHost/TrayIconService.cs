using System.Windows.Forms;

namespace LabelFrame.WinHost;

/// <summary>系统托盘图标（WinExe 无窗口，提供打开界面 / 退出入口）。</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly object _lock = new();
    private NotifyIcon? _icon;
    private ApplicationContext? _context;
    private Thread? _thread;
    private bool _disposed;

    /// <summary>启动托盘（后台 STA 线程跑 WinForms 消息循环）。</summary>
    /// <param name="listenUrl">界面地址（双击 / 菜单打开）。</param>
    /// <param name="shutdown">退出回调（停止宿主）。</param>
    public void Start(string listenUrl, Func<Task> shutdown)
    {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
                   ?? System.Drawing.SystemIcons.Application;
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开界面", null, (_, _) => OpenBrowser(listenUrl));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, async (_, _) =>
        {
            lock (_lock)
            {
                if (_icon is not null) { _icon.Visible = false; }
            }
            await shutdown();
            Application.Exit();
        });

        _context = new ApplicationContext();
        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "LabelFrame 标签打印",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenBrowser(listenUrl);

        _thread = new Thread(() =>
        {
            if (_context is not null)
            {
                Application.Run(_context);
            }
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打开浏览器失败忽略
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // 让托盘消息循环退出并等待线程结束；不跨线程操作 NotifyIcon（避免死锁），
            // 进程退出时托盘图标由系统自动清理。
            if (_context is not null)
            {
                try
                {
                    _context.ExitThread();
                }
                catch
                {
                    // 跨线程调用可能无效，忽略
                }

                _thread?.Join(TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            // 清理失败不影响宿主退出
        }
    }
}
