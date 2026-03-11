using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace GameSave.Helpers
{
    /// <summary>
    /// 系统托盘图标管理器
    /// 使用 Win32 Shell_NotifyIcon API 实现托盘图标功能
    /// </summary>
    public class TrayIconHelper : IDisposable
    {
        #region Win32 API 声明

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y,
            int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        // 常量定义
        private const uint NIM_ADD = 0x00;
        private const uint NIM_MODIFY = 0x01;
        private const uint NIM_DELETE = 0x02;

        private const uint NIF_MESSAGE = 0x01;
        private const uint NIF_ICON = 0x02;
        private const uint NIF_TIP = 0x04;
        private const uint NIF_INFO = 0x10;

        private const uint WM_USER = 0x0400;
        private const uint WM_TRAYICON = WM_USER + 1;
        private const uint WM_COMMAND = 0x0111;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_DESTROY = 0x0002;

        private const uint MF_STRING = 0x00;
        private const uint MF_SEPARATOR = 0x0800;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_NONOTIFY = 0x0080;

        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;

        // 菜单命令 ID
        private const uint MENU_SHOW = 1001;
        private const uint MENU_EXIT = 1002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
        }

        #endregion

        private IntPtr _messageWindowHandle;
        private NOTIFYICONDATA _notifyIconData;
        private WndProcDelegate? _wndProc; // 防止 GC 回收委托
        private IntPtr _hIcon;
        private bool _disposed;

        /// <summary>
        /// 请求显示主窗口时触发
        /// </summary>
        public event Action? ShowWindowRequested;

        /// <summary>
        /// 请求退出应用时触发
        /// </summary>
        public event Action? ExitRequested;

        /// <summary>
        /// 初始化系统托盘图标
        /// </summary>
        public void Initialize()
        {
            // 创建消息窗口用于接收托盘图标的消息
            CreateMessageWindow();

            // 从当前 exe 提取图标
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                _hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
            }

            // 如果无法获取 exe 图标，使用默认应用程序图标
            if (_hIcon == IntPtr.Zero)
            {
                _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
            }

            // 创建托盘图标
            _notifyIconData = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _messageWindowHandle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "GameSave Manager - 游戏存档管理器",
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };

            Shell_NotifyIcon(NIM_ADD, ref _notifyIconData);
        }

        /// <summary>
        /// 显示气泡通知
        /// </summary>
        /// <param name="title">通知标题</param>
        /// <param name="message">通知内容</param>
        public void ShowBalloonTip(string title, string message)
        {
            _notifyIconData.uFlags = NIF_INFO;
            _notifyIconData.szInfoTitle = title;
            _notifyIconData.szInfo = message;
            _notifyIconData.dwInfoFlags = 0x01; // NIIF_INFO

            Shell_NotifyIcon(NIM_MODIFY, ref _notifyIconData);

            // 恢复标准配置
            _notifyIconData.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        }

        /// <summary>
        /// 创建隐藏的消息窗口来接收托盘图标事件
        /// </summary>
        private void CreateMessageWindow()
        {
            _wndProc = WndProc;
            var hInstance = GetModuleHandle(null);

            var wndClass = new WNDCLASS
            {
                lpfnWndProc = _wndProc,
                hInstance = hInstance,
                lpszClassName = "GameSaveTrayIconWindow"
            };

            RegisterClass(ref wndClass);

            _messageWindowHandle = CreateWindowEx(
                0, "GameSaveTrayIconWindow", "GameSave Tray",
                0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        }

        /// <summary>
        /// 消息窗口的窗口过程，处理托盘图标事件
        /// </summary>
        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
        {
            if (uMsg == WM_TRAYICON)
            {
                var mouseMsg = (uint)lParam.ToInt32();
                switch (mouseMsg)
                {
                    case WM_LBUTTONDBLCLK:
                        // 双击托盘图标 -> 显示主窗口
                        ShowWindowRequested?.Invoke();
                        break;

                    case WM_RBUTTONUP:
                        // 右键点击 -> 显示右键菜单
                        ShowContextMenu();
                        break;
                }
            }
            else if (uMsg == WM_COMMAND)
            {
                var menuId = (uint)(wParam.ToInt32() & 0xFFFF);
                switch (menuId)
                {
                    case MENU_SHOW:
                        ShowWindowRequested?.Invoke();
                        break;
                    case MENU_EXIT:
                        ExitRequested?.Invoke();
                        break;
                }
            }

            return DefWindowProc(hWnd, uMsg, wParam, lParam);
        }

        /// <summary>
        /// 显示托盘图标的右键菜单
        /// </summary>
        private void ShowContextMenu()
        {
            var hMenu = CreatePopupMenu();
            AppendMenu(hMenu, MF_STRING, MENU_SHOW, "显示主窗口");
            AppendMenu(hMenu, MF_SEPARATOR, 0, string.Empty);
            AppendMenu(hMenu, MF_STRING, MENU_EXIT, "退出");

            GetCursorPos(out var pt);

            // 设置前台窗口以确保菜单能够正确关闭
            SetForegroundWindow(_messageWindowHandle);

            var cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_NONOTIFY,
                pt.X, pt.Y, 0, _messageWindowHandle, IntPtr.Zero);

            DestroyMenu(hMenu);

            // 根据用户选择的菜单项执行对应操作
            switch ((uint)cmd)
            {
                case MENU_SHOW:
                    ShowWindowRequested?.Invoke();
                    break;
                case MENU_EXIT:
                    ExitRequested?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// 释放托盘图标资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 删除托盘图标
            Shell_NotifyIcon(NIM_DELETE, ref _notifyIconData);

            // 销毁消息窗口
            if (_messageWindowHandle != IntPtr.Zero)
            {
                DestroyWindow(_messageWindowHandle);
                _messageWindowHandle = IntPtr.Zero;
            }

            // 释放图标资源
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        ~TrayIconHelper()
        {
            Dispose();
        }
    }
}
