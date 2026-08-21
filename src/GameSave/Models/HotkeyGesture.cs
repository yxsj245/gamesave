namespace GameSave.Models;

/// <summary>
/// 全局快捷键修饰键。
/// 数值与 Win32 RegisterHotKey 的 MOD_* 常量保持一致。
/// </summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

/// <summary>
/// 可持久化的全局快捷键组合。
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int DefaultVirtualKey = 0x74; // F5

    public static HotkeyGesture Default => new(
        HotkeyModifiers.Control | HotkeyModifiers.Shift,
        DefaultVirtualKey);

    /// <summary>
    /// 校验是否为应用允许注册的组合。
    /// </summary>
    public bool IsValid(out string message)
    {
        const HotkeyModifiers supportedModifiers =
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

        if ((Modifiers & HotkeyModifiers.Windows) != 0)
        {
            message = "Windows 键组合由系统保留，请使用 Ctrl、Alt 或 Shift";
            return false;
        }

        if ((Modifiers & ~supportedModifiers) != 0 || Modifiers == HotkeyModifiers.None)
        {
            message = "快捷键至少需要包含 Ctrl、Alt 或 Shift 中的一个修饰键";
            return false;
        }

        bool isLetter = VirtualKey is >= 0x41 and <= 0x5A;
        bool isDigit = VirtualKey is >= 0x30 and <= 0x39;
        bool isFunctionKey = VirtualKey is >= 0x70 and <= 0x7A; // F1-F11，F12 保留给调试器
        if (!isLetter && !isDigit && !isFunctionKey)
        {
            message = "仅支持 A-Z、0-9 或 F1-F11 作为快捷键主键";
            return false;
        }

        message = string.Empty;
        return true;
    }

    /// <summary>
    /// 转换为设置页显示文本。
    /// </summary>
    public string ToDisplayString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");

        parts.Add(GetVirtualKeyDisplayName(VirtualKey));
        return string.Join("+", parts);
    }

    private static string GetVirtualKeyDisplayName(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
            return ((char)virtualKey).ToString();

        if (virtualKey is >= 0x30 and <= 0x39)
            return ((char)virtualKey).ToString();

        if (virtualKey is >= 0x70 and <= 0x7A)
            return $"F{virtualKey - 0x6F}";

        return $"0x{virtualKey:X2}";
    }
}
