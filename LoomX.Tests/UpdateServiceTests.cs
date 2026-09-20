using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests;

public sealed class UpdateServiceTests
{
    private static readonly UpdateProxySettings DirectSettings = new(false, "direct", "", 0, null, null);

    [Fact]
    public async Task CheckAsync_ShouldIgnoreDraftPrereleaseAndSelectHighestStableRelease()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [
                  {"tag_name":"v0.12.7","name":"稳定版","body":"修复","html_url":"https://github.com/Bian-Sh/Loom-X/releases/tag/v0.12.7","draft":false,"prerelease":false,"assets":[{"name":"LoomX-0.12.7-setup.exe","browser_download_url":"https://github.com/Bian-Sh/Loom-X/releases/download/v0.12.7/LoomX-0.12.7-setup.exe","size":12,"content_type":"application/octet-stream"},{"name":"LoomX-0.12.7-setup.exe.sha256","browser_download_url":"https://github.com/Bian-Sh/Loom-X/releases/download/v0.12.7/LoomX-0.12.7-setup.exe.sha256","size":70,"content_type":"text/plain"}]},
                  {"tag_name":"v0.12.8-rc.1","draft":false,"prerelease":true,"assets":[]},
                  {"tag_name":"v0.13.0","draft":true,"prerelease":false,"assets":[]},
                  {"tag_name":"not-a-version","draft":false,"prerelease":false,"assets":[]}
                ]
                """)
        });
        var service = new UpdateService(_ => new HttpClient(handler), currentVersion: "0.12.6");

        var result = await service.CheckAsync(DirectSettings);

        Assert.NotNull(result.Latest);
        Assert.Equal("0.12.7", result.Latest!.Version);
        Assert.NotNull(result.Latest.InstallerAsset);
        Assert.NotNull(result.Latest.ChecksumAsset);
    }

    [Fact]
    public async Task GetStableReleasesAsync_跨原始页凑满正式版本且过滤无效项()
    {
        var handler = new StubHandler(request => BuildPagedReleaseResponse(request.RequestUri!));
        var service = new UpdateService(_ => new HttpClient(handler), currentVersion: "0.12.6");

        var page = await service.GetStableReleasesAsync(DirectSettings, page: 1, pageSize: 10);

        Assert.Equal(10, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.All(page.Items, item => Assert.True(AppVersion.TryParse(item.Version, out _)));
        Assert.Contains(page.Items, item => item.Version == "0.12.9" && item.InstallerAsset is null);
        Assert.Equal(2, handler.RequestUris.Count);
    }

    [Fact]
    public async Task CheckAsync_跳过缺少兼容资产的更高版本()
    {
        var handler = new StubHandler(_ => JsonResponse(BuildReleasesJson(
            StableRelease("v0.13.0", assets: []),
            StableRelease("v0.12.9", assets: CompatibleAssets("0.12.9")))));
        var service = new UpdateService(_ => new HttpClient(handler), currentVersion: "0.12.6");

        var result = await service.CheckAsync(DirectSettings);

        Assert.Equal("0.12.9", result.Latest?.Version);
    }

    [Fact]
    public async Task PrepareUpdateAsync_校验成功但不会启动安装器()
    {
        var fixture = UpdateFixture.CreateValid();
        try
        {
            var prepared = await fixture.Service.PrepareUpdateAsync(fixture.Release, DirectSettings);

            Assert.Equal("0.12.7", prepared.Version);
            Assert.True(File.Exists(prepared.InstallerPath));
            Assert.Null(fixture.Launcher.Path);

            fixture.Service.LaunchInstaller(prepared);
            Assert.Equal(prepared.InstallerPath, fixture.Launcher.Path);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public async Task PrepareUpdateAsync_有效缓存重新校验后不重复下载()
    {
        var fixture = UpdateFixture.CreateValid();
        try
        {
            var first = await fixture.Service.PrepareUpdateAsync(fixture.Release, DirectSettings);
            var requestCount = fixture.Handler.RequestUris.Count;
            var second = await fixture.Service.PrepareUpdateAsync(fixture.Release, DirectSettings);

            Assert.Equal(first.InstallerPath, second.InstallerPath);
            Assert.Equal(requestCount, fixture.Handler.RequestUris.Count);
            Assert.Null(fixture.Launcher.Path);
        }
        finally { fixture.Dispose(); }
    }

    [Fact]
    public async Task PrepareUpdateAsync_校验失败删除当前版本临时文件()
    {
        var fixture = UpdateFixture.CreateWithChecksum("0000000000000000000000000000000000000000000000000000000000000000");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Service.PrepareUpdateAsync(fixture.Release, DirectSettings));

            Assert.Empty(Directory.Exists(fixture.VersionDirectory)
                ? Directory.EnumerateFiles(fixture.VersionDirectory, "*.partial")
                : []);
            Assert.Null(fixture.Launcher.Path);
        }
        finally { fixture.Dispose(); }
    }

    private static HttpResponseMessage TextResponse(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage BuildPagedReleaseResponse(Uri requestUri)
    {
        var page = requestUri.Query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.TrimStart('?').Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == "page")
            .Select(parts => int.Parse(parts[1]))
            .Single();

        if (page == 1)
        {
            var response = JsonResponse(BuildReleasesJson(
                StableRelease("v0.13.0"),
                StableRelease("v0.12.9"),
                StableRelease("v0.12.8", CompatibleAssets("0.12.8")),
                StableRelease("v0.12.7"),
                StableRelease("v0.12.6"),
                StableRelease("v0.12.5"),
                StableRelease("v0.12.4"),
                StableRelease("v0.12.3"),
                StableRelease("v9.9.9", draft: true),
                StableRelease("v8.8.8", prerelease: true),
                StableRelease("not-a-version")));
            response.Headers.TryAddWithoutValidation(
                "Link",
                "<https://api.github.com/repos/Bian-Sh/Loom-X/releases?per_page=100&page=2>; rel=\"next\"");
            return response;
        }

        return JsonResponse(BuildReleasesJson(
            StableRelease("v0.12.2"),
            StableRelease("v0.12.1"),
            StableRelease("v0.11.9"),
            StableRelease("v0.11.8")));
    }

    private static string BuildReleasesJson(params object[] releases) => JsonSerializer.Serialize(releases);

    private static object StableRelease(
        string tag,
        object[]? assets = null,
        bool draft = false,
        bool prerelease = false) => new
        {
            tag_name = tag,
            name = tag,
            body = "版本说明",
            html_url = $"https://github.com/Bian-Sh/Loom-X/releases/tag/{tag}",
            draft,
            prerelease,
            assets = assets ?? []
        };

    private static object[] CompatibleAssets(string version) =>
    [
        new
        {
            name = $"LoomX-{version}-setup.exe",
            browser_download_url = $"https://github.com/Bian-Sh/Loom-X/releases/download/v{version}/LoomX-{version}-setup.exe",
            size = 12,
            content_type = "application/octet-stream"
        },
        new
        {
            name = $"LoomX-{version}-setup.exe.sha256",
            browser_download_url = $"https://github.com/Bian-Sh/Loom-X/releases/download/v{version}/LoomX-{version}-setup.exe.sha256",
            size = 70,
            content_type = "text/plain"
        }
    ];

    private sealed class RecordingLauncher : IUpdateInstallerLauncher
    {
        public string? Path { get; private set; }
        public void Launch(string installerPath) => Path = installerPath;
    }

    private sealed class UpdateFixture : IDisposable
    {
        private readonly string root;

        private UpdateFixture(string checksum)
        {
            var bytes = Encoding.UTF8.GetBytes("测试安装包");
            Handler = new StubHandler(request =>
            {
                if (request.RequestUri?.AbsoluteUri.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true)
                    return TextResponse($"{checksum}  LoomX-0.12.7-setup.exe");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            });
            Launcher = new RecordingLauncher();
            root = Path.Combine(Path.GetTempPath(), "LoomX-UpdateTests", Guid.NewGuid().ToString("N"));
            var assets = new[]
            {
                new UpdateAsset("LoomX-0.12.7-setup.exe", "https://github.com/Bian-Sh/Loom-X/releases/download/v0.12.7/LoomX-0.12.7-setup.exe", bytes.Length, null),
                new UpdateAsset("LoomX-0.12.7-setup.exe.sha256", "https://github.com/Bian-Sh/Loom-X/releases/download/v0.12.7/LoomX-0.12.7-setup.exe.sha256", checksum.Length, "text/plain")
            };
            Release = new UpdateRelease(
                "v0.12.7", "0.12.7", "稳定版", "修复", "https://github.com/Bian-Sh/Loom-X/releases/tag/v0.12.7", null,
                assets, assets[0], assets[1]);
            Service = new UpdateService(_ => new HttpClient(Handler), Launcher, tempRoot: root, currentVersion: "0.12.6");
        }

        public UpdateService Service { get; }
        public UpdateRelease Release { get; }
        public StubHandler Handler { get; }
        public RecordingLauncher Launcher { get; }
        public string VersionDirectory => Path.Combine(root, Release.Version);

        public static UpdateFixture CreateValid()
        {
            var bytes = Encoding.UTF8.GetBytes("测试安装包");
            return CreateWithChecksum(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        public static UpdateFixture CreateWithChecksum(string checksum) => new(checksum);

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(responder(request));
        }
    }
}
