using System.ComponentModel;
using System.Runtime.InteropServices;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 全局快捷键注册服务抽象，便于设置页验证和测试。
/// </summary>
public interface IGlobalHotkeyService
{
    bool IsRegistered { get; }
    HotkeyGesture? RegisteredGesture { get; }
    string StatusMessage { get; }
    bool TryUpdateRegistration(bool enabled, HotkeyGesture gesture, out string message);
}

/// <summary>
/// 使用 Win32 RegisterHotKey 和隐藏消息窗口接收系统级快捷键。
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const int HotkeyId = 0x4753;
    private static readonly IntPtr HwndMessage = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WndClass wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string className, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint style;
        public WndProcDelegate wndProc;
        public int classExtra;
        public int windowExtra;
        public IntPtr instance;
        public IntPtr icon;
        public IntPtr cursor;
        public IntPtr background;
        public string? menuName;
        public string className;
    }

    private readonly string _windowClassName = $"GameSaveHotkeyWindow_{Guid.NewGuid():N}";
    private readonly IntPtr _moduleHandle;
    private readonly WndProcDelegate _wndProc;
    private IntPtr _messageWindowHandle;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _moduleHandle = GetModuleHandleW(null);
        _wndProc = WndProc;
        CreateMessageWindow();
    }

    public event Action? HotkeyPressed;

    public bool IsRegistered { get; private set; }

    public HotkeyGesture? RegisteredGesture { get; private set; }

    public string StatusMessage { get; private set; } = "快捷键尚未注册";

    public bool TryUpdateRegistration(bool enabled, HotkeyGesture gesture, out string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!gesture.IsValid(out message))
        {
            StatusMessage = message;
            return false;
        }

        var oldGesture = RegisteredGesture;
        var oldWasRegistered = IsRegistered;
        UnregisterCurrent();

        if (!enabled)
        {
            RegisteredGesture = gesture;
            StatusMessage = "快捷存档快捷键已关闭";
            message = StatusMessage;
            return true;
        }

        if (TryRegister(gesture, out message))
        {
            RegisteredGesture = gesture;
            IsRegistered = true;
            StatusMessage = $"已注册：{gesture.ToDisplayString()}";
            message = StatusMessage;
            return true;
        }

        // 新组合注册失败时尽量恢复旧组合，避免设置失败后原快捷键也失效。
        if (oldWasRegistered && oldGesture.HasValue && TryRegister(oldGesture.Value, out _))
        {
            RegisteredGesture = oldGesture;
            IsRegistered = true;
        }

        StatusMessage = message;
        return false;
    }

    private bool TryRegister(HotkeyGesture gesture, out string message)
    {
        var modifiers = (uint)gesture.Modifiers | ModNoRepeat;
        if (RegisterHotKey(_messageWindowHandle, HotkeyId, modifiers, (uint)gesture.VirtualKey))
        {
            message = string.Empty;
            return true;
        }

        var errorCode = Marshal.GetLastWin32Error();
        var detail = new Win32Exception(errorCode).Message;
        message = errorCode == 1409
            ? $"快捷键 {gesture.ToDisplayString()} 已被其他程序占用，请更换组合"
            : $"快捷键注册失败：{detail}（错误码 {errorCode}）";
        return false;
    }

    private void CreateMessageWindow()
    {
        var wndClass = new WndClass
        {
            wndProc = _wndProc,
            instance = _moduleHandle,
            className = _windowClassName
        };

        if (RegisterClassW(ref wndClass) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建快捷键消息窗口类失败");

        _messageWindowHandle = CreateWindowExW(
            0,
            _windowClassName,
            "GameSave Hotkey",
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            _moduleHandle,
            IntPtr.Zero);

        if (_messageWindowHandle == IntPtr.Zero)
        {
            var errorCode = Marshal.GetLastWin32Error();
            UnregisterClassW(_windowClassName, _moduleHandle);
            throw new Win32Exception(errorCode, "创建快捷键消息窗口失败");
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
            HotkeyPressed?.Invoke();

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    private void UnregisterCurrent()
    {
        if (IsRegistered && _messageWindowHandle != IntPtr.Zero)
            UnregisterHotKey(_messageWindowHandle, HotkeyId);

        IsRegistered = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        UnregisterCurrent();
        if (_messageWindowHandle != IntPtr.Zero)
        {
            DestroyWindow(_messageWindowHandle);
            _messageWindowHandle = IntPtr.Zero;
        }

        UnregisterClassW(_windowClassName, _moduleHandle);
    }
}
