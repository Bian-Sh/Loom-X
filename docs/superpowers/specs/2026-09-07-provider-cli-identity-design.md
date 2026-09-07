---
comet_change: provider-cli-identity
role: technical-design
canonical_spec: openspec
---

# Provider CLI 身份模拟 · 技术设计

## 1. 上下文

LoomX 的 Provider 编辑弹窗（`LoomX/Views/ProvidersView.axaml`「请求」tab）目前只提供手填 header 能力。LiveAgent 桌面端已经实现了一个「模拟 CLI」下拉按钮，一键写入三家官方 CLI 的整套身份请求头，本变更把该能力移植过来并做两点增强：**版本号动态获取**（避免 LiveAgent 那种写死过期的问题）与**版本缓存 + 网络失败降级**。

关键上游调研（已在 open 阶段 proposal.md 记录）：
- LiveAgent `customHeaders.ts:95-97` 三家 CLI 版本号全部写死且已过期
- LiveAgent `buildCliIdentityHeaders` + `CLI_IDENTITY_HEADER_FAMILIES` 定义了「家族剥除 + 合并」的核心逻辑
- Claude / Codex 各有官方公开版本源（npm / GitHub），Grok 无

## 2. 目标与非目标

**目标**（来自 proposal.md）：
1. 在 Provider「请求」tab 加入「模拟 CLI」下拉按钮，就地套用三家 CLI 身份头
2. Claude / Codex 动态拉版本，Grok 默认 + 手改，24h 缓存 + 失败降级
3. 套用身份时按家族剥除其他 CLI 的头
4. 显示当前 CLI 身份标记

**非目标**（同 proposal.md）：
- 不改数据库 schema
- 不新增独立 tab 或页面
- 不做 CLI 模板自定义 UI
- 不从本地 CLI 二进制探测版本
- 不新增国际化新语言

## 3. 架构

### 3.1 分层

沿用 LoomX 现有分层，不引入新层：

```
┌─ ProvidersView.axaml
│    新增 Menu 组件，绑定 SelectedProvider.CliIdentities
│    MenuItem 模板：CLI 名称 + 版本（等宽）+ 推荐标签 + 已应用标记
├─ ProviderEditorViewModel (MainWindowViewModel.cs)
│    CliIdentities: ObservableCollection<CliIdentityItemViewModel>
│    CurrentCliIdentity: CliIdentityType?
│    ApplyCliIdentityCommand: IAsyncRelayCommand<CliIdentityItemViewModel>
│    RefreshCliVersionsCommand: IAsyncRelayCommand
├─ CliIdentityService（新）
│    静态配置 + 家族剥除 + 合并 + 反推
├─ CliVersionService（新）
│    npm / GitHub / 默认三路版本来源
├─ CliVersionCache（新）
│    AppData JSON + 24h TTL
└─ HttpClient / AppDataPathResolver / LocaleService（复用）
```

### 3.2 关键类型

**CliIdentityType 枚举**：
```csharp
namespace LoomX.Services;

public enum CliIdentityType
{
    ClaudeCode,
    Codex,
    Grok
}
```

**CliIdentityProfile 静态配置**（记录型）：
```csharp
public sealed record CliIdentityProfile(
    CliIdentityType Type,
    string DisplayName,
    string UaTemplate,              // "claude-cli/{version} (external, cli)"
    IReadOnlyList<KeyValuePair<string,string>> StaticHeaders,  // 除 UA 外的静态头
    IReadOnlyList<string> FamilyHeaderKeys  // 剥除本家族的头键集合（可含通配符）
);
```

**CliVersionInfo 动态版本**：
```csharp
public sealed record CliVersionInfo(
    CliIdentityType Type,
    string Version,
    DateTimeOffset? FetchedAt,
    CliVersionSource Source
);

public enum CliVersionSource { NpmRegistry, GitHubReleases, Default, Cached, UserOverridden }
```

**CliIdentityItemViewModel**：
```csharp
public sealed class CliIdentityItemViewModel : NotifyViewModel
{
    public CliIdentityType Type { get; }
    public string DisplayName { get; }
    public string Version { get; set; }         // 支持用户手改
    public string ShortDescription { get; }      // "claude-cli/2.1.263"
    public bool IsRecommended { get; }           // 当前 provider.ApiMode 匹配时
    public bool IsApplied { get; set; }          // 当前正在使用
    public bool HasNewVersion { get; set; }      // 检测到新版本
    public CliVersionSource Source { get; set; } // 显示来源标签
}
```

## 4. 关键决策

### D1：数据存储位置 —— 直接写入 Headers 字典

**决策**：不新增数据库字段，`CliIdentity` 结果通过现有 `Provider.HeadersJson` 落库。

**依据**：
- LiveAgent 也是把 CLI 身份头写进 `CustomHeader[]`，用户手动添加的 header 也走同一数组
- 避免数据库 schema 迁移
- 用户删掉某家 CLI 的头即自然生效

**代价与应对**：
- 无"上次用了哪家 CLI"持久标记 → 通过 header 内容匹配反推（见 §5.4 `DetectCliIdentity`）
- 反推逻辑放在 `ProviderEditorViewModel.LoadHeaders()` 内部，用户不会感知

### D2：版本来源分层

**决策**：
- Claude：`GET https://registry.npmjs.org/@anthropic-ai/claude-code/latest` → JSON `.version`
- Codex：`GET https://api.github.com/repos/openai/codex/releases/latest` → JSON `.name`，回退 `.tag_name` 去 `rust-` 前缀
- Grok：无公开源，默认 `1.0.6` + 用户可手改

**依据**：
- 上一轮调研已实测确认三家源可用性
- Codex GitHub release tag 有 `rust-` 前缀（如 `rust-v0.153.4`），需处理

**HTTP 配置**：
- `HttpClient` 超时 5s
- 复用 `LoomX.UseProxy`（走现有代理配置）
- 未认证 GitHub 请求限 60 req/hr，24h 缓存足够

**失败降级链**：
```
fetch → catch → cache → catch → default → 通知用户"使用默认版本"
```

### D3：缓存策略

**决策**：
- 路径：`%LocalAppData%/LoomX/cli-versions.json`（通过 `AppDataPathResolver`）
- TTL：24h
- 格式：
  ```json
  {
    "claude_code": { "version": "2.1.263", "fetchedAt": "2026-09-07T02:10:00Z", "source": "npm_registry" },
    "codex":       { "version": "0.153.4", "fetchedAt": "2026-09-07T02:10:00Z", "source": "github_releases" },
    "grok":        { "version": "1.0.6",   "fetchedAt": null,                 "source": "default" }
  }
  ```
- 用户手改 Grok 版本时，缓存条目不覆盖

**缓存 API**：
```csharp
public interface ICliVersionCache
{
    Task<CliVersionInfo> GetOrFetchAsync(CliIdentityType type, bool forceRefresh = false, CancellationToken ct = default);
    bool IsStale(CliIdentityType type);
    void SetUserOverride(CliIdentityType type, string version);  // Grok 手改
    bool HasUserOverride(CliIdentityType type);
}
```

### D4：家族剥除逻辑

**决策**：沿用 LiveAgent `CLI_IDENTITY_HEADER_FAMILIES` 思路。每家 CLI 有完整头键集合，切换身份时先按集合剥除其他家族的头，再合并目标家族的头。

**实现**：
```csharp
public static class CliIdentityService
{
    // 三家 CLI 各家族的完整头键集合
    private static readonly Dictionary<CliIdentityType, IReadOnlyList<string>> FamilyHeaders = new()
    {
        [CliIdentityType.ClaudeCode] = [
            "User-Agent",
            "anthropic-version",
            "x-stainless-lang",
            "x-stainless-package-version",
            "x-stainless-os",
            "x-stainless-arch",
            "x-stainless-runtime",
            "x-stainless-timeout",
            "x-stainless-retries",
        ],
        [CliIdentityType.Codex] = [
            "User-Agent",
            "originator",
            "version",
        ],
        [CliIdentityType.Grok] = [
            "User-Agent",
            "x-grok-client-identifier",
            "x-grok-client-version",
            "x-grok-client-mode",
            "X-XAI-Token-Auth",
            "x-authenticateresponse",
        ],
    };

    public static Dictionary<string, string> ApplyCliIdentity(
        Dictionary<string, string> headers,
        CliIdentityType targetType,
        string version)
    {
        // 1. 剥除所有已知家族键（跨全部家族）
        var allKeys = FamilyHeaders.Values.SelectMany(k => k).Distinct().ToList();
        foreach (var key in allKeys)
            if (headers.Remove(key, out _)) { /* removed */ }

        // 2. 构造目标家族完整头（UA + 静态头）
        var profile = ProfileByType[targetType];
        var newHeaders = BuildCliIdentityHeaders(profile, version);

        // 3. 合并同名
        foreach (var (k, v) in newHeaders)
            headers[k] = v;

        return headers;
    }

    public static CliIdentityType? DetectCliIdentity(Dictionary<string, string> headers)
    {
        // 检查哪一家 CLI 的 UA 存在（UA 是唯一必带头）
        foreach (var (type, profile) in ProfileByType)
        {
            if (headers.TryGetValue("User-Agent", out var ua) &&
                ua.StartsWith(profile.UaPrefix, StringComparison.OrdinalIgnoreCase))
                return type;
        }
        return null;
    }
}
```

**参考位置**（便于后期校对）：
- LiveAgent `crates/agent-ui/src/lib/providers/customHeaders.ts:95-197`
- 三家 CLI 源码：
  - Claude：`anthropic-sdk-typescript/src/_fetch.ts`
  - Codex：`openai/codex-rs/codex-apps-ui/src/lib/api.rs`
  - Grok：`xai-org/grok-cli`（私有）

### D5：UI 交互 —— Menu 组件

**决策**：使用 Avalonia 内置 `Menu` 组件，不用 ToggleButton+Popup 组合。

**理由**：
- Avalonia `Menu` 内置 Escape 关闭、外点关闭、键盘导航
- 项目已有 Menu 使用经验（`MainWindow.axaml` 顶部导航）
- 样式通过 `Menu` / `MenuItem` 类选择器可微调
- 无需自实现焦点管理（ToggleButton+Popup 风险高）

**UI 结构**：
```xml
<Grid ColumnDefinitions="Auto,Auto">
  <Button Content="{l:Locale providers.cli.button}" Command="{Binding SelectedProvider.RefreshCliVersionsCommand}">
    <Button.Icon>
      <PathIcon Data="{StaticResource FingerprintIcon}" />
    </Button.Icon>
  </Button>
  <Menu Grid.Column="1">
    <MenuItem Header="{l:Locale providers.cli.identity}">
      <ItemsControl ItemsSource="{Binding SelectedProvider.CliIdentities}">
        <MenuItem ... />
      </ItemsControl>
    </MenuItem>
  </Menu>
</Grid>
```

**位置**：ProvidersView「请求」tab，「自定义请求头」标题右侧、"添加"按钮左侧。

### D6：命令绑定

**决策**：沿用现有 `NotifyViewModel` + `IAsyncRelayCommand` 模式。

**新增命令**：
```csharp
public IAsyncRelayCommand<CliIdentityItemViewModel> ApplyCliIdentityCommand { get; }
public IAsyncRelayCommand RefreshCliVersionsCommand { get; }
```

**CanExecute**：`SelectedProvider != null`

## 5. 数据流

### 5.1 打开 Provider → 请求 tab

1. `ProviderEditorViewModel` 构造
2. 读 `HeadersJson` → 反序列化 `Headers`
3. 调 `CliIdentityService.DetectCliIdentity(headers)` → 设置 `CurrentCliIdentity`
4. 初始化 `CliIdentities` = 三家 profile
5. 从 `CliVersionCache` 读版本 → 设置 `CliIdentityItemViewModel.Version` / `Source`
6. 订阅 `CliVersionService.VersionChanged` 事件

### 5.2 打开 Popup / 点「刷新」

1. 若 `CliVersionCache.IsStale()` → 后台触发 `CliVersionService.RefreshAsync()`
2. 后台刷新完成 → 通知 `VersionChanged` 事件
3. ViewModel 更新 `CliIdentities` 版本
4. 若检测到新版本（旧缓存 vs 新拉取不同）→ 置 `HasNewVersion = true`

### 5.3 用户选中某家 CLI

1. `ApplyCliIdentityCommand.Execute(item)`
2. `provider.ApplyCliIdentity(item.Type, item.Version)`
3. 内部调 `CliIdentityService.ApplyCliIdentity(headers, type, version)`
4. 剥除其他家族头 → 合并目标家族头
5. `HeadersJson` 重新序列化 → 触发 `NotifyPropertyChange`
6. 现有保存路径拾取（`ProviderStore.SaveAsync`）

### 5.4 反推当前身份（打开时）

```csharp
CurrentCliIdentity = CliIdentityService.DetectCliIdentity(Headers);
foreach (var item in CliIdentities)
    item.IsApplied = item.Type == CurrentCliIdentity;
```

### 5.5 用户手改 Grok 版本

1. `CliIdentityItemViewModel.Version` setter 触发
2. 若 `IsApplied` → 调 `RefreshAppliedIdentity()`
3. `RefreshAppliedIdentity`：调 `CliIdentityService.ApplyCliIdentity(headers, type, newVersion)`
4. 通知缓存 `SetUserOverride(grok, newVersion)`（下次不覆盖用户值）

## 6. 边界与异常

### 6.1 网络失败
- `HttpClient` 超时或异常 → 静默降级到缓存 → 缓存缺失降级到默认
- 用户可见：版本显示 `Source = Default` 或 `Cached`，不弹错误
- 日志：`LogDebug` 记录失败原因（非用户可见）

### 6.2 空 headers
- `DetectCliIdentity` 返回 `null`
- `CliIdentities` 仍显示三家，均无「已应用」标记

### 6.3 用户手改 UA 值破坏身份
- `DetectCliIdentity` 匹配失败（UA 前缀不匹配）
- 视为未应用任何 CLI，`CurrentCliIdentity = null`
- 用户可重新选择任一 CLI 覆盖

### 6.4 Grok 缓存 vs 手改冲突
- 用户手改后，`SetUserOverride` 记录
- 下次 `GetOrFetchAsync(grok)` 时，若 `HasUserOverride` → 跳过默认值写入缓存

### 6.5 GitHub API rate limit
- 未认证请求 60 req/hr
- 24h 缓存 → 每用户每小时最多一次拉取
- 无 rate limit 处理（失败降级即可）

## 7. 测试策略

### 7.1 单元测试（新增 3 个测试文件）

**`CliIdentityServiceTests.cs`**（核心）：
- `BuildCliIdentityHeaders_ClaudeCode_ReturnsFullHeaderSet`：构造完整头集合
- `BuildCliIdentityHeaders_Codex_ReturnsOriginatorAndVersionHeaders`
- `BuildCliIdentityHeaders_Grok_ReturnsXaiClientHeaders`
- `ApplyCliIdentity_RemovesOtherFamilyHeaders`：**核心**——给定三家混头，应用 Claude → 只留 Claude 家族
- `ApplyCliIdentity_SameFamilyOverwrites`：同一家族应用新版本，旧值被覆盖
- `ApplyCliIdentity_PreservesNonFamilyHeaders`：非家族 header 不动
- `DetectCliIdentity_ReturnsMatchedFamily`：识别已应用的 CLI
- `DetectCliIdentity_ReturnsNoneWhenEmpty`
- `DetectCliIdentity_ReturnsNoneWhenUaMismatched`：UA 被手改后返回 null

**`CliVersionServiceTests.cs`**：
- `ParseClaudeVersion_ExtractsVersionFromNpmJson`：给一段 npm JSON，正确解析出 version
- `ParseCodexVersion_ExtractsVersionFromGitHubJson`：`.name` 字段
- `ParseCodexVersion_FallsBackToTagName`：`.name` 缺失时用 `.tag_name` 去 `rust-` 前缀
- `FetchFailure_ReturnsCachedValue`：mock HttpClient 抛异常 → 返回缓存
- `FetchFailure_NoCache_ReturnsDefault`：mock HttpClient 抛异常 + 缓存缺失 → 返回默认

**`CliVersionCacheTests.cs`**：
- `IsStale_FreshCache_ReturnsFalse`：24h 内
- `IsStale_ExpiredCache_ReturnsTrue`：超过 24h
- `IsStale_MissingCache_ReturnsTrue`
- `GetOrFetchAsync_FreshCache_ReturnsCached`：不触发 fetch
- `GetOrFetchAsync_ExpiredCache_TriggersRefresh`：触发 fetch，先返回旧值
- `UserOverrides_GrokVersion_NotOverwrittenByCache`：手改值保护

### 7.2 手动验证（打包发布后）

按 proposal.md 验收场景 1-10 走一遍：
1. UI 存在性
2. 下拉展开
3. 动态版本
4. 一键写入
5. 家族剥除
6. 保存
7. 手改兼容
8. 降级
9. 构建
10. 桌面验证

## 8. 与 LiveAgent 的对齐 / 差异

| 项 | LiveAgent | LoomX 本变更 |
|---|---|---|
| UI 位置 | Provider 弹窗「请求」tab 内 header 区旁边 | 同款（LoomX 无 modal，就地嵌入 tab 内容） |
| 三家 CLI | claude_code / codex / xai | 同款 |
| UA 模板 | 写死在 customHeaders.ts | 集中在 `CliIdentityService` 常量 |
| 版本来源 | 全部写死 | 动态获取（Claude/Codex）+ 默认（Grok） |
| 家族剥除 | `CLI_IDENTITY_HEADER_FAMILIES` | 同款思路，C# 实现 |
| 缓存 | 无 | AppData JSON + 24h TTL |
| 用户手改 | 可以（改 header） | 可以（改 `item.Version` 或改 header） |

## 9. 演进路线（非本次范围）

- 支持从当前 headers 自动识别 CLI 身份（已初步实现）
- CLI 模板自定义 UI（用户可创建自定义 CLI 家族）
- 后台定时刷新（如每小时检查一次）
- 支持更多 CLI（Gemini、DeepSeek 官方 CLI）
- 版本变更通知（新版本发布时 toast 提示）

## 10. 相关文件清单

**新增**：
- `LoomX/Services/CliIdentityService.cs`
- `LoomX/Services/CliVersionService.cs`
- `LoomX/Services/CliVersionCache.cs`
- `LoomX.Tests/CliIdentityServiceTests.cs`
- `LoomX.Tests/CliVersionServiceTests.cs`
- `LoomX.Tests/CliVersionCacheTests.cs`

**修改**：
- `LoomX/Views/ProvidersView.axaml`
- `LoomX/Views/ProvidersView.axaml.cs`
- `LoomX/ViewModels/MainWindowViewModel.cs`
- `LoomX/Resources/Strings.resx`
- `LoomX/Resources/Strings.en-US.resx`
