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
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
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

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    private Thread? _thread;
    private IntPtr _hwnd;
    private uint _threadId;
    private WndProcDelegate? _wndProc; // 防止委托被 GC
    private bool _disposed;

    /// <summary>启动托盘（独立消息循环线程）。</summary>
    /// <param name="listenUrl">界面地址（双击 / 菜单打开）。</param>
    /// <param name="shutdown">退出回调（停止宿主）。</param>
    public void Start(string listenUrl, Func<Task> shutdown)
    {
        _thread = new Thread(() => RunTrayLoop(listenUrl, shutdown))
        {
            IsBackground = true,
        };
        _thread.Start();
    }

    private void RunTrayLoop(string listenUrl, Func<Task> shutdown)
    {
        _threadId = GetCurrentThreadId();
        var instance = GetModuleHandle(null);
        _wndProc = (hWnd, msg, wParam, lParam) => WndProc(hWnd, msg, wParam, lParam, listenUrl, shutdown);

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

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, string listenUrl, Func<Task> shutdown)
    {
        if (msg == WM_USER + 1)
        {
            var mouseMsg = (uint)lParam.ToInt64();
            if (mouseMsg == WM_RBUTTONUP)
            {
                ShowMenu(listenUrl, shutdown);
            }
            else if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                OpenBrowser(listenUrl);
            }

            return IntPtr.Zero;
        }

        if (msg == WM_QUIT)
        {
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu(string listenUrl, Func<Task> shutdown)
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
            OpenBrowser(listenUrl);
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
            if (_threadId != 0)
            {
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _thread?.Join(TimeSpan.FromSeconds(2));
            }
        }
        catch
        {
            // 清理失败不影响宿主退出
        }
    }
}
