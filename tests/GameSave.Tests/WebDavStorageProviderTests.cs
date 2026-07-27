using System.Net;
using System.Text;
using GameSave.Models;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

public class WebDavStorageProviderTests
{
    [Fact]
    public async Task UploadFileAsync_CreatesParentCollectionsBeforePuttingFile()
    {
        using var tempFile = new TempFile("save-data");
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var provider = new WebDavStorageProvider(CreateConfig(), client);

        await provider.UploadFileAsync(tempFile.Path, "game-1/123_退出存档.tar");

        string[] expected =
        [
            "MKCOL http://nas.example.test/webdav/GameSave/",
            "MKCOL http://nas.example.test/webdav/GameSave/game-1/",
            "PUT http://nas.example.test/webdav/GameSave/game-1/123_退出存档.tar"
        ];
        Assert.True(expected.SequenceEqual(handler.Requests),
            $"Expected:\n{string.Join('\n', expected)}\nActual:\n{string.Join('\n', handler.Requests)}");
    }

    [Fact]
    public async Task ListObjectsAsync_ParsesPropfindFilesAndStripsRemoteBasePath()
    {
        var handler = new RecordingHandler
        {
            PropfindResponse = """
                <?xml version="1.0" encoding="utf-8"?>
                <d:multistatus xmlns:d="DAV:">
                  <d:response>
                    <d:href>/webdav/GameSave/game-1/</d:href>
                    <d:propstat><d:prop><d:resourcetype><d:collection /></d:resourcetype></d:prop></d:propstat>
                  </d:response>
                  <d:response>
                    <d:href>/webdav/GameSave/game-1/123_%E9%80%80%E5%87%BA%E5%AD%98%E6%A1%A3.tar</d:href>
                    <d:propstat>
                      <d:prop>
                        <d:getcontentlength>42</d:getcontentlength>
                        <d:getlastmodified>Mon, 20 Jul 2026 08:30:00 GMT</d:getlastmodified>
                      </d:prop>
                    </d:propstat>
                  </d:response>
                </d:multistatus>
                """
        };
        using var client = new HttpClient(handler);
        using var provider = new WebDavStorageProvider(CreateConfig(), client);

        var objects = await provider.ListObjectsAsync("game-1/");

        var obj = Assert.Single(objects);
        Assert.Equal("game-1/123_退出存档.tar", obj.Key);
        Assert.Equal(42, obj.Size);
        Assert.Equal(new DateTime(2026, 7, 20, 8, 30, 0, DateTimeKind.Utc).ToLocalTime(), obj.LastModified);
    }

    [Fact]
    public async Task ListObjectsAsync_UsesTrailingSlashForRootCollection()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        using var provider = new WebDavStorageProvider(CreateConfig(), client);

        await provider.ListObjectsAsync();

        Assert.Contains("PROPFIND http://nas.example.test/webdav/GameSave/", handler.Requests);
    }

    [Fact]
    public async Task DownloadContentAsync_ReturnsNullWhenWebDavObjectDoesNotExist()
    {
        var handler = new RecordingHandler { MissingGet = true };
        using var client = new HttpClient(handler);
        using var provider = new WebDavStorageProvider(CreateConfig(), client);

        var content = await provider.DownloadContentAsync("game-1/game.json");

        Assert.Null(content);
    }

    [Fact]
    public async Task DeleteObjectsByPrefixAsync_DeletesEmptyCollectionAfterObjects()
    {
        var handler = new RecordingHandler
        {
            PropfindResponse = """
                <?xml version="1.0" encoding="utf-8"?>
                <d:multistatus xmlns:d="DAV:">
                  <d:response>
                    <d:href>/webdav/GameSave/game-1/</d:href>
                    <d:propstat><d:prop><d:resourcetype><d:collection /></d:resourcetype></d:prop></d:propstat>
                  </d:response>
                  <d:response>
                    <d:href>/webdav/GameSave/game-1/save.tar</d:href>
                    <d:propstat><d:prop><d:getcontentlength>42</d:getcontentlength></d:prop></d:propstat>
                  </d:response>
                </d:multistatus>
                """
        };
        using var client = new HttpClient(handler);
        using var provider = new WebDavStorageProvider(CreateConfig(), client);

        var deleted = await provider.DeleteObjectsByPrefixAsync("game-1/");

        Assert.Equal(1, deleted);
        Assert.Contains("PROPFIND http://nas.example.test/webdav/GameSave/game-1/", handler.Requests);
        Assert.Contains("DELETE http://nas.example.test/webdav/GameSave/game-1/save.tar", handler.Requests);
        Assert.Contains("DELETE http://nas.example.test/webdav/GameSave/game-1/", handler.Requests);
    }

    private static CloudConfig CreateConfig() => new()
    {
        ProviderType = CloudProviderType.WebDav,
        ServerUrl = "http://nas.example.test/webdav",
        Username = "admin",
        Password = "secret",
        RemoteBasePath = "GameSave"
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        public string PropfindResponse { get; set; } = "<d:multistatus xmlns:d=\"DAV:\" />";
        public bool MissingGet { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method.Method} {request.RequestUri}");

            if (request.Method.Method == "PROPFIND")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MultiStatus)
                {
                    Content = new StringContent(PropfindResponse, Encoding.UTF8, "application/xml")
                });
            }

            if (request.Method == HttpMethod.Get && MissingGet)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(
                request.Method.Method == "MKCOL" ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            });
        }
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName();

        public TempFile(string content)
        {
            File.WriteAllText(Path, content);
        }

        public void Dispose()
        {
            try { File.Delete(Path); } catch { }
        }
    }
}
