using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using LoomX.Services;
using Xunit;

namespace LoomX.Tests;

public sealed class UpdateServiceTests
{
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

        var result = await service.CheckAsync(new UpdateProxySettings(false, "direct", "", 0, null, null));

        Assert.NotNull(result.Latest);
        Assert.Equal("0.12.7", result.Latest!.Version);
        Assert.NotNull(result.Latest.InstallerAsset);
        Assert.NotNull(result.Latest.ChecksumAsset);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_ShouldVerifyChecksumBeforeLaunchingInstaller()
    {
        var bytes = Encoding.UTF8.GetBytes("测试安装包");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true)
                return TextResponse($"{hash}  LoomX-0.12.7-setup.exe");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        var launcher = new RecordingLauncher();
        var root = Path.Combine(Path.GetTempPath(), "LoomX-UpdateTests", Guid.NewGuid().ToString("N"));
        var release = new UpdateRelease(
            "v0.12.7", "0.12.7", "稳定版", "修复", "https://github.com/Bian-Sh/Loom-X/releases/tag/v0.12.7", null,
            [
                new UpdateAsset("LoomX-0.12.7-setup.exe", "https://github.com/Bian-Sh/Loom-X/releases/download/v0.12.7/LoomX-0.12.7-setup.exe", bytes.Length, null),
                new UpdateAsset("LoomX-0.12.7-setup.exe.sha256", "https://github.com/Bian-Sh/Loom-X/releases/download/v0.12.7/LoomX-0.12.7-setup.exe.sha256", hash.Length, "text/plain")
            ],
            null,
            null);
        release = release with { InstallerAsset = release.Assets[0], ChecksumAsset = release.Assets[1] };

        try
        {
            var service = new UpdateService(_ => new HttpClient(handler), launcher, tempRoot: root, currentVersion: "0.12.6");
            var result = await service.DownloadAndInstallAsync(release, new UpdateProxySettings(false, "direct", "", 0, null, null));
            Assert.Equal("0.12.7", result.Version);
            Assert.Equal(result.InstallerPath, launcher.Path);
            Assert.True(File.Exists(result.InstallerPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static HttpResponseMessage TextResponse(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    private sealed class RecordingLauncher : IUpdateInstallerLauncher
    {
        public string? Path { get; private set; }
        public void Launch(string installerPath) => Path = installerPath;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }
}
