namespace GameSave.Models;

/// <summary>
/// 云存储配置模型
/// </summary>
public class CloudConfig
{
    /// <summary>配置唯一ID</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>显示名称（如 "我的坚果云"）</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>云存储类型</summary>
    public CloudProviderType ProviderType { get; set; } = CloudProviderType.WebDav;

    /// <summary>服务器地址（WebDAV/FTP）</summary>
    public string? ServerUrl { get; set; }

    /// <summary>用户名</summary>
    public string? Username { get; set; }

    /// <summary>密码（加密存储）</summary>
    public string? Password { get; set; }

    /// <summary>远端存储根路径</summary>
    public string RemoteBasePath { get; set; } = "/GameSave";

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>云存储提供商类型</summary>
public enum CloudProviderType
{
    /// <summary>WebDAV（坚果云、Nextcloud 等）</summary>
    WebDav,
    /// <summary>FTP</summary>
    Ftp,
    /// <summary>SFTP</summary>
    Sftp,
    /// <summary>本地文件夹（用于测试）</summary>
    LocalFolder
}
