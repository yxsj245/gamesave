using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GameSave.Helpers;

/// <summary>
/// 快捷方式解析信息
/// </summary>
public class ShortcutInfo
{
    /// <summary>目标路径（快捷方式指向的可执行文件路径）</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>启动参数</summary>
    public string? Arguments { get; set; }

    /// <summary>工作目录</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>图标路径</summary>
    public string? IconLocation { get; set; }
}

/// <summary>
/// Windows 快捷方式（.lnk 文件）解析辅助类。
/// 使用 COM 的 IShellLink 接口解析快捷方式文件。
/// </summary>
public static class ShortcutHelper
{
    /// <summary>
    /// 判断给定路径是否为快捷方式文件
    /// </summary>
    public static bool IsShortcut(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析快捷方式文件，返回目标路径、参数、工作目录等信息
    /// </summary>
    /// <param name="shortcutPath">快捷方式文件的完整路径</param>
    /// <returns>解析后的快捷方式信息，失败返回 null</returns>
    public static ShortcutInfo? ResolveShortcut(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
            return null;

        try
        {
            // 创建 ShellLink COM 对象
            var shellLink = (IShellLinkW)new ShellLink();
            var persistFile = (IPersistFile)shellLink;

            // 加载 .lnk 文件
            persistFile.Load(shortcutPath, 0); // STGM_READ = 0

            // 尝试解析目标路径（不显示 UI，超时1秒）
            shellLink.Resolve(IntPtr.Zero, SLR_FLAGS.SLR_NO_UI | SLR_FLAGS.SLR_NOSEARCH);

            // 获取目标路径
            var targetPathBuilder = new StringBuilder(260);
            var findData = new WIN32_FIND_DATAW();
            shellLink.GetPath(targetPathBuilder, targetPathBuilder.Capacity, ref findData, SLGP_FLAGS.SLGP_RAWPATH);
            var targetPath = targetPathBuilder.ToString();

            if (string.IsNullOrWhiteSpace(targetPath))
                return null;

            // 获取启动参数
            var argsBuilder = new StringBuilder(1024);
            shellLink.GetArguments(argsBuilder, argsBuilder.Capacity);
            var arguments = argsBuilder.ToString();

            // 获取工作目录
            var workDirBuilder = new StringBuilder(260);
            shellLink.GetWorkingDirectory(workDirBuilder, workDirBuilder.Capacity);
            var workingDirectory = workDirBuilder.ToString();

            // 获取图标路径
            var iconBuilder = new StringBuilder(260);
            shellLink.GetIconLocation(iconBuilder, iconBuilder.Capacity, out _);
            var iconLocation = iconBuilder.ToString();

            // 释放 COM 对象
            Marshal.ReleaseComObject(shellLink);

            return new ShortcutInfo
            {
                TargetPath = targetPath,
                Arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                IconLocation = string.IsNullOrWhiteSpace(iconLocation) ? null : iconLocation
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShortcutHelper] 解析快捷方式失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 如果路径是快捷方式，解析并返回实际目标路径；否则原样返回
    /// </summary>
    public static string ResolvePathIfShortcut(string path)
    {
        if (IsShortcut(path))
        {
            var info = ResolveShortcut(path);
            if (info != null && !string.IsNullOrWhiteSpace(info.TargetPath))
                return info.TargetPath;
        }
        return path;
    }

    #region COM 接口定义

    // ShellLink CLSID
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // IShellLinkW 接口
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cch, ref WIN32_FIND_DATAW pfd, SLGP_FLAGS fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, SLR_FLAGS fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [Flags]
    private enum SLR_FLAGS : uint
    {
        SLR_NO_UI = 0x0001,
        SLR_NOSEARCH = 0x0010,
    }

    [Flags]
    private enum SLGP_FLAGS : uint
    {
        SLGP_RAWPATH = 0x0004,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    #endregion
}
