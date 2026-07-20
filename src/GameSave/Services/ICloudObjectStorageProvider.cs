namespace GameSave.Services;

/// <summary>
/// 云端对象存储提供者接口，供 OSS、WebDAV 等后端复用同一套云端同步流程。
/// </summary>
public interface ICloudObjectStorageProvider : IDisposable
{
    Task UploadFileAsync(string localFilePath, string objectKey, IProgress<double>? progress = null);

    Task DownloadFileAsync(string objectKey, string localFilePath, IProgress<double>? progress = null);

    Task<List<CloudObjectInfo>> ListObjectsAsync(string prefix = "");

    Task UploadContentAsync(string content, string objectKey);

    Task<string?> DownloadContentAsync(string objectKey);

    Task DeleteObjectAsync(string objectKey);

    Task<int> DeleteObjectsByPrefixAsync(string prefix);

    Task<(bool success, string message)> TestConnectionAsync();
}

/// <summary>
/// 云端对象信息（相对 Key 已去除远端根路径前缀）。
/// </summary>
public class CloudObjectInfo
{
    /// <summary>相对键（已去除 basePath 前缀）</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>完整远端键或 URL 路径</summary>
    public string FullKey { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    public long Size { get; set; }

    /// <summary>最后修改时间</summary>
    public DateTime LastModified { get; set; }
}
