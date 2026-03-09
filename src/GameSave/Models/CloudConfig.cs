namespace GameSave.Models;

/// <summary>
/// 云存储配置模型
/// </summary>
public class CloudConfig
{
    /// <summary>配置唯一ID</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>显示名称（如 "我的阿里云OSS"）</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>云存储类型</summary>
    public CloudProviderType ProviderType { get; set; } = CloudProviderType.AliyunOss;

    /// <summary>服务器地址（WebDAV/FTP 等协议用）</summary>
    public string? ServerUrl { get; set; }

    /// <summary>用户名（WebDAV/FTP/SFTP 协议用）</summary>
    public string? Username { get; set; }

    /// <summary>密码（WebDAV/FTP/SFTP 协议用，加密存储）</summary>
    public string? Password { get; set; }

    /// <summary>远端存储根路径，默认 GameSave</summary>
    public string RemoteBasePath { get; set; } = "GameSave";

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    // ====== 阿里云 OSS 专用字段 ======

    /// <summary>阿里云 OSS 端点（如 oss-cn-hangzhou.aliyuncs.com）</summary>
    public string? Endpoint { get; set; }

    /// <summary>OSS 存储桶名称</summary>
    public string? BucketName { get; set; }

    /// <summary>阿里云 AccessKey ID</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>阿里云 AccessKey Secret</summary>
    public string? AccessKeySecret { get; set; }
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
    LocalFolder,
    /// <summary>阿里云 OSS</summary>
    AliyunOss
}
