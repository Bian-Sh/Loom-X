---
change: enhance-update-experience
design-doc: docs/superpowers/specs/2026-09-20-enhance-update-experience-design.md
base-ref: e0e1dde3ebd11c130a77935313372a332016a1fb
archived-with: 2026-09-21-enhance-update-experience
---

<!-- comet-task-authority: openspec/changes/enhance-update-experience/tasks.md -->
<!-- comet-task-ref:e4a05700-8775-4fe2-bc3f-7b3ea93eb46c -->
<!-- comet-task-ref:c741dd3b-fca4-412b-bb51-5d113854351e -->
<!-- comet-task-ref:5883d1d3-ba19-4b7a-ba63-33e8e4f8de37 -->
<!-- comet-task-ref:8ebc1515-7e79-4a75-b3a8-163c3270a3ee -->
<!-- comet-task-ref:018e4db9-6083-42b4-bf53-08c1007d610d -->
<!-- comet-task-ref:c0a422cc-d932-4bad-93c2-6660c065f82d -->
<!-- comet-task-ref:dca76ff5-20ee-4d96-8901-1f4843133064 -->
<!-- comet-task-ref:cbccc188-e9d0-45bd-8a34-0902a436c24c -->
<!-- comet-task-ref:4940c2eb-26c8-4e1d-b215-d4490945123e -->
<!-- comet-task-ref:d2a7b929-f10f-4494-9635-7b942de2f520 -->
<!-- comet-task-ref:30347da4-671a-435e-a115-f303a58cc8fc -->
<!-- comet-task-ref:e4b31920-ee71-46e6-9979-b2edd57f4051 -->
<!-- comet-task-ref:f2f017d9-815c-4e52-96d0-40dabe1afdc9 -->
<!-- comet-task-ref:adf6294f-5f85-4097-b36f-9e63ff9d419e -->
<!-- comet-task-ref:80883e85-89e2-4fd4-9cb9-84cc2d443fe3 -->
<!-- comet-task-ref:4630a1ee-9224-4db6-a4b1-c6b2a0c31891 -->
<!-- comet-task-ref:68d86b48-6984-4993-9e0a-af0d5f93e1a4 -->
<!-- comet-task-ref:769ab435-65ec-4fce-a0ab-1fed06603a24 -->
<!-- comet-task-ref:18416923-e484-4367-9334-e6c4aa504488 -->
<!-- comet-task-ref:306b389c-d609-49e5-85d2-8aa1c9a769cd -->
<!-- comet-task-ref:1e00806c-1003-4407-bb4b-28ecc4d00719 -->
<!-- comet-task-ref:aa115763-31df-42d5-af5a-87774b46be6a -->
<!-- comet-task-ref:a8424e08-e7a4-49e4-9471-267eb7d13514 -->
<!-- comet-task-ref:0e29205f-2c44-4631-ac35-9122cc63c40e -->
<!-- comet-task-ref:498183c6-ffa5-460b-91da-e7539296171f -->
<!-- comet-task-ref:632cb215-6a6b-4ea2-aef0-ac2613eecd13 -->
<!-- comet-task-ref:05e3eaa0-bb65-4b61-ae4a-2b16583311bb -->
<!-- comet-task-ref:4ecfd11c-09a6-434e-a09e-d74784c2cfad -->
<!-- comet-task-ref:bfe6c322-d777-4332-b68e-7cb5bc644c7f -->
<!-- comet-task-ref:fc1ab8c3-338c-454d-9e62-8d59b9aad60b -->
<!-- comet-task-ref:64fd542b-9cc6-4e80-b551-7cd3a4771992 -->

# Loom-X 更新体验实施计划

> **供代理执行者使用：** 必须逐项执行本计划；推荐使用 superpowers:subagent-driven-development，也可使用 superpowers:executing-plans。任务完成状态以 OpenSpec `tasks.md` 为唯一权威，本计划仅保留实施步骤与稳定 task ID 映射；任何测试、构建或运行异常都先加载 systematic-debugging，不得直接猜测修复。

**目标：** 将 Loom-X 更新流程改造成“自动检查并后台下载、标题栏持久入口、浮窗内直接阅读 Release Notes、用户确认后重启安装”，并在设置页提供最近 10 个正式版本的 Markdown 历史浏览、切换、刷新与分页。

**架构：** UpdateService 实现窄接口 IUpdateService，负责正式 Release 分页、更新包准备、SHA-256 校验和显式安装器启动；UpdateCoordinator 只维护当前可安装版本状态机，ReleaseHistoryViewModel 独立维护历史列表、缓存和选择。更新浮窗与设置页共用 ReleaseNotesContentViewModel、ReleaseNotesMarkdownPolicy 和 ReleaseNotesView，应用退出通过现有 Avalonia Desktop lifetime 正常路径完成。

**技术栈：** .NET 10、C#、Avalonia 11.3.20、LiveMarkdown.Avalonia 1.12.2、Markdig 0.43.0、xUnit、HttpClient、System.Text.Json、Microsoft.Extensions.Logging、OpenSpec/Comet、cua-driver。

**规格：** docs/superpowers/specs/2026-09-20-enhance-update-experience-design.md；行为契约同时受 openspec/changes/enhance-update-experience/specs/desktop-update-experience/spec.md 约束。

## Global Constraints（全局约束）

- 所有文档、代码注释和 Git 提交消息必须使用中文；技术名词、类型名、命令、路径和配置键保持原文。
- 实施基线固定为 e0e1dde3ebd11c130a77935313372a332016a1fb；开始执行前运行 git status --short --branch、git fetch origin 和 git rev-list --left-right --count HEAD...origin/master，不得 reset、clean、stash、强制推送或覆盖其他 Session 的未提交文件。
- 配置数据库只能使用 %LOCALAPPDATA%\LoomX\LoomX.db，活动库只能使用 %LOCALAPPDATA%\LoomX\LoomX.Activity.db；本变更不得修改数据库路径、schema、迁移源或创建第二份设置库。
- 更新检查、Release 历史和安装包下载必须复用 AppDataStore.GetUpdateProxySettingsAsync；不得新增绕过代理设置的 HttpClient 路径。
- 只处理正式 Release：Draft、Pre-release、非法 tag 不占正式分页名额；缺少安装资产的正式 Release 可浏览，但不得成为自动安装目标。
- 自动检查发现兼容更新后自动下载并校验；任何路径在用户点击“重启并安装”前都不得调用安装器。
- 下载文件继续位于系统临时目录 LoomX/updates；只清理当前版本的 .partial 或无效缓存，不得删除其他版本目录或其他 Session 产物。
- 日志统一使用 ILogger<T> 和结构化模板；禁止记录 Release Body、API Key、Authorization、自定义 Header 值、代理凭据、请求/响应正文、用户 prompt、图片或工具参数。
- 用户可见动态文案必须来自 Strings.resx；新增键同步写入 Strings.en-US.resx、Strings.zh-TW.resx，并为 Strings.ja-JP.resx 提供非空值以满足现有资源完整性测试。
- 不引入 WebView、第二套 Markdown 引擎、新数据库表或新的持久化队列；继续使用 LiveMarkdown.Avalonia 1.12.2 和其传递依赖 Markdig 0.43.0。
- 实现 Markdown 策略前必须通过 Context7 MCP 依次执行 resolve-library-id 和 query-docs，核对 Markdig AST 遍历、SourceSpan、LinkInline 以及 LiveMarkdown MarkdownRenderer/ObservableStringBuilder 的当前 API；Context7 不可用时才读取本机 NuGet 包 README/XML 文档，并在验证报告记录降级原因。
- ObservableStringBuilder 非线程安全；创建、替换和追加供 MarkdownRenderer 使用的内容必须在 UI 线程完成。
- Avalonia AXAML、资源和桌面资产修改与 Unity 无关，不执行 Unity Reimport、ReimportAll 或任何 Unity Editor 操作。
- 标题栏、浮窗和设置页只能使用现有 DynamicResource 主题 token；不得照搬参考图的黑金配色，不得用透明主题截图单独判定真实配色。
- 任何应用修改完成后必须先定向测试，再完整测试和 Release 构建，最后通过 cua-driver 只截取 Loom-X 窗口验证，并发布到 outputs/yyyyMMdd-HHmmss-enhance-update-experience；不得删除既有 outputs 目录。

## 文件结构与职责

**新增文件：**

- LoomX/Services/ReleaseNotesMarkdownPolicy.cs：使用 Markdig 解析定位不安全节点，移除 HTML、远程图片和非 HTTPS 链接，同时保留普通 Markdown 结构。
- LoomX/ViewModels/ReleaseNotesContentViewModel.cs：把单个 Release 投影为标题、日期、空态和新的 ObservableStringBuilder。
- LoomX/ViewModels/ReleaseHistoryViewModel.cs：维护正式版本分页、缓存、选择、刷新、空态和错误态。
- LoomX/Views/ReleaseNotesView.axaml：唯一可复用 Markdown 阅读组件。
- LoomX/Views/ReleaseNotesView.axaml.cs：纯 InitializeComponent 代码后置，不承载网络或安装逻辑。
- LoomX.Tests/ViewModels/UpdateCoordinatorTests.cs：状态机、并发、稍后、重试和一次性安装测试。
- LoomX.Tests/ViewModels/ReleaseHistoryViewModelTests.cs：首次加载、分页、缓存、选择与失败保留测试。
- LoomX.Tests/ViewModels/ReleaseNotesContentViewModelTests.cs：Markdown 安全和内容替换测试。
- LoomX.Tests/Views/ReleaseNotesViewContractTests.cs：共享 Markdown 视图结构契约。
- LoomX.Tests/Views/UpdateExperienceContractTests.cs：标题栏入口、更新浮窗、设置页与应用退出接线契约。
- docs/superpowers/reports/2026-09-20-enhance-update-experience-verify.md：最终测试、CUA、发布包和规格覆盖证据。

**修改文件：**

- LoomX/Services/UpdateService.cs：新增 IUpdateService、正式分页模型、准备/启动边界、缓存复验和阶段进度。
- LoomX/ViewModels/UpdateCoordinator.cs：删除 Available/旧卡片状态，改为自动准备、Ready、持久入口和安装确认状态机。
- LoomX/ViewModels/MainWindowViewModel.cs：创建并共享 IUpdateService，注入正常退出回调，向设置页共享历史状态。
- LoomX/ViewModels/SettingsViewModel.cs：新增 SelectedTabIndex 和 ReleaseHistory，首次进入更新 Tab 时加载。
- LoomX/App.axaml.cs：向 MainWindowViewModel 注入 desktop.Shutdown 正常退出回调。
- LoomX/MainWindow.axaml：标题栏入口、扁平更新浮窗、共享 ReleaseNotesView，删除旧右下角卡片和纯文本遮罩。
- LoomX/MainWindow.axaml.cs：浮窗出现时设置焦点，Escape 执行“稍后”，保持窗口拖动与按钮命中边界。
- LoomX/Views/SettingsView.axaml：保留四项更新设置并加入 Release Notes 分栏和完整状态。
- LoomX/Resources/Strings.resx、Strings.en-US.resx、Strings.zh-TW.resx、Strings.ja-JP.resx：更新入口、浮窗、历史、错误和辅助文案。
- LoomX.Tests/UpdateServiceTests.cs：Release 分页、安装资产、缓存、校验和显式启动测试。
- LoomX.Tests/Views/MainWindowChromeContractTests.cs、SettingsViewContractTests.cs：保留既有契约并补充更新 UX 断言。
- LoomX.Tests/LocalizationNoCjkTest.cs、LocalizationResourceParityTest.cs：把新增 ViewModel 纳入硬编码扫描并保持资源键一致。
- openspec/changes/enhance-update-experience/tasks.md：每个实现任务绿灯后勾选其覆盖项。

## OpenSpec 22 项任务覆盖映射

| OpenSpec 项 | 本计划任务 |
|---|---|
| 1.1、1.2 | Task 1 |
| 1.3、1.4 | Task 2 |
| 2.1、2.2 | Task 4 |
| 2.3 | Task 9 |
| 3.1 | Task 3 |
| 3.2 | Task 7 |
| 4.1 | Task 8 的红灯契约 |
| 4.2、4.3、4.4 | Task 8 |
| 5.1、5.2 | Task 6 |
| 5.3 | Task 9 |
| 5.4 | Task 9 |
| 6.1、6.2 | Task 10 |
| 6.3、6.4、6.5 | Task 10 |

---

### Task 1：正式 Release 分页与 IUpdateService 契约

**文件：**
- 修改：LoomX/Services/UpdateService.cs:13-351
- 修改：LoomX.Tests/UpdateServiceTests.cs:10-85
- 修改：openspec/changes/enhance-update-experience/tasks.md:3-6

**接口：**
- 消费：UpdateProxySettings、UpdateRelease、UpdateCheckResult、UpdateHttpClientFactory.Create。
- 产出：IUpdateService、UpdateReleasePage；CheckAsync 只返回高于当前版本且同时有安装器和校验文件的最高正式版本；GetStableReleasesAsync 返回可浏览的正式 Release，包括缺少安装资产者。

~~~csharp
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
~~~

**步骤 1：编写 Release 分页和可安装筛选失败测试**

在 UpdateServiceTests 中新增以下测试方法，并把 StubHandler 扩展为记录 RequestUri：

~~~csharp
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
~~~

测试辅助成员固定为：

~~~csharp
private static readonly UpdateProxySettings DirectSettings = new(false, "direct", "", 0, null, null);

private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<Uri> RequestUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri!);
        return Task.FromResult(responder(request));
    }
}
~~~

**步骤 2：运行定向测试并确认红灯**

运行：

~~~powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateServiceTests"
~~~

预期：FAIL，编译错误指出 IUpdateService、UpdateReleasePage 或 GetStableReleasesAsync 不存在；现有 CheckAsync 也会错误选择缺少安装资产的 v0.13.0。

**步骤 3：实现接口、正式分页和安全日志**

将固定 ApiUrl 改为基础地址，并用 Link 响应头判断原始 GitHub 页是否还有下一页：

~~~csharp
private const string ApiUrl = "https://api.github.com/repos/Bian-Sh/Loom-X/releases";
private const int GitHubPageSize = 100;

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

private static bool HasNextPage(HttpResponseMessage response) =>
    response.Headers.TryGetValues("Link", out var values)
    && values.SelectMany(value => value.Split(','))
        .Any(part => part.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase));
~~~

CheckAsync 调用第一页正式 Release 数据，按 StableVersion 降序选择 Version 高于 CurrentVersion 且 InstallerAsset、ChecksumAsset 均非空的条目。日志只能包含当前版本、目标版本、条目数和耗时，不记录 Body 或 URL 查询中的敏感值。

**步骤 4：运行服务测试并确认绿灯**

运行同一步骤 2 命令。

预期：PASS；分页请求包含 page=1 和 page=2；历史结果包含无安装资产的正式版本；自动检查只选择兼容安装目标。

**步骤 5：勾选 OpenSpec 1.1、1.2 并提交**

~~~powershell
git add LoomX/Services/UpdateService.cs LoomX.Tests/UpdateServiceTests.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "实现正式版本分页与更新服务契约"
~~~

---

### Task 2：更新包准备、缓存复验与显式安装启动

**文件：**
- 修改：LoomX/Services/UpdateService.cs:84-351
- 修改：LoomX.Tests/UpdateServiceTests.cs:36-85
- 修改：openspec/changes/enhance-update-experience/tasks.md:7-9

**接口：**
- 消费：IUpdateInstallerLauncher.Launch、UpdateRelease.InstallerAsset、UpdateRelease.ChecksumAsset。
- 产出：PreparedUpdate、UpdatePreparationPhase、扩展后的 UpdateDownloadProgress、PrepareUpdateAsync、LaunchInstaller。

~~~csharp
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
    DateTimeOffset VerifiedAt);
~~~

**步骤 1：把旧“下载即安装”测试改成失败优先测试**

用以下测试替换 DownloadAndInstallAsync_ShouldVerifyChecksumBeforeLaunchingInstaller，并新增缓存与校验失败用例：

~~~csharp
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
~~~

**步骤 2：运行测试并确认红灯**

~~~powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateServiceTests"
~~~

预期：FAIL，PrepareUpdateAsync、PreparedUpdate 和 LaunchInstaller 尚不存在；旧实现会在准备完成后立即记录 Launcher.Path。

**步骤 3：实现 .partial 下载、缓存复验和准备阶段进度**

PrepareUpdateAsync 使用以下固定顺序：

~~~csharp
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
            await VerifyChecksumAsync(installerPath, checksumPath, cancellationToken);
            return new PreparedUpdate(release.Version, installerPath, DateTimeOffset.UtcNow);
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
        await VerifyChecksumAsync(installerPartial, checksumPartial, cancellationToken);
        File.Move(installerPartial, installerPath, true);
        File.Move(checksumPartial, checksumPath, true);
        return new PreparedUpdate(release.Version, installerPath, DateTimeOffset.UtcNow);
    }
    catch
    {
        TryDelete(installerPartial);
        TryDelete(checksumPartial);
        throw;
    }
}
~~~

LaunchInstaller 在调用 launcher 前验证 Version、InstallerPath 和文件存在性；方法只启动安装器，不退出应用：

~~~csharp
public void LaunchInstaller(PreparedUpdate preparedUpdate)
{
    ArgumentNullException.ThrowIfNull(preparedUpdate);
    if (string.IsNullOrWhiteSpace(preparedUpdate.Version)
        || string.IsNullOrWhiteSpace(preparedUpdate.InstallerPath)
        || !File.Exists(preparedUpdate.InstallerPath))
        throw new InvalidOperationException("已验证更新包不可用，请重新下载。");

    installerLauncher.Launch(preparedUpdate.InstallerPath);
    logger.LogInformation("更新安装器已启动 {Version}", preparedUpdate.Version);
}
~~~

删除 UpdateInstallResult 和 DownloadAndInstallAsync；下载方法继续只记录版本、资产名、字节数、耗时和结果。

**步骤 4：运行测试并确认绿灯**

运行步骤 2 命令。

预期：PASS；准备成功时启动次数为 0，显式调用后为 1；有效缓存不新增请求；校验失败不留下 .partial 文件。

**步骤 5：勾选 OpenSpec 1.3、1.4 并提交**

~~~powershell
git add LoomX/Services/UpdateService.cs LoomX.Tests/UpdateServiceTests.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "拆分更新包准备与安装器启动"
~~~


---

### Task 3：Release Notes 安全策略与内容模型

**文件：**
- 新建：LoomX/Services/ReleaseNotesMarkdownPolicy.cs
- 新建：LoomX/ViewModels/ReleaseNotesContentViewModel.cs
- 新建：LoomX.Tests/ViewModels/ReleaseNotesContentViewModelTests.cs
- 修改：openspec/changes/enhance-update-experience/tasks.md

**接口：**
- 产出：ReleaseNotesMarkdownPolicy.Sanitize(string?) 返回可交给 LiveMarkdown 的安全 Markdown。
- 产出：ReleaseNotesContentViewModel.SetRelease(UpdateRelease?)，以及 Title、PublishedAtText、Markdown、IsEmpty、EmptyText。

**步骤 1：先用 Context7 核对 API**

依次执行 resolve-library-id：Markdig（查询 0.43.0 的 Markdown.Parse、Descendants、SourceSpan、LinkInline.Url、LinkInline.IsImage），再 query-docs 查询 AST 节点遍历；对 LiveMarkdown.Avalonia 1.12.2 重复 resolve-library-id 和 query-docs，查询 MarkdownRenderer.MarkdownBuilder 与 ObservableStringBuilder。Context7 无 LiveMarkdown 条目时读取本机 NuGet README 和 XML，并在最终报告记录降级。

**步骤 2：写失败测试**

~~~csharp
[Fact]
public void Sanitize_移除Html图片和非Https目标()
{
    var source = """
        # 标题
        <script>alert(1)</script>
        ![图片](https://img.example/a.png)
        [安全](https://example.com) [危险](http://example.com)
        """;

    var result = ReleaseNotesMarkdownPolicy.Sanitize(source);

    Assert.Contains("# 标题", result);
    Assert.Contains("[安全](https://example.com)", result);
    Assert.Contains("危险", result);
    Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("img.example", result, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("http://example.com", result, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void SetRelease_替换版本时创建全新Builder()
{
    var vm = new ReleaseNotesContentViewModel();
    vm.SetRelease(CreateRelease("0.12.7", "第一版"));
    var first = vm.Markdown;
    vm.SetRelease(CreateRelease("0.12.8", "第二版"));

    Assert.NotSame(first, vm.Markdown);
    Assert.Equal("第二版", vm.Markdown.ToString());
    Assert.Equal("v0.12.8", vm.Title);
}
~~~

**步骤 3：运行红灯**

运行：dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ReleaseNotesContentViewModelTests"

预期：FAIL，新类型不存在。

**步骤 4：实现最小安全策略和内容替换**

ReleaseNotesMarkdownPolicy 用 Markdown.Parse 解析，收集 HtmlBlock、HtmlInline、LinkInline 的 SourceSpan；HTML 替换为空，图片替换为替代文本，非绝对 HTTPS 链接替换为可见文本，按 Span.Start 降序应用编辑。HTTPS 判定固定为：

~~~csharp
private static bool IsSafeHttps(string? value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri)
    && uri.Scheme == Uri.UriSchemeHttps;
~~~

SetRelease 每次创建 new ObservableStringBuilder，只在安全正文非空时 Append；监听 LocaleService.CultureChanged，重新计算日期和 EmptyText。所有 builder 操作在 UI 线程发生。

**步骤 5：运行绿灯并提交**

运行同一步骤 3 命令，预期 PASS。勾选 OpenSpec 3.1，然后执行：

~~~powershell
git add LoomX/Services/ReleaseNotesMarkdownPolicy.cs LoomX/ViewModels/ReleaseNotesContentViewModel.cs LoomX.Tests/ViewModels/ReleaseNotesContentViewModelTests.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "实现安全的更新说明内容模型"
~~~

---

### Task 4：UpdateCoordinator 自动准备状态机

**文件：**
- 修改：LoomX/ViewModels/UpdateCoordinator.cs
- 新建：LoomX.Tests/ViewModels/UpdateCoordinatorTests.cs
- 修改：openspec/changes/enhance-update-experience/tasks.md

**接口：**

~~~csharp
public enum UpdateStage { Idle, Checking, Downloading, Verifying, Ready, Installing, Latest, Error }
public enum UpdateErrorKind { None, Check, Prepare, Install }

public UpdateCoordinator(
    AppDataStore dataStore,
    IUpdateService? updateService = null,
    ILogger<UpdateCoordinator>? logger = null,
    Action? requestApplicationExit = null,
    IStringLocalizer<UpdateCoordinator>? localizer = null,
    Action<Action>? dispatch = null);
~~~

产出 CheckNowAsync、ToggleDialogCommand、DismissDialogCommand、RetryCommand、InstallAndRestartCommand，以及 IsUpdateEntryVisible、IsDialogVisible、IsProgressVisible、IsProgressIndeterminate、CanInstall、CanRetry、UpdateEntryText。

**步骤 1：写失败测试**

UpdateCoordinatorTests 固定覆盖：自动检查依次进入 Downloading、Verifying、Ready；两个并发 CheckNowAsync 只调用一次服务；稍后只关闭浮窗且 Prepare Token 未取消；准备失败保留 Release，Retry 不重新检查；Ready 连点两次只 Launch 一次且只请求一次退出；启动器失败不退出并恢复 Ready；手动无更新进入 Latest，自动无更新回 Idle 且入口隐藏。

核心断言示例：

~~~csharp
[Fact]
public async Task Ready重复安装只启动一次()
{
    var service = FakeUpdateService.WithPreparedUpdate();
    var exits = 0;
    using var vm = CreateCoordinator(service, () => exits++);
    await vm.CheckNowAsync(false);
    await service.PrepareCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    vm.InstallAndRestartCommand.Execute(null);
    vm.InstallAndRestartCommand.Execute(null);
    await service.InstallerLaunched.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Assert.Equal(1, service.LaunchCalls);
    Assert.Equal(1, exits);
}
~~~

**步骤 2：运行红灯**

运行：dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateCoordinatorTests"

预期：FAIL，Ready、新命令和 IUpdateService 注入边界不存在。

**步骤 3：实现状态机**

删除 Available、CardVisible、ReleaseNotesVisible、DownloadCommand、OpenReleaseNotesCommand、CloseReleaseNotesCommand 和纯文本 SanitizeMarkdown。以 checkTask、prepareTask 复用并发任务；Progress.Phase 为 Verifying 时切换校验态。所有状态变化只经 TransitionTo，集中刷新派生属性、命令状态和结构化日志。发现 Release 后立即显示浮窗并后台 Prepare；Dismiss 只隐藏浮窗。InstallAndRestartAsync 用 Interlocked 防重复，LaunchInstaller 成功后调用 requestApplicationExit；Launch 失败不退出。

~~~csharp
public bool IsUpdateEntryVisible => Stage is UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Ready
    || Stage == UpdateStage.Error && Release is not null;
public bool IsProgressVisible => Stage is UpdateStage.Downloading or UpdateStage.Verifying;
public bool IsProgressIndeterminate => Stage == UpdateStage.Verifying;
public bool CanInstall => Stage == UpdateStage.Ready && PreparedUpdate is not null;
public bool CanRetry => Stage == UpdateStage.Error && ErrorKind is UpdateErrorKind.Check or UpdateErrorKind.Prepare;
~~~

**步骤 4：运行绿灯并提交**

运行步骤 2 命令，预期 PASS。勾选 OpenSpec 2.1、2.2：

~~~powershell
git add LoomX/ViewModels/UpdateCoordinator.cs LoomX.Tests/ViewModels/UpdateCoordinatorTests.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "实现自动准备更新的全局状态机"
~~~

---

### Task 5：共享服务实例与正常退出接线

**文件：**
- 修改：LoomX/ViewModels/MainWindowViewModel.cs
- 修改：LoomX/App.axaml.cs
- 新建：LoomX.Tests/Views/UpdateExperienceContractTests.cs

**接口：**
- MainWindowViewModel 构造函数末尾新增 Action? requestApplicationExit = null。
- MainWindowViewModel 创建一个 IUpdateService，同时传给 UpdateCoordinator 和 ReleaseHistoryViewModel。
- App 传入 requestApplicationExit: () => desktop.Shutdown()，继续复用 desktop.Exit 的网关、数据存储、ViewModel 和日志释放路径。

**步骤 1：写接线契约红灯**

~~~csharp
[Fact]
public void 更新安装请求复用正常退出路径和共享服务()
{
    var app = ReadDesktopFile("App.axaml.cs");
    var vm = ReadDesktopFile("ViewModels", "MainWindowViewModel.cs");
    Assert.Contains("requestApplicationExit: () => desktop.Shutdown()", app);
    Assert.Contains("IUpdateService updateService = new UpdateService", vm);
    Assert.Contains("new UpdateCoordinator(this.dataStore, updateService", vm);
    Assert.Contains("new ReleaseHistoryViewModel(updateService", vm);
}
~~~

运行：dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateExperienceContractTests"

预期：FAIL。

**步骤 2：实现生产接线**

~~~csharp
IUpdateService updateService = new UpdateService(
    logger: this.loggerFactory.CreateLogger<UpdateService>(),
    currentVersion: AppVersion.Current);
updateCoordinator = new UpdateCoordinator(
    this.dataStore,
    updateService,
    this.loggerFactory.CreateLogger<UpdateCoordinator>(),
    requestApplicationExit);
releaseHistoryViewModel = new ReleaseHistoryViewModel(
    updateService,
    this.dataStore.GetUpdateProxySettingsAsync,
    this.loggerFactory.CreateLogger<ReleaseHistoryViewModel>());
~~~

MainWindowViewModel 负责释放共享协调器和历史模型；SettingsViewModel 对外部注入实例不重复 Dispose。

**步骤 3：运行绿灯并提交**

运行步骤 1 命令，预期 PASS。

~~~powershell
git add LoomX/App.axaml.cs LoomX/ViewModels/MainWindowViewModel.cs LoomX.Tests/Views/UpdateExperienceContractTests.cs
git commit -m "接入更新服务共享实例与正常退出"
~~~


---

### Task 6：Release History 分页、缓存与选择

**文件：**
- 新建：LoomX/ViewModels/ReleaseHistoryViewModel.cs
- 新建：LoomX.Tests/ViewModels/ReleaseHistoryViewModelTests.cs
- 修改：openspec/changes/enhance-update-experience/tasks.md

**接口：**

~~~csharp
public sealed class ReleaseHistoryViewModel : NotifyViewModel, IDisposable
{
    public ObservableCollection<ReleaseHistoryItemViewModel> Releases { get; }
    public ReleaseHistoryItemViewModel? SelectedRelease { get; set; }
    public ReleaseNotesContentViewModel Content { get; }
    public bool IsInitialLoading { get; }
    public bool IsRefreshing { get; }
    public bool IsLoadingMore { get; }
    public bool IsEmpty { get; }
    public bool HasError { get; }
    public bool HasCachedContent { get; }
    public bool HasMore { get; }
    public ICommand LoadCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default);
}
~~~

构造函数接收 IUpdateService、Func<CancellationToken, Task<UpdateProxySettings>>、ILogger 和可选 localizer/dispatch。

**步骤 1：写失败测试**

覆盖六个确定行为：首次请求 page=1,pageSize=10 并选择最高版本；最新与当前标记独立；切换版本不增加服务调用；加载更多按 Version 去重且保留选择；刷新失败保留集合和 Content；首次空响应进入 IsEmpty，首次失败进入 HasError 且可重试。

~~~csharp
[Fact]
public async Task 加载更多保留选择并去重追加()
{
    var service = FakeUpdateService.WithPages(
        Page(1, true, Release("0.13.0"), Release("0.12.9")),
        Page(2, false, Release("0.12.9"), Release("0.12.8")));
    using var vm = CreateHistory(service);
    await vm.EnsureLoadedAsync();
    vm.SelectedRelease = vm.Releases.Single(item => item.Release.Version == "0.12.9");

    vm.LoadMoreCommand.Execute(null);
    await service.SecondPageCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Assert.Equal(new[] { "0.13.0", "0.12.9", "0.12.8" }, vm.Releases.Select(item => item.Release.Version));
    Assert.Equal("0.12.9", vm.SelectedRelease?.Release.Version);
}
~~~

**步骤 2：运行红灯**

运行：dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ReleaseHistoryViewModelTests"

预期：FAIL，新 ViewModel 不存在。

**步骤 3：实现加载规则**

首次加载与刷新先写临时列表，成功后一次替换；刷新优先恢复原版本选择，否则选择最高 StableVersion。LoadMore 请求 currentPage+1，按标准化 Version 去重追加，不修改当前 Content。错误只设置本地化安全摘要，不使用 exception.Message；有缓存时 HasCachedContent=true，正文保持。CultureChanged 只刷新日期和用户可见状态，不发网络请求。

**步骤 4：运行绿灯并提交**

运行步骤 2 命令，预期 PASS。勾选 OpenSpec 5.1、5.2：

~~~powershell
git add LoomX/ViewModels/ReleaseHistoryViewModel.cs LoomX.Tests/ViewModels/ReleaseHistoryViewModelTests.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "实现正式版本历史状态模型"
~~~

---

### Task 7：共享 ReleaseNotesView

**文件：**
- 新建：LoomX/Views/ReleaseNotesView.axaml
- 新建：LoomX/Views/ReleaseNotesView.axaml.cs
- 新建：LoomX.Tests/Views/ReleaseNotesViewContractTests.cs
- 修改：openspec/changes/enhance-update-experience/tasks.md

**接口：**
- DataContext 固定为 ReleaseNotesContentViewModel。
- 唯一正文控件为 md:MarkdownRenderer MarkdownBuilder={Binding Markdown}；组件不包含网络、安装命令或外部 URL 逻辑。

**步骤 1：写 AXAML 契约红灯**

~~~csharp
[Fact]
public void 共享视图使用LiveMarkdown且只有一个滚动正文区()
{
    var source = ReadDesktopFile("Views", "ReleaseNotesView.axaml");
    Assert.Contains("xmlns:md=\"using:LiveMarkdown.Avalonia\"", source);
    Assert.Contains("<md:MarkdownRenderer MarkdownBuilder=\"{Binding Markdown}\"", source);
    Assert.Equal(1, source.Split("<ScrollViewer", StringSplitOptions.None).Length - 1);
    Assert.DoesNotContain("HttpClient", source);
}
~~~

运行：dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ReleaseNotesViewContractTests"

预期：FAIL，文件不存在。

**步骤 2：实现视图**

~~~xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:md="using:LiveMarkdown.Avalonia"
             x:Class="LoomX.Views.ReleaseNotesView">
  <Grid>
    <ScrollViewer IsVisible="{Binding HasContent}"
                  VerticalScrollBarVisibility="Auto"
                  HorizontalScrollBarVisibility="Disabled">
      <md:MarkdownRenderer MarkdownBuilder="{Binding Markdown}" />
    </ScrollViewer>
    <TextBlock IsVisible="{Binding IsEmpty}" Text="{Binding EmptyText}"
               Foreground="{DynamicResource TextSecondaryBrush}"
               TextWrapping="Wrap" HorizontalAlignment="Center" VerticalAlignment="Center" />
  </Grid>
</UserControl>
~~~

ReleaseNotesContentViewModel 明确定义 public bool HasContent => !IsEmpty；SetRelease 刷新 IsEmpty 时同时通知 HasContent，不新增转换器依赖。

**步骤 3：运行绿灯并提交**

运行步骤 1 命令，预期 PASS。勾选 OpenSpec 3.2：

~~~powershell
git add LoomX/Views/ReleaseNotesView.axaml LoomX/Views/ReleaseNotesView.axaml.cs LoomX.Tests/Views/ReleaseNotesViewContractTests.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "提取共享更新说明视图"
~~~

---

### Task 8：标题栏更新入口与扁平更新浮窗

**文件：**
- 修改：LoomX/MainWindow.axaml
- 修改：LoomX/MainWindow.axaml.cs
- 修改：LoomX.Tests/Views/MainWindowChromeContractTests.cs
- 修改：LoomX.Tests/Views/UpdateExperienceContractTests.cs
- 修改：openspec/changes/enhance-update-experience/tasks.md

**接口：**
- 标题栏 Grid 从 *,42,42,42 改为 *,Auto,42,42,42；入口在最小化按钮左侧。
- 浮窗绑定 Update.ReleaseNotesContent、进度和四类 Footer；Escape 与关闭按钮执行 DismissDialogCommand。

**步骤 1：写视图契约红灯**

断言 MainWindow.axaml 含 ColumnDefinitions="*,Auto,42,42,42"、Update.IsUpdateEntryVisible、Update.UpdateEntryText、Update.ToggleDialogCommand、views:ReleaseNotesView；不再含 Update.CardVisible、Update.ReleaseNotesVisible、Update.OpenReleaseNotesCommand 或旧 TextBlock ReleaseNotes。断言 Footer 分别绑定 IsDownloading、IsVerifying、CanInstall、CanRetry；Toast border 仍存在且位于浮窗后方的独立层。

运行：dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MainWindowChromeContractTests|FullyQualifiedName~UpdateExperienceContractTests"

预期：FAIL，旧卡片仍存在且标题栏列数不符。

**步骤 2：实现紧凑入口**

入口按钮高度 32、最小命中区 32，默认只显示图标；内部文字 Width=0、Opacity=0，Button:pointerover 和 Button:focus 时 Width 展开到 220、Opacity=1，使用 DoubleTransition。背景按 IsReady、IsError 和准备中状态绑定现有 AccentSoftBrush、DangerSoftBrush、SurfaceMutedBrush；ToolTip.Tip 与 AutomationProperties.Name 均绑定 UpdateEntryText。

**步骤 3：实现单层浮窗**

浮窗最大宽度 760、最大高度为窗口可用区，Header 显示版本、发布日期与状态；正文只放 ReleaseNotesView；Footer：Downloading 显示字节、速度、百分比和确定进度；Verifying 显示不确定进度；Ready 显示“稍后 / 重启并安装”；Error 显示安全摘要、“稍后 / 重试”。所有颜色使用 DynamicResource，遮罩不使用参考图黑金色。

MainWindow.axaml.cs 在 Update.IsDialogVisible 变为 true 后通过 Dispatcher.UIThread.Post 聚焦主操作或关闭按钮；KeyDown 收到 Escape 时执行 DismissDialogCommand；现有 IsInsideButton 保证点击入口不会触发拖动。

**步骤 4：运行绿灯、勾选 4.1 至 4.4 并提交**

运行步骤 1 命令，预期 PASS。

~~~powershell
git add LoomX/MainWindow.axaml LoomX/MainWindow.axaml.cs LoomX.Tests/Views/MainWindowChromeContractTests.cs LoomX.Tests/Views/UpdateExperienceContractTests.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "实现标题栏更新入口与更新浮窗"
~~~

---

### Task 9：设置页历史分栏、本地化与日志安全

**文件：**
- 修改：LoomX/ViewModels/SettingsViewModel.cs
- 修改：LoomX/Views/SettingsView.axaml
- 修改：LoomX/Resources/Strings.resx
- 修改：LoomX/Resources/Strings.en-US.resx
- 修改：LoomX/Resources/Strings.zh-TW.resx
- 修改：LoomX/Resources/Strings.ja-JP.resx
- 修改：LoomX.Tests/Views/SettingsViewContractTests.cs
- 修改：LoomX.Tests/LocalizationNoCjkTest.cs
- 修改：LoomX.Tests/LocalizationResourceParityTest.cs
- 修改：openspec/changes/enhance-update-experience/tasks.md

**接口：**
- SettingsViewModel 新增 int SelectedTabIndex 和 ReleaseHistoryViewModel ReleaseHistory。
- SelectedTabIndex 切换到索引 1 时调用 ReleaseHistory.EnsureLoadedAsync；重复进入不重复请求。

**步骤 1：写设置页和本地化红灯**

SettingsViewContractTests 断言原 VersionLabel、AutoCheckUpdates、UseProxyForUpdates、CheckUpdateCommand 四项仍存在；TabControl 双向绑定 SelectedTabIndex；历史区包含 RefreshCommand、Releases、SelectedRelease、LoadMoreCommand、ReleaseNotesView 和加载/空/无缓存错误/有缓存错误状态。LocalizationNoCjkTest 将 UpdateCoordinator.cs、ReleaseHistoryViewModel.cs、ReleaseNotesContentViewModel.cs 纳入扫描。ResourceParity 继续要求 neutral、en-US、zh-TW 键完全一致，ja-JP 所有值非空。

运行：

~~~powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~LocalizationNoCjkTest|FullyQualifiedName~LocalizationResourceParityTest"
~~~

预期：FAIL，历史分栏、选项卡生命周期和资源键不存在。

**步骤 2：实现设置页布局**

保留现有更新设置 panel；把自动检查提示改为“启动及每 24 小时检查，发现兼容版本后在后台下载并等待确认安装”。新增固定高度约 430 的 Release Notes panel：Header 为标题、非阻塞错误、刷新按钮；左列宽 200，ListBox 显示版本、日期、“最新/当前”徽标，底部加载更多；右列显示标题、日期和共享 ReleaseNotesView。首次加载、空态、无缓存错误覆盖整个阅读区；有缓存错误只显示 Header 提示。

**步骤 3：补齐资源和动态刷新**

新增键前缀：update.entry.*、update.dialog.*、update.progress.*、release.notes.empty、settings.update.history.*。四个 resx 都写非空值；en-US 与 zh-TW 占位符顺序与 neutral 完全一致。UpdateCoordinator 和 ReleaseHistoryViewModel 监听 CultureChanged 后重新计算状态、入口文案、日期和错误摘要，不缓存已格式化字符串。

日志测试使用 RecordingLogger 断言消息不含 Release Body、测试 API Key、代理密码或响应正文；仅允许版本、页码、条数、阶段、字节数和耗时。

**步骤 4：运行绿灯并提交**

运行步骤 1 命令，再运行：dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateCoordinatorTests|FullyQualifiedName~ReleaseHistoryViewModelTests|FullyQualifiedName~UpdateServiceTests"

预期：全部 PASS。勾选 OpenSpec 2.3、5.3、5.4：

~~~powershell
git add LoomX/ViewModels/SettingsViewModel.cs LoomX/Views/SettingsView.axaml LoomX/Resources/Strings.resx LoomX/Resources/Strings.en-US.resx LoomX/Resources/Strings.zh-TW.resx LoomX/Resources/Strings.ja-JP.resx LoomX.Tests/Views/SettingsViewContractTests.cs LoomX.Tests/LocalizationNoCjkTest.cs LoomX.Tests/LocalizationResourceParityTest.cs openspec/changes/enhance-update-experience/tasks.md
git commit -m "完善设置页版本历史与更新本地化"
~~~

---

### Task 10：完整验证、CUA 与时间命名发布包

**文件：**
- 新建：docs/superpowers/reports/2026-09-20-enhance-update-experience-verify.md
- 修改：openspec/changes/enhance-update-experience/tasks.md
- 生成但不纳入 Git：outputs/yyyyMMdd-HHmmss-enhance-update-experience/

**接口：**
- 消费全部实现，不新增产品接口。
- 产出测试、构建、OpenSpec、CUA、进程路径和发布文件完整性证据。

**步骤 1：运行定向与完整自动化验证**

~~~powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~UpdateCoordinatorTests|FullyQualifiedName~ReleaseHistoryViewModelTests|FullyQualifiedName~ReleaseNotesContentViewModelTests|FullyQualifiedName~ReleaseNotesViewContractTests|FullyQualifiedName~UpdateExperienceContractTests|FullyQualifiedName~MainWindowChromeContractTests|FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~Localization"
dotnet test LoomX.slnx -c Release --no-restore
dotnet build LoomX.slnx -c Release --no-restore
openspec validate enhance-update-experience --type change --strict --no-interactive
git diff --check
~~~

预期：所有测试 PASS；Build 0 error；OpenSpec 输出 change valid；git diff --check 无输出。出现任何失败先加载 systematic-debugging，写最小失败测试后修复，不以重跑掩盖问题。

**步骤 2：发布到唯一新目录**

~~~powershell
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = "outputs/$stamp-enhance-update-experience"
if (Test-Path -LiteralPath $outputDir) { throw "发布目录已存在：$outputDir" }
./scripts/publish-desktop.ps1 -Configuration Release -OutputDirectory $outputDir
$executables = @(Get-ChildItem -LiteralPath $outputDir -Filter *.exe -File)
if ($executables.Count -ne 1 -or $executables[0].Name -ne "LoomX.exe") { throw "发布入口不唯一" }
~~~

预期：目录名符合 yyyyMMdd-HHmmss-enhance-update-experience，只包含一个 exe 入口 LoomX.exe，并保留 publish.log。不得删除或覆盖其他 outputs 子目录。

**步骤 3：用受控假 Release 启动验证模式**

在 Debug 构建中增加仅由 LOOMX_UPDATE_PREVIEW 环境变量启用的测试接线，值固定支持 downloading、verifying、ready、error、history-empty；该接线只在 #if DEBUG 内创建 FakeUpdateService，Release 构建不包含预览分支。先用 Debug 预览逐态执行 CUA，再用 cua-driver launch_app 启动新发布目录的 LoomX.exe 做真实启动、设置页和进程路径冒烟。每次操作前后都调用 get_window_state，截图只包含 Loom-X 窗口。

逐项验证：浅色、深色、关闭透明效果；标题栏默认紧凑、Hover 展开且不挤压窗口按钮；下载浮窗显示 Markdown、字节、速度、百分比并可稍后；校验态为不确定进度；Ready 仅显示稍后和重启并安装；Error 可重试；关闭后入口保留；设置页首次 10 条、最新/当前徽标、切换不请求、加载更多保留选择；空态和有/无缓存错误可读；普通 Toast 不与浮窗重叠。透明主题截图只作辅助，不单独判定配色。

**步骤 4：校验实际进程路径并写报告**

~~~powershell
$expected = (Resolve-Path "$outputDir/LoomX.exe").Path
$process = Get-CimInstance Win32_Process -Filter "Name='LoomX.exe'" | Where-Object { $_.ExecutablePath -eq $expected }
if (-not $process) { throw "未找到从发布目录启动的 LoomX 进程：$expected" }
~~~

验证报告记录每条命令、结果、发布目录、进程路径、CUA 截图路径、主题结论、Context7 查询结果或降级原因，以及日志敏感信息检查结果。

**步骤 5：完成 Comet 任务边界并提交**

勾选 OpenSpec 6.1 至 6.5，确认 tasks.md 的 22 项均为已完成；运行 git status --short，列出并保留无关 Session 产物。提交报告和任务状态：

~~~powershell
git add docs/superpowers/reports/2026-09-20-enhance-update-experience-verify.md openspec/changes/enhance-update-experience/tasks.md
git commit -m "完成更新体验集成验证与发布"
~~~

预期：outputs 保持 ignored，不进入提交；不修改或删除非本 change 文件。

## 自查结果

- **规格覆盖：** OpenSpec 1.1 至 6.5 的 22 项均映射到 Task 1 至 Task 10；自动准备、持久入口、浮窗直接 Markdown、设置页最近 10 条、分页、缓存失败保留、代理、资产命名、安全日志和最终发布均有明确任务。
- **占位符扫描：** 未发现禁止占位词、延后实现表述或跨任务模糊引用；每个实现任务均给出文件、接口、红灯、绿灯、命令和中文提交消息。
- **类型一致性：** IUpdateService、UpdateReleasePage、PreparedUpdate、UpdateDownloadProgress、UpdateStage、UpdateErrorKind、ReleaseNotesContentViewModel、ReleaseHistoryViewModel 的签名在生产接线、测试和视图任务中保持一致。
- **流程边界：** Unity 不参与；Avalonia 资产不执行 Reimport；最终发布目录唯一且先验证不存在；CUA 只操作 Loom-X 窗口；正常退出仍由 desktop.Exit 完成。

## 14. 归档前验收补充设计

2026-09-21 的验收反馈扩大了更新体验的最终交付范围，以下内容覆盖此前“Ready 时重新打开 Release Notes 并直接安装”的交互：

1. `updateDialogOverlay` 改为完整窗口覆盖层，遮罩固定 `#A6000000`，不进入 `WindowAppearanceCoordinator` 的透明度资源缩放。弹窗相对于完整窗口居中，不再为左侧导航栏保留 `228px` 偏移。
2. 更新说明容器使用独立的 `ExperimentalAcrylicBorder` 材质；Avalonia 不支持 Acrylic 时使用高不透明度主题表面回退。磨砂材料只负责内容容器，外层遮罩始终固定纯黑 65%。
3. `ReleaseNotesContentViewModel` 在完成安全清洗后，将三个约定的二级标题切分为独立 section。每个 section 持有自己的 `ObservableStringBuilder` 与默认 `IsExpanded = true`；旧正文没有约定标题时回退为单一兼容 section。
4. 右上角关闭按钮替换为“前往发布页”，通过当前 `UpdateRelease.HtmlUrl` 打开 HTTPS 页面；关闭动作继续由 Footer 的“稍后”承担。
5. Ready 状态下，标题栏入口和浮窗安装按钮统一调用应用内确认流程。确认流程由可复用 `AppModalHost` 提供，不创建独立 Window；正文明确提示应用重启、路由服务短暂中断和进行中请求可能失败。取消后保持 Ready，确认后才调用现有一次性安装命令。
6. Inno Setup 桌面快捷方式从默认未勾选任务改为无条件创建，覆盖全新安装与升级。
7. 使用真实 GitHub Release `v0.12.7` 的测试正文验证三段结构；正文只包含虚构的安全测试信息，不包含敏感信息。

### 14.1 补充测试顺序

1. 先补 Release Notes 分段、默认展开、旧正文回退测试，并确认旧实现失败。
2. 补主窗口固定遮罩、完整窗口居中、发布页按钮和 Acrylic 容器契约测试。
3. 补 Ready 入口不再打开 Release Notes、确认/取消安装风险模态的协调器测试。
4. 补安装器桌面快捷方式无条件创建的文本契约测试。
5. 完成实现后运行相关测试、完整 Release 构建、透明/非透明 CUA 验证，并重新发布到可读时间目录。
