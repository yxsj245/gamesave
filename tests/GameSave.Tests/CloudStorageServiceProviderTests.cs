using GameSave.Models;
using GameSave.Services;
using Xunit;

namespace GameSave.Tests;

public class CloudStorageServiceProviderTests
{
    [Fact]
    public async Task TestConnectionAsync_UsesWebDavProviderForWebDavConfig()
    {
        var config = new CloudConfig
        {
            ProviderType = CloudProviderType.WebDav,
            ServerUrl = "http://127.0.0.1:1",
            Username = "admin",
            Password = "secret",
            RemoteBasePath = "GameSave"
        };
        var service = new CloudStorageService(config, new ConfigService());

        var result = await service.TestConnectionAsync();

        Assert.DoesNotContain("暂不支持 WebDav", result.message);
    }
}
