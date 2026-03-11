namespace GameSave.Helpers;

/// <summary>
/// 路径环境变量工具类
/// 用于将绝对路径替换为 Windows 系统环境变量形式，或将环境变量路径展开为绝对路径。
/// 支持多设备间导入导出时路径自动适配。
/// </summary>
public static class PathEnvironmentHelper
{
    /// <summary>
    /// 环境变量映射表（按路径长度降序排列，优先匹配最具体的路径）
    /// </summary>
    private static readonly (string VarName, Environment.SpecialFolder? Folder, string? EnvKey)[] EnvMappings =
    [
        // 优先匹配更具体的路径（子目录优先于父目录）
        ("%LOCALAPPDATA%", Environment.SpecialFolder.LocalApplicationData, null),
        ("%APPDATA%", Environment.SpecialFolder.ApplicationData, null),
        ("%USERPROFILE%", Environment.SpecialFolder.UserProfile, null),
        ("%PUBLIC%", null, "PUBLIC"),
        ("%PROGRAMFILES%", Environment.SpecialFolder.ProgramFiles, null),
        ("%PROGRAMFILES(X86)%", Environment.SpecialFolder.ProgramFilesX86, null),
        ("%PROGRAMDATA%", Environment.SpecialFolder.CommonApplicationData, null),
        ("%SYSTEMDRIVE%", null, "SystemDrive"),
    ];

    /// <summary>
    /// 将绝对路径替换为环境变量形式。
    /// 优先匹配最长（最具体）的环境变量路径。
    /// 例如：C:\Users\xxx\AppData\Roaming\Game → %APPDATA%\Game
    /// </summary>
    /// <param name="absolutePath">绝对路径</param>
    /// <returns>替换后的路径（如果匹配到环境变量）或原始路径</returns>
    public static string ReplaceWithEnvVariables(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return absolutePath;

        // 标准化路径分隔符
        var normalizedPath = absolutePath.Replace('/', '\\');

        string? bestMatch = null;
        string? bestVarName = null;
        int bestMatchLength = 0;

        foreach (var (varName, folder, envKey) in EnvMappings)
        {
            string? envValue = null;

            if (folder.HasValue)
            {
                envValue = Environment.GetFolderPath(folder.Value);
            }
            else if (!string.IsNullOrEmpty(envKey))
            {
                envValue = Environment.GetEnvironmentVariable(envKey);
            }

            if (string.IsNullOrEmpty(envValue))
                continue;

            envValue = envValue.Replace('/', '\\');

            // 检查路径是否以该环境变量值开头（忽略大小写）
            if (normalizedPath.StartsWith(envValue, StringComparison.OrdinalIgnoreCase) &&
                envValue.Length > bestMatchLength)
            {
                // 确保匹配的是完整路径段（后面是分隔符或路径结束）
                if (normalizedPath.Length == envValue.Length ||
                    normalizedPath[envValue.Length] == '\\')
                {
                    bestMatch = envValue;
                    bestVarName = varName;
                    bestMatchLength = envValue.Length;
                }
            }
        }

        if (bestMatch != null && bestVarName != null)
        {
            return bestVarName + normalizedPath[bestMatch.Length..];
        }

        return absolutePath;
    }

    /// <summary>
    /// 将包含环境变量的路径展开为绝对路径。
    /// 例如：%APPDATA%\Game → C:\Users\xxx\AppData\Roaming\Game
    /// </summary>
    /// <param name="path">可能包含环境变量的路径</param>
    /// <returns>展开后的绝对路径</returns>
    public static string ExpandEnvVariables(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// 获取显示用的路径（展开环境变量后的绝对路径）。
    /// 如果路径不包含环境变量，直接返回原始路径。
    /// </summary>
    /// <param name="path">可能包含环境变量的路径</param>
    /// <returns>展开后的绝对路径</returns>
    public static string GetDisplayPath(string path)
    {
        return ExpandEnvVariables(path);
    }
}
