using System.Runtime.InteropServices;

namespace LabelFrame.WinHost;

/// <summary>
/// 系统托盘图标（原生 P/Invoke 实现，无 WinForms 依赖）。
/// 隐藏窗口接收 Shell_NotifyIcon 回调：右键菜单（打开界面 / 退出）、双击打开界面。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1;
    private const uint NIF_ICON = 2;
    private const uint NIF_TIP = 4;

    private const uint WM_USER = 0x0400;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_QUIT = 0x0012;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_TOPALIGN = 0x0000;

    private const int CmdOpen = 1;
    private const int CmdExit = 2;
    private const string WindowClass = "LabelFrameTrayWindow";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern IntPtr TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private readonly Action<string>? _log;
    private readonly TrayQuitSignaler _quitSignaler;
    private IntPtr _hwnd;
    private WndProcDelegate? _wndProc; // 防止委托被 GC
    private int _disposed;

    /// <summary>创建托盘服务（可选日志回调；quitSignaler 可注入观测替身，默认自建）。</summary>
    public TrayIconService(Action<string>? log = null, TrayQuitSignaler? quitSignaler = null)
    {
        _log = log;
        _quitSignaler = quitSignaler ?? new TrayQuitSignaler(log);
    }

    /// <summary>启动托盘（独立消息循环线程）。</summary>
    /// <param name="openUi">打开界面回调（界面壳：显示并前置窗口；浏览器兜底：打开默认浏览器）。</param>
    /// <param name="shutdown">退出回调（停止宿主）。</param>
    public void Start(Action openUi, Func<Task> shutdown)
    {
        // 线程引用由 TrayQuitSignaler 登记（RunTrayLoop 内），Dispose 时投递 WM_QUIT 并限时 Join
        var thread = new Thread(() => RunTrayLoop(openUi, shutdown))
        {
            IsBackground = true,
        };
        thread.Start();
    }

    private void RunTrayLoop(Action openUi, Func<Task> shutdown)
    {
        try
        {
            var instance = GetModuleHandle(null);
            _wndProc = (hWnd, msg, wParam, lParam) => WndProc(hWnd, msg, wParam, lParam, openUi, shutdown);

            // 注册窗口类并创建隐藏消息窗口
            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = instance,
                lpszClassName = WindowClass,
            };
            RegisterClassW(ref wc);
            _hwnd = CreateWindowEx(
                0, WindowClass, "LabelFrameTray", 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                _log?.Invoke("托盘窗口创建失败（CreateWindowEx 返回空）。");
                return;
            }

            var icon = LoadIcon(instance, new IntPtr(0x7F00)); // IDI_APPLICATION
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_USER + 1,
                hIcon = icon,
                szTip = "LabelFrame 标签打印",
            };
            Shell_NotifyIcon(NIM_ADD, ref nid);

            // 登记托盘线程（缺陷 #58）：Dispose 据此投递 WM_QUIT——消息循环退出后在托盘线程内执行 NIM_DELETE
            _quitSignaler.RegisterLoopThread(GetCurrentThreadId(), Thread.CurrentThread);

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            Shell_NotifyIcon(NIM_DELETE, ref nid);
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            // 托盘异常只记录日志，不让宿主进程崩溃（WinExe 无窗口，用户看不到错误弹窗）
            _log?.Invoke($"托盘服务异常：{ex}");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, Action openUi, Func<Task> shutdown)
    {
        if (msg == WM_USER + 1)
        {
            var mouseMsg = (uint)lParam.ToInt64();
            if (mouseMsg == WM_RBUTTONUP)
            {
                ShowMenu(openUi, shutdown);
            }
            else if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                openUi();
            }

            return IntPtr.Zero;
        }

        if (msg == WM_QUIT)
        {
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu(Action openUi, Func<Task> shutdown)
    {
        GetCursorPos(out var pt);
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, new IntPtr(CmdOpen), "打开界面");
        AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, string.Empty);
        AppendMenu(menu, MF_STRING, new IntPtr(CmdExit), "退出");
        var cmd = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_TOPALIGN, pt.x, pt.y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == CmdOpen)
        {
            openUi();
        }
        else if (cmd == CmdExit)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await shutdown();
                }
                catch
                {
                    // 退出回调异常由宿主记录
                }
            });
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            // 向托盘线程投递 WM_QUIT + 限时 Join（缺陷 #58）：消息循环退出后在托盘线程内
            // 执行 Shell_NotifyIcon(NIM_DELETE)，托盘图标随进程退出移除，不再残留幽灵图标。
            _quitSignaler.Dispose();
        }
        catch
        {
            // 清理失败不影响宿主退出
        }
    }
}

/// <summary>
/// 托盘消息循环线程的退出信号器（缺陷 #58）。
/// 托盘线程启动并添加图标后登记自身；<see cref="Dispose"/> 向该线程投递 WM_QUIT——
/// 消息循环退出后在托盘线程内执行 Shell_NotifyIcon(NIM_DELETE)（消除幽灵图标）——并限时 Join 等待。
/// 线程消息投递函数可注入，供单元测试替代 Win32 调用（AC-05：消息循环交互可抽象可测）。
/// </summary>
public sealed class TrayQuitSignaler : IDisposable
{
    private const uint WM_QUIT = 0x0012;

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly Action<string>? _log;
    private readonly Func<uint, uint, IntPtr, IntPtr, bool> _postThreadMessage;
    private long _threadId; // 0 = 托盘线程未登记（Interlocked 读写）
    private Thread? _loopThread;
    private int _quitPosted;

    /// <param name="log">host.log 写入回调。</param>
    /// <param name="postThreadMessage">线程消息投递（默认 Win32 PostThreadMessage；测试注入替身）。</param>
    public TrayQuitSignaler(Action<string>? log = null, Func<uint, uint, IntPtr, IntPtr, bool>? postThreadMessage = null)
    {
        _log = log;
        _postThreadMessage = postThreadMessage ?? PostThreadMessage;
    }

    /// <summary>托盘消息循环线程 Id（0 = 未登记）。</summary>
    public uint LoopThreadId => (uint)Interlocked.Read(ref _threadId);

    /// <summary>托盘线程是否仍在运行（未登记视为已退出；退出即 NIM_DELETE 已在托盘线程内执行完毕）。</summary>
    public bool IsLoopThreadAlive => Volatile.Read(ref _loopThread)?.IsAlive ?? false;

    /// <summary>托盘线程登记自身（消息循环启动并添加托盘图标后调用）。</summary>
    public void RegisterLoopThread(uint threadId, Thread loopThread)
    {
        Volatile.Write(ref _loopThread, loopThread);
        Interlocked.Exchange(ref _threadId, threadId);
    }

    /// <summary>
    /// 向托盘线程投递 WM_QUIT 并限时等待其退出（超时不阻塞退出流程）。
    /// 未登记（托盘未启动）时不投递直接返回 false；幂等——只投递一次。
    /// </summary>
    /// <param name="joinTimeout">等待消息循环退出的上限。</param>
    /// <returns>是否实际投递。</returns>
    public bool SignalQuit(TimeSpan joinTimeout)
    {
        var threadId = (uint)Interlocked.Read(ref _threadId);
        if (threadId == 0 || Interlocked.Exchange(ref _quitPosted, 1) == 1)
        {
            return false;
        }

        _log?.Invoke($"托盘清理：向托盘消息循环线程（{threadId}）投递 WM_QUIT，等待循环退出（≤{joinTimeout.TotalSeconds:0.#} 秒）。");
        var posted = _postThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        if (!posted)
        {
            _log?.Invoke("托盘清理：WM_QUIT 投递失败（托盘线程可能已退出）。");
        }

        Volatile.Read(ref _loopThread)?.Join(joinTimeout);
        return posted;
    }

    /// <inheritdoc />
    public void Dispose() => SignalQuit(TimeSpan.FromSeconds(2));
}
