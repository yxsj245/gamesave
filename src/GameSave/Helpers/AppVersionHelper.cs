using System.Reflection;

namespace GameSave.Helpers;

/// <summary>
/// 应用版本号帮助类
/// 从程序集信息中读取版本号（由构建时 MSBuild 属性注入）
/// </summary>
public static class AppVersionHelper
{
    /// <summary>
    /// 获取应用版本号字符串（如 "1.2.0"）
    /// </summary>
    public static string GetVersion()
    {
        // 优先使用 InformationalVersion（对应 csproj 中的 Version 属性，支持语义化版本）
        var infoVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrEmpty(infoVersion))
        {
            // InformationalVersion 可能包含 +commitHash 后缀，只取版本号部分
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }

        // 回退到 FileVersion
        var fileVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

        if (!string.IsNullOrEmpty(fileVersion))
        {
            return fileVersion;
        }

        // 最终回退
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion != null
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : "1.0.0";
    }
}
