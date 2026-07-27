using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using GameSave.Models;

namespace GameSave.Services;

/// <summary>
/// 通用 WebDAV 存储提供者。
/// </summary>
public class WebDavStorageProvider : ICloudObjectStorageProvider
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly HttpMethod PropfindMethod = new("PROPFIND");
    private static readonly HttpMethod MkcolMethod = new("MKCOL");
    private static readonly HttpClient SharedClient = new();

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly AuthenticationHeaderValue _authorizationHeader;
    private readonly Uri _serverUri;
    private readonly string _remoteBasePath;
    private readonly string _serverPathPrefix;
    private readonly string _fullBasePathPrefix;
    private bool _disposed;

    public WebDavStorageProvider(CloudConfig config)
        : this(config, SharedClient, ownsClient: false)
    {
    }

    public WebDavStorageProvider(CloudConfig config, HttpClient httpClient)
        : this(config, httpClient, ownsClient: false)
    {
    }

    private WebDavStorageProvider(CloudConfig config, HttpClient httpClient, bool ownsClient)
    {
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
            throw new ArgumentException("WebDAV 服务器地址不能为空");
        if (string.IsNullOrWhiteSpace(config.Username))
            throw new ArgumentException("WebDAV 用户名不能为空");
        if (string.IsNullOrWhiteSpace(config.Password))
            throw new ArgumentException("WebDAV 密码不能为空");

        _serverUri = new Uri(config.ServerUrl.Trim().TrimEnd('/') + "/");
        _remoteBasePath = NormalizeRelativePath(config.RemoteBasePath);
        _serverPathPrefix = EnsureLeadingAndTrailingSlash(Uri.UnescapeDataString(_serverUri.AbsolutePath));
        _fullBasePathPrefix = EnsureLeadingAndTrailingSlash(CombineAbsolutePath(_serverPathPrefix, _remoteBasePath));
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
        _authorizationHeader = new AuthenticationHeaderValue("Basic", auth);
        _client = httpClient;
        _ownsClient = ownsClient;
    }

    public async Task UploadFileAsync(string localFilePath, string objectKey, IProgress<double>? progress = null)
    {
        if (!File.Exists(localFilePath))
            throw new FileNotFoundException($"本地文件不存在: {localFilePath}");

        await EnsureParentCollectionsAsync(objectKey);

        progress?.Report(0);
        await using var fileStream = File.OpenRead(localFilePath);
        using var content = progress == null
            ? new StreamContent(fileStream)
            : new StreamContent(new ProgressStream(fileStream, progress));

        using var request = CreateRequest(HttpMethod.Put, BuildObjectUri(objectKey));
        request.Content = content;
        using var response = await _client.SendAsync(request);
        EnsureSuccess(response, "上传文件");
        progress?.Report(100);
    }

    public async Task DownloadFileAsync(string objectKey, string localFilePath, IProgress<double>? progress = null)
    {
        using var request = CreateRequest(HttpMethod.Get, BuildObjectUri(objectKey));
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response, "下载文件");

        var dir = Path.GetDirectoryName(localFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var totalRead = 0L;
        var buffer = new byte[81920];

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(localFilePath);

        progress?.Report(0);
        int read;
        while ((read = await responseStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            totalRead += read;
            if (totalBytes > 0)
                progress?.Report(Math.Min(100, (double)totalRead / totalBytes * 100));
        }
        progress?.Report(100);
    }

    public async Task<List<CloudObjectInfo>> ListObjectsAsync(string prefix = "")
    {
        var normalizedPrefix = NormalizeRelativePath(prefix);
        var rootKey = string.IsNullOrEmpty(normalizedPrefix) ? "" : EnsureTrailingSlash(normalizedPrefix);
        var results = new List<CloudObjectInfo>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await ListCollectionRecursiveAsync(rootKey, results, visited);
        return results
            .Where(x => string.IsNullOrEmpty(normalizedPrefix) || x.Key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task UploadContentAsync(string content, string objectKey)
    {
        await EnsureParentCollectionsAsync(objectKey);
        using var httpContent = new StringContent(content, Encoding.UTF8, "application/json");
        using var request = CreateRequest(HttpMethod.Put, BuildObjectUri(objectKey));
        request.Content = httpContent;
        using var response = await _client.SendAsync(request);
        EnsureSuccess(response, "上传内容");
    }

    public async Task<string?> DownloadContentAsync(string objectKey)
    {
        using var request = CreateRequest(HttpMethod.Get, BuildObjectUri(objectKey));
        using var response = await _client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        EnsureSuccess(response, "下载内容");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task DeleteObjectAsync(string objectKey)
    {
        using var request = CreateRequest(HttpMethod.Delete, BuildObjectUri(objectKey));
        using var response = await _client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        EnsureSuccess(response, "删除对象");
    }

    public async Task<int> DeleteObjectsByPrefixAsync(string prefix)
    {
        var objects = await ListObjectsAsync(prefix);
        var deleted = 0;
        foreach (var obj in objects)
        {
            await DeleteObjectAsync(obj.Key);
            deleted++;
        }
        await DeleteCollectionIfEmptyAsync(prefix);
        return deleted;
    }

    public async Task<(bool success, string message)> TestConnectionAsync()
    {
        try
        {
            await EnsureBaseCollectionsAsync();
            using var response = await SendPropfindAsync("");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (false, "远端路径不存在或无权限访问");

            EnsureSuccess(response, "测试连接");
            return (true, "连接成功");
        }
        catch (Exception ex)
        {
            return (false, $"WebDAV 连接失败: {ex.Message}");
        }
    }

    private async Task ListCollectionRecursiveAsync(string collectionKey, List<CloudObjectInfo> results, HashSet<string> visited)
    {
        var collectionUri = BuildObjectUri(collectionKey);
        var visitedKey = collectionUri.ToString();
        if (!visited.Add(visitedKey))
            return;

        using var response = await SendPropfindAsync(collectionKey);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        EnsureSuccess(response, "列出对象");
        var xml = await response.Content.ReadAsStringAsync();
        foreach (var entry in ParseMultiStatus(xml))
        {
            if (string.IsNullOrEmpty(entry.Key))
                continue;

            if (entry.IsCollection)
            {
                if (!PathsEqual(entry.Key, collectionKey))
                    await ListCollectionRecursiveAsync(EnsureTrailingSlash(entry.Key), results, visited);
                continue;
            }

            results.Add(new CloudObjectInfo
            {
                Key = entry.Key,
                FullKey = entry.FullPath,
                Size = entry.Size,
                LastModified = entry.LastModified
            });
        }
    }

    private async Task<HttpResponseMessage> SendPropfindAsync(string collectionKey)
    {
        var request = CreateRequest(PropfindMethod, BuildObjectUri(collectionKey, isCollection: true));
        request.Headers.Add("Depth", "1");
        request.Content = new StringContent(
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <d:propfind xmlns:d="DAV:">
              <d:prop>
                <d:resourcetype />
                <d:getcontentlength />
                <d:getlastmodified />
              </d:prop>
            </d:propfind>
            """,
            Encoding.UTF8,
            "application/xml");
        return await _client.SendAsync(request);
    }

    private IEnumerable<WebDavEntry> ParseMultiStatus(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            yield break;

        var doc = XDocument.Parse(xml);
        foreach (var response in doc.Descendants(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var fullPath = Uri.UnescapeDataString(new Uri(_serverUri, href).AbsolutePath);
            var key = ToObjectKey(fullPath);
            var prop = response.Descendants(Dav + "prop").FirstOrDefault();
            var isCollection = prop?.Element(Dav + "resourcetype")?.Element(Dav + "collection") != null
                || fullPath.EndsWith('/');

            long.TryParse(prop?.Element(Dav + "getcontentlength")?.Value, out var size);
            var modified = ParseHttpDate(prop?.Element(Dav + "getlastmodified")?.Value);

            yield return new WebDavEntry(key, fullPath, isCollection, size, modified);
        }
    }

    private async Task EnsureBaseCollectionsAsync()
    {
        var segments = _remoteBasePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var key = string.Join('/', segments.Take(i + 1));
            await CreateCollectionAsync(key, isBasePath: true);
        }
    }

    private async Task EnsureParentCollectionsAsync(string objectKey)
    {
        await EnsureBaseCollectionsAsync();

        var normalizedKey = NormalizeRelativePath(objectKey);
        var slashIndex = normalizedKey.LastIndexOf('/');
        if (slashIndex <= 0)
            return;

        var dirSegments = normalizedKey[..slashIndex].Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < dirSegments.Length; i++)
        {
            var key = string.Join('/', dirSegments.Take(i + 1));
            await CreateCollectionAsync(key, isBasePath: false);
        }
    }

    private async Task CreateCollectionAsync(string relativePath, bool isBasePath)
    {
        var uri = isBasePath
            ? BuildUriFromRemotePath(relativePath, isCollection: true)
            : BuildObjectUri(relativePath, isCollection: true);

        using var request = CreateRequest(MkcolMethod, uri);
        using var response = await _client.SendAsync(request);
        if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.MethodNotAllowed)
            return;

        EnsureSuccess(response, "创建远端目录");
    }

    private async Task DeleteCollectionIfEmptyAsync(string prefix)
    {
        var collectionKey = NormalizeRelativePath(prefix);
        if (string.IsNullOrEmpty(collectionKey))
            return;

        using var request = CreateRequest(HttpMethod.Delete, BuildObjectUri(collectionKey, isCollection: true));
        using var response = await _client.SendAsync(request);
        if (response.IsSuccessStatusCode ||
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.MethodNotAllowed)
            return;

        EnsureSuccess(response, "删除远端空目录");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = _authorizationHeader;
        return request;
    }

    private Uri BuildObjectUri(string objectKey, bool isCollection = false)
    {
        isCollection = isCollection || objectKey.Replace('\\', '/').EndsWith('/');
        var normalizedKey = NormalizeRelativePath(objectKey);
        var path = string.IsNullOrEmpty(_remoteBasePath)
            ? normalizedKey
            : CombinePath(_remoteBasePath, normalizedKey);
        return BuildUriFromRemotePath(path, isCollection);
    }

    private Uri BuildUriFromRemotePath(string remotePath, bool isCollection)
    {
        var encoded = string.Join('/', NormalizeRelativePath(remotePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var relative = isCollection && !encoded.EndsWith('/') ? encoded + "/" : encoded;
        return new Uri(_serverUri, relative);
    }

    private string ToObjectKey(string fullPath)
    {
        var normalized = EnsureTrailingSlashForPrefix(fullPath);
        if (!normalized.StartsWith(_fullBasePathPrefix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var key = normalized[_fullBasePathPrefix.Length..].TrimStart('/');
        return key;
    }

    private static DateTime ParseHttpDate(string? value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime.ToLocalTime();

        return DateTime.MinValue;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 207)
            return;

        throw new HttpRequestException($"{operation}失败: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Replace('\\', '/').Trim('/');
    }

    private static string CombinePath(string left, string right)
    {
        left = NormalizeRelativePath(left);
        right = NormalizeRelativePath(right);
        if (string.IsNullOrEmpty(left)) return right;
        if (string.IsNullOrEmpty(right)) return left;
        return $"{left}/{right}";
    }

    private static string CombineAbsolutePath(string left, string right)
    {
        left = "/" + NormalizeRelativePath(left);
        right = NormalizeRelativePath(right);
        return string.IsNullOrEmpty(right) ? left : $"{left.TrimEnd('/')}/{right}";
    }

    private static string EnsureTrailingSlash(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return path.EndsWith('/') ? path : path + "/";
    }

    private static string EnsureLeadingAndTrailingSlash(string path)
    {
        var normalized = "/" + NormalizeRelativePath(path);
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private static string EnsureTrailingSlashForPrefix(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool PathsEqual(string left, string right)
    {
        return NormalizeRelativePath(left).Equals(NormalizeRelativePath(right), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient)
            _client.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record WebDavEntry(string Key, string FullPath, bool IsCollection, long Size, DateTime LastModified);
}
