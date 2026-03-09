using Aliyun.OSS;
using Aliyun.OSS.Common;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 阿里云 OSS 存储操作封装
/// 提供文件上传、下载、列表查询、删除和连接测试等基础操作
/// </summary>
public class OssStorageProvider : IDisposable
{
    private readonly OssClient _client;
    private readonly string _bucketName;
    private readonly string _basePath;

    /// <summary>
    /// 创建 OSS 存储提供者
    /// </summary>
    /// <param name="config">云存储配置（需包含 OSS 专用字段）</param>
    public OssStorageProvider(CloudConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
            throw new ArgumentException("OSS Endpoint 不能为空");
        if (string.IsNullOrWhiteSpace(config.AccessKeyId))
            throw new ArgumentException("AccessKeyId 不能为空");
        if (string.IsNullOrWhiteSpace(config.AccessKeySecret))
            throw new ArgumentException("AccessKeySecret 不能为空");
        if (string.IsNullOrWhiteSpace(config.BucketName))
            throw new ArgumentException("BucketName 不能为空");

        _client = new OssClient(config.Endpoint, config.AccessKeyId, config.AccessKeySecret);
        _bucketName = config.BucketName;
        // 确保 basePath 不以 / 开头，且以 / 结尾
        _basePath = NormalizePath(config.RemoteBasePath);
    }

    /// <summary>
    /// 上传本地文件到 OSS
    /// </summary>
    /// <param name="localFilePath">本地文件路径</param>
    /// <param name="ossKey">OSS 对象键（不含 basePath 前缀，如 gameId/xxx.tar）</param>
    public Task UploadFileAsync(string localFilePath, string ossKey)
    {
        return Task.Run(() =>
        {
            var fullKey = _basePath + ossKey;
            _client.PutObject(_bucketName, fullKey, localFilePath);
            System.Diagnostics.Debug.WriteLine($"[OSS] 上传完成: {fullKey}");
        });
    }

    /// <summary>
    /// 从 OSS 下载文件到本地
    /// </summary>
    /// <param name="ossKey">OSS 对象键（不含 basePath 前缀）</param>
    /// <param name="localFilePath">本地保存路径</param>
    public Task DownloadFileAsync(string ossKey, string localFilePath)
    {
        return Task.Run(() =>
        {
            var fullKey = _basePath + ossKey;
            var result = _client.GetObject(_bucketName, fullKey);

            // 确保本地目录存在
            var dir = Path.GetDirectoryName(localFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var responseStream = result.Content;
            using var fileStream = File.Create(localFilePath);
            responseStream.CopyTo(fileStream);

            System.Diagnostics.Debug.WriteLine($"[OSS] 下载完成: {fullKey} -> {localFilePath}");
        });
    }

    /// <summary>
    /// 列出指定前缀下的所有对象
    /// </summary>
    /// <param name="prefix">前缀（不含 basePath，如 gameId/）</param>
    /// <returns>对象信息列表（Key 已去除 basePath 前缀）</returns>
    public Task<List<OssObjectInfo>> ListObjectsAsync(string prefix = "")
    {
        return Task.Run(() =>
        {
            var results = new List<OssObjectInfo>();
            var fullPrefix = _basePath + prefix;
            string? nextMarker = null;

            do
            {
                var request = new ListObjectsRequest(_bucketName)
                {
                    Prefix = fullPrefix,
                    MaxKeys = 1000,
                    Marker = nextMarker
                };

                var listing = _client.ListObjects(request);

                foreach (var summary in listing.ObjectSummaries)
                {
                    // 跳过"目录"对象（以 / 结尾且大小为 0）
                    if (summary.Key.EndsWith("/") && summary.Size == 0)
                        continue;

                    results.Add(new OssObjectInfo
                    {
                        Key = summary.Key.StartsWith(_basePath)
                            ? summary.Key[_basePath.Length..]
                            : summary.Key,
                        FullKey = summary.Key,
                        Size = summary.Size,
                        LastModified = summary.LastModified
                    });
                }

                nextMarker = listing.NextMarker;
            }
            while (!string.IsNullOrEmpty(nextMarker));

            return results;
        });
    }

    /// <summary>
    /// 删除 OSS 上的对象
    /// </summary>
    /// <param name="ossKey">OSS 对象键（不含 basePath 前缀）</param>
    public Task DeleteObjectAsync(string ossKey)
    {
        return Task.Run(() =>
        {
            var fullKey = _basePath + ossKey;
            _client.DeleteObject(_bucketName, fullKey);
            System.Diagnostics.Debug.WriteLine($"[OSS] 已删除: {fullKey}");
        });
    }

    /// <summary>
    /// 测试 OSS 连接是否正常
    /// </summary>
    /// <returns>连接成功返回 true</returns>
    public Task<(bool success, string message)> TestConnectionAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                // 尝试检查 Bucket 是否存在
                var exist = _client.DoesBucketExist(_bucketName);
                if (!exist)
                    return (false, $"存储桶 \"{_bucketName}\" 不存在");

                return (true, "连接成功");
            }
            catch (OssException ex)
            {
                return (false, $"OSS 错误: {ex.ErrorCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"连接失败: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 规范化路径：去掉开头的 /，确保以 / 结尾
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        path = path.TrimStart('/');
        if (!path.EndsWith('/'))
            path += "/";

        return path;
    }

    public void Dispose()
    {
        // OssClient 不需要显式 Dispose，但保留接口以便将来扩展
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// OSS 对象信息（简化模型）
/// </summary>
public class OssObjectInfo
{
    /// <summary>相对键（已去除 basePath 前缀）</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>完整的 OSS 对象键</summary>
    public string FullKey { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    public long Size { get; set; }

    /// <summary>最后修改时间</summary>
    public DateTime LastModified { get; set; }
}
