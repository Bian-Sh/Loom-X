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

public enum UpdatePreparationPhase
{
    Downloading,
    Verifying
}

public sealed record UpdateDownloadProgress(
    long Transferred,
    long Total,
    int Percent,
    long BytesPerSecond,
    UpdatePreparationPhase Phase = UpdatePreparationPhase.Downloading);

public sealed record PreparedUpdate(
    string Version,
    string InstallerPath,
    DateTimeOffset VerifiedAt,
    string Sha256,
    long InstallerSize);

public sealed class InvalidPreparedUpdateException(string message) : InvalidOperationException(message);

public sealed record UpdateReleasePage(
    IReadOnlyList<UpdateRelease> Items,
    int Page,
    int PageSize,
    bool HasMore);

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(
        UpdateProxySettings settings,
        CancellationToken cancellationToken = default);

    Task<UpdateReleasePage> GetStableReleasesAsync(
        UpdateProxySettings settings,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PreparedUpdate> PrepareUpdateAsync(
        UpdateRelease release,
        UpdateProxySettings settings,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    void LaunchInstaller(PreparedUpdate preparedUpdate);
}

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

public sealed class UpdateService : IUpdateService
{
    private const string Repository = "Bian-Sh/Loom-X";
    private const string ApiUrl = "https://api.github.com/repos/Bian-Sh/Loom-X/releases";
    private const int GitHubPageSize = 100;
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
            var page = await GetStableReleasesAsync(proxySettings, 1, GitHubPageSize, cancellationToken);
            var current = AppVersion.TryParse(currentVersion, out var currentParsed) ? currentParsed : default;
            var latest = page.Items
                .Where(item => item.InstallerAsset is not null && item.ChecksumAsset is not null)
                .Where(item => AppVersion.TryParse(item.Version, out var parsed) && parsed.CompareTo(current) > 0)
                .OrderByDescending(item =>
                {
                    AppVersion.TryParse(item.Version, out var parsed);
                    return parsed;
                })
                .FirstOrDefault();

            logger.LogInformation(
                "更新检查完成 {CurrentVersion} {LatestVersion} {ItemCount} {ElapsedMs}ms",
                currentVersion,
                latest?.Version ?? "无",
                page.Items.Count,
                (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return new UpdateCheckResult(currentVersion, latest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "check");
            logger.LogWarning(
                diagnostic,
                "更新检查失败 {CurrentVersion} {ElapsedMs}ms {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                currentVersion,
                (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
            throw;
        }
    }

    public async Task<UpdateReleasePage> GetStableReleasesAsync(
        UpdateProxySettings settings,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var skip = (page - 1) * pageSize;
        var required = skip + pageSize + 1;
        var stable = new List<UpdateRelease>(required);
        var rawPage = 1;
        var hasNextRawPage = true;
        using var client = clientFactory(settings);

        while (stable.Count < required && hasNextRawPage)
        {
            var url = $"{ApiUrl}?per_page={GitHubPageSize}&page={rawPage}";
            using var response = await SendGetAsync(client, url, cancellationToken);
            var releases = await DeserializeReleasesAsync(response, cancellationToken);
            stable.AddRange(releases
                .Where(item => !item.Draft && !item.Prerelease)
                .Select(MapRelease)
                .OfType<UpdateRelease>());
            hasNextRawPage = HasNextPage(response);
            rawPage++;
        }

        var items = stable.Skip(skip).Take(pageSize).ToArray();
        var hasMore = stable.Count > skip + items.Length || hasNextRawPage;
        logger.LogInformation(
            "正式版本历史拉取完成 {Page} {PageSize} {ItemCount} {HasMore}",
            page, pageSize, items.Length, hasMore);
        return new UpdateReleasePage(items, page, pageSize, hasMore);
    }

    public async Task<PreparedUpdate> PrepareUpdateAsync(
        UpdateRelease release,
        UpdateProxySettings settings,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (release.InstallerAsset is null || release.ChecksumAsset is null)
            throw new InvalidOperationException("该版本缺少兼容的 LoomX 安装器或校验文件。");

        var versionDirectory = Path.Combine(tempRoot, release.Version);
        Directory.CreateDirectory(versionDirectory);
        var installerPath = Path.Combine(versionDirectory, Path.GetFileName(release.InstallerAsset.Name));
        var checksumPath = Path.Combine(versionDirectory, Path.GetFileName(release.ChecksumAsset.Name));
        var installerPartial = installerPath + ".partial";
        var checksumPartial = checksumPath + ".partial";

        if (File.Exists(installerPath) && File.Exists(checksumPath))
        {
            try
            {
                progress?.Report(new UpdateDownloadProgress(
                    new FileInfo(installerPath).Length,
                    new FileInfo(installerPath).Length,
                    100,
                    0,
                    UpdatePreparationPhase.Verifying));
                var sha256 = await VerifyChecksumAsync(installerPath, checksumPath, cancellationToken);
                var installerSize = new FileInfo(installerPath).Length;
                logger.LogInformation("更新包缓存校验完成 {Version} {Bytes}", release.Version, installerSize);
                return new PreparedUpdate(release.Version, installerPath, DateTimeOffset.UtcNow, sha256, installerSize);
            }
            catch (InvalidOperationException)
            {
                TryDelete(installerPath);
                TryDelete(checksumPath);
            }
        }

        TryDelete(installerPartial);
        TryDelete(checksumPartial);
        try
        {
            using var client = clientFactory(settings);
            await DownloadFileAsync(client, release.InstallerAsset.Url, installerPartial, progress, cancellationToken);
            await DownloadFileAsync(client, release.ChecksumAsset.Url, checksumPartial, null, cancellationToken);
            progress?.Report(new UpdateDownloadProgress(
                new FileInfo(installerPartial).Length,
                new FileInfo(installerPartial).Length,
                100,
                0,
                UpdatePreparationPhase.Verifying));
            var sha256 = await VerifyChecksumAsync(installerPartial, checksumPartial, cancellationToken);
            File.Move(installerPartial, installerPath, true);
            File.Move(checksumPartial, checksumPath, true);
            var installerSize = new FileInfo(installerPath).Length;
            logger.LogInformation("更新包校验完成 {Version} {Bytes}", release.Version, installerSize);
            return new PreparedUpdate(release.Version, installerPath, DateTimeOffset.UtcNow, sha256, installerSize);
        }
        catch
        {
            TryDelete(installerPath);
            TryDelete(checksumPath);
            TryDelete(installerPartial);
            TryDelete(checksumPartial);
            throw;
        }
    }

    public void LaunchInstaller(PreparedUpdate preparedUpdate)
    {
        ArgumentNullException.ThrowIfNull(preparedUpdate);
        try
        {
            if (string.IsNullOrWhiteSpace(preparedUpdate.Version)
                || string.IsNullOrWhiteSpace(preparedUpdate.InstallerPath)
                || preparedUpdate.InstallerSize < 0
                || !TryParseSha256(preparedUpdate.Sha256, out var expectedSha256)
                || !File.Exists(preparedUpdate.InstallerPath))
                throw new InvalidPreparedUpdateException("已验证更新包不可用，请重新下载。");

            using var stream = new FileStream(
                preparedUpdate.InstallerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.SequentialScan);
            if (stream.Length != preparedUpdate.InstallerSize)
                throw new InvalidPreparedUpdateException("已验证更新包已发生变化，请重新下载。");

            var actualSha256 = SHA256.HashData(stream);
            if (!CryptographicOperations.FixedTimeEquals(actualSha256, expectedSha256))
                throw new InvalidPreparedUpdateException("已验证更新包已发生变化，请重新下载。");
        }
        catch (InvalidPreparedUpdateException)
        {
            InvalidatePreparedUpdate(preparedUpdate);
            logger.LogWarning("更新安装器启动前身份复验失败 {Version}", preparedUpdate.Version);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            InvalidatePreparedUpdate(preparedUpdate);
            logger.LogWarning("更新安装器启动前无法读取 {Version} {ExceptionType}", preparedUpdate.Version, exception.GetType().Name);
            throw new InvalidPreparedUpdateException("已验证更新包不可用，请重新下载。");
        }

        installerLauncher.Launch(preparedUpdate.InstallerPath);
        logger.LogInformation("更新安装器已启动 {Version}", preparedUpdate.Version);
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

    private static async Task<string> VerifyChecksumAsync(string installerPath, string checksumPath, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(checksumPath, cancellationToken);
        var match = ChecksumRegex.Match(text);
        if (!match.Success) throw new InvalidOperationException("校验文件格式无效。");
        await using var stream = File.OpenRead(installerPath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, match.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("更新包 SHA-256 校验失败。");
        return actual;
    }

    private static bool TryParseSha256(string value, out byte[] sha256)
    {
        sha256 = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
        try
        {
            sha256 = Convert.FromHexString(value);
            return sha256.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void InvalidatePreparedUpdate(PreparedUpdate preparedUpdate)
    {
        TryDelete(preparedUpdate.InstallerPath);
        TryDelete(preparedUpdate.InstallerPath + ".sha256");
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

    private static bool HasNextPage(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Link", out var values)
        && values.SelectMany(value => value.Split(','))
            .Any(part => part.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase));

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
