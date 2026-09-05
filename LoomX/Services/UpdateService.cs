using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LoomX.Services;

public readonly record struct StableVersion(int Major, int Minor, int Patch) : IComparable<StableVersion>
{
    public int CompareTo(StableVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public static class AppVersion
{
    public const string DefaultVersion = "0.12.6";

    public static string Current => Normalize(
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString());

    public static string Label => $"v{Current}";

    public static string Normalize(string? raw)
    {
        var value = raw?.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        return TryParse(value, out _) ? value! : DefaultVersion;
    }

    public static bool TryParse(string? value, out StableVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = Regex.Match(value.Trim().TrimStart('v', 'V'), "^(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)$", RegexOptions.CultureInvariant);
        if (!match.Success
            || !int.TryParse(match.Groups["major"].Value, out var major)
            || !int.TryParse(match.Groups["minor"].Value, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, out var patch))
            return false;

        version = new StableVersion(major, minor, patch);
        return true;
    }
}

public sealed record UpdateProxySettings(
    bool UseProxy,
    string ProxyMode,
    string ProxyHost,
    int ProxyPort,
    string? ProxyUsername,
    string? ProxyPassword);

public sealed record UpdateAsset(string Name, string Url, long Size, string? ContentType);

public sealed record UpdateRelease(
    string TagName,
    string Version,
    string Name,
    string Body,
    string HtmlUrl,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<UpdateAsset> Assets,
    UpdateAsset? InstallerAsset,
    UpdateAsset? ChecksumAsset);

public sealed record UpdateCheckResult(string CurrentVersion, UpdateRelease? Latest)
{
    public bool IsAvailable => Latest is not null;
}

public sealed record UpdateDownloadProgress(long Transferred, long Total, int Percent, long BytesPerSecond);

public sealed record UpdateInstallResult(string InstallerPath, string Version);

public interface IUpdateInstallerLauncher
{
    void Launch(string installerPath);
}

public sealed class UpdateInstallerLauncher : IUpdateInstallerLauncher
{
    public void Launch(string installerPath)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory
        });
        if (process is null) throw new InvalidOperationException("无法启动更新安装器。");
    }
}

public static class UpdateHttpClientFactory
{
    public static HttpClient Create(UpdateProxySettings settings)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = settings.UseProxy && !string.Equals(settings.ProxyMode, "direct", StringComparison.OrdinalIgnoreCase)
        };

        if (handler.UseProxy && string.Equals(settings.ProxyMode, "custom", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(settings.ProxyHost?.Trim(), UriKind.Absolute, out var proxyUri)
                || proxyUri.Scheme is not ("http" or "https")
                || settings.ProxyPort is < 1 or > 65535)
                throw new InvalidOperationException("更新代理配置无效。");

            var proxy = new WebProxy($"{proxyUri.Scheme}://{proxyUri.Host}:{settings.ProxyPort}");
            if (!string.IsNullOrWhiteSpace(settings.ProxyUsername) || !string.IsNullOrWhiteSpace(settings.ProxyPassword))
                proxy.Credentials = new NetworkCredential(settings.ProxyUsername ?? string.Empty, settings.ProxyPassword ?? string.Empty);
            handler.Proxy = proxy;
        }

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LoomX", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

public sealed class UpdateService
{
    private const string Repository = "Bian-Sh/Loom-X";
    private const string ApiUrl = "https://api.github.com/repos/Bian-Sh/Loom-X/releases?per_page=30";
    private const int MaxRedirects = 8;
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
        "release-assets.githubusercontent.com"
    };
    private static readonly Regex ChecksumRegex = new(@"\b[0-9a-fA-F]{64}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly Func<UpdateProxySettings, HttpClient> clientFactory;
    private readonly IUpdateInstallerLauncher installerLauncher;
    private readonly ILogger<UpdateService> logger;
    private readonly string tempRoot;
    private readonly string currentVersion;

    public UpdateService(
        Func<UpdateProxySettings, HttpClient>? clientFactory = null,
        IUpdateInstallerLauncher? installerLauncher = null,
        ILogger<UpdateService>? logger = null,
        string? tempRoot = null,
        string? currentVersion = null)
    {
        this.clientFactory = clientFactory ?? UpdateHttpClientFactory.Create;
        this.installerLauncher = installerLauncher ?? new UpdateInstallerLauncher();
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateService>.Instance;
        this.tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "LoomX", "updates");
        this.currentVersion = AppVersion.Normalize(currentVersion ?? AppVersion.Current);
    }

    public string CurrentVersion => currentVersion;

    public async Task<UpdateCheckResult> CheckAsync(UpdateProxySettings proxySettings, CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            using var client = clientFactory(proxySettings);
            using var response = await SendGetAsync(client, ApiUrl, cancellationToken);
            var releases = await DeserializeReleasesAsync(response, cancellationToken);
            var current = AppVersion.TryParse(currentVersion, out var currentParsed) ? currentParsed : default;
            var latest = releases
                .Where(item => !item.Draft && !item.Prerelease)
                .Select(MapRelease)
                .Where(item => item is not null)
                .Select(item => item!)
                .Where(item => AppVersion.TryParse(item.Version, out var parsed) && parsed.CompareTo(current) > 0)
                .OrderByDescending(item => new StableVersion(
                    int.Parse(item.Version.Split('.')[0]),
                    int.Parse(item.Version.Split('.')[1]),
                    int.Parse(item.Version.Split('.')[2])))
                .FirstOrDefault();

            logger.LogInformation("更新检查完成 {CurrentVersion} {LatestVersion} {ElapsedMs}ms", currentVersion, latest?.Version ?? "无", (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return new UpdateCheckResult(currentVersion, latest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "更新检查失败 {CurrentVersion} {ElapsedMs}ms", currentVersion, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            throw;
        }
    }

    public async Task<UpdateInstallResult> DownloadAndInstallAsync(
        UpdateRelease release,
        UpdateProxySettings proxySettings,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (release.InstallerAsset is null || release.ChecksumAsset is null)
            throw new InvalidOperationException("该版本缺少兼容的 LoomX 安装器或校验文件。");

        Directory.CreateDirectory(tempRoot);
        var versionDirectory = Path.Combine(tempRoot, release.Version);
        Directory.CreateDirectory(versionDirectory);
        var installerPath = Path.Combine(versionDirectory, Path.GetFileName(release.InstallerAsset.Name));
        var checksumPath = Path.Combine(versionDirectory, Path.GetFileName(release.ChecksumAsset.Name));

        try
        {
            using var client = clientFactory(proxySettings);
            await DownloadFileAsync(client, release.InstallerAsset.Url, installerPath, progress, cancellationToken);
            await DownloadFileAsync(client, release.ChecksumAsset.Url, checksumPath, null, cancellationToken);
            await VerifyChecksumAsync(installerPath, checksumPath, cancellationToken);
            logger.LogInformation("更新包校验完成 {Version} {Bytes}", release.Version, new FileInfo(installerPath).Length);
            installerLauncher.Launch(installerPath);
            logger.LogInformation("更新安装器已启动 {Version}", release.Version);
            return new UpdateInstallResult(installerPath, release.Version);
        }
        catch
        {
            TryDelete(installerPath);
            TryDelete(checksumPath);
            throw;
        }
    }

    private async Task DownloadFileAsync(HttpClient client, string url, string path, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(client, url, cancellationToken);
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan);
        var buffer = new byte[81920];
        long transferred = 0;
        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            transferred += read;
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
            var speed = elapsed <= 0 ? 0 : (long)(transferred / elapsed);
            var percent = total <= 0 ? 0 : (int)Math.Clamp(transferred * 100 / total, 0, 100);
            progress?.Report(new UpdateDownloadProgress(transferred, total, percent, speed));
        }
        progress?.Report(new UpdateDownloadProgress(transferred, total, 100, (long)(transferred / Math.Max(Stopwatch.GetElapsedTime(startedAt).TotalSeconds, 0.001))));
    }

    private static async Task VerifyChecksumAsync(string installerPath, string checksumPath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(checksumPath, cancellationToken);
        var match = ChecksumRegex.Match(text);
        if (!match.Success) throw new InvalidOperationException("校验文件格式无效。");
        await using var stream = File.OpenRead(installerPath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, match.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新包 SHA-256 校验失败。");
    }

    private static async Task<HttpResponseMessage> SendGetAsync(HttpClient client, string rawUrl, CancellationToken cancellationToken)
    {
        var currentUrl = rawUrl;
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            EnsureAllowedUrl(currentUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest)
            {
                var location = response.Headers.Location?.ToString();
                response.Dispose();
                if (string.IsNullOrWhiteSpace(location)) throw new InvalidOperationException("更新服务器重定向地址为空。");
                currentUrl = new Uri(new Uri(currentUrl), location).ToString();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"更新服务器返回 HTTP {status}。");
            }

            return response;
        }

        throw new InvalidOperationException("更新服务器重定向次数过多。");
    }

    private static void EnsureAllowedUrl(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AllowedHosts.Contains(uri.Host))
            throw new InvalidOperationException("更新下载地址不受信任。");
    }

    private static async Task<IReadOnlyList<GitHubRelease>> DeserializeReleasesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return releases ?? throw new InvalidOperationException("GitHub Release 响应格式无效。");
    }

    private static UpdateRelease? MapRelease(GitHubRelease release)
    {
        if (!AppVersion.TryParse(release.TagName, out _)) return null;
        var version = AppVersion.Normalize(release.TagName);
        var assets = (release.Assets ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && Uri.TryCreate(item.BrowserDownloadUrl, UriKind.Absolute, out _))
            .Select(item => new UpdateAsset(item.Name.Trim(), item.BrowserDownloadUrl, item.Size, item.ContentType))
            .ToArray();
        var installer = assets.FirstOrDefault(item => string.Equals(item.Name, $"LoomX-{version}-setup.exe", StringComparison.OrdinalIgnoreCase));
        var checksum = installer is null ? null : assets.FirstOrDefault(item => string.Equals(item.Name, installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        return new UpdateRelease(release.TagName, version, release.Name ?? release.TagName, release.Body ?? string.Empty, release.HtmlUrl ?? $"https://github.com/{Repository}/releases", release.PublishedAt, assets, installer, checksum);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        string? Name,
        string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        bool Draft,
        bool Prerelease,
        List<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        long Size,
        [property: JsonPropertyName("content_type")] string? ContentType);
}
