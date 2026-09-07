# Comet Design Handoff

- Change: provider-cli-identity
- Phase: design
- Mode: compact
- Context hash: 1ce9a3daf5af39cd3e0c8084a3bf5c134be524329e3456e6e5f2233c1e88481f

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/provider-cli-identity/proposal.md

- Source: openspec/changes/provider-cli-identity/proposal.md
- Lines: 1-77
- SHA256: a143aef0a4ad79de9fbfa674666bea1f218f105ad54a1744449f56bbe1b3e69a

```md
# Provider 面板「模拟 CLI」：一键写入官方 CLI 身份请求头

## 背景

LiveAgent 桌面端的 Provider 编辑弹窗在「请求」tab 内有一个 `Fingerprint` 图标的「模拟 CLI / Mimic CLI」下拉按钮（`crates/agent-ui/src/pages/settings/ProviderModalView.tsx:1037-1078`），一键写入三家官方 CLI 的整套身份请求头（`User-Agent` + 随附头），并按「CLI 家族」整套剥除其他家的头——避免只替换 `User-Agent` 残留 `X-Stainless-*` / `originator` / `x-grok-*` 拼出假指纹。

调研发现 LiveAgent 的三家 CLI 版本号全部写死在 `crates/agent-ui/src/lib/providers/customHeaders.ts:95-97`，且已明显过期：

- `CLAUDE_CLI_VERSION = "2.1.88"` → npm 实测已 `2.1.263`
- `CODEX_CLI_VERSION = "0.151.0"` → GitHub releases 实测已 `0.153.4`
- `GROK_CLI_VERSION = "1.0.6"` → 无公开源，无法自动获取

LoomX 的 Provider「请求」tab 目前只有手填 header 能力（`LoomX/Views/ProvidersView.axaml:112-125`），缺少一键套用 CLI 身份的入口。本变更同时补齐**功能**和**动态版本**两点。

## 目标

1. 在 Provider「请求」tab 的「自定义请求头」区域旁边新增「模拟 CLI」下拉按钮，就地套用三家 CLI 身份头，位置与交互对齐 LiveAgent。
2. Claude Code 版本从 npm registry 动态获取、Codex 版本从 GitHub releases 动态获取；均缓存 24h，网络失败降级为本地缓存或写死默认值。
3. 套用身份时按 CLI 家族整套剥除其他 CLI 的头，避免拼假指纹。
4. 补齐「当前身份」指示（在徽章旁或 header 区标签显示当前应用的 CLI 类型 + 版本）。

## 范围

**包含**
- `LoomX/Services/CliIdentityService.cs`（新）——三家 CLI 的 `User-Agent` 模板、随附身份头、家族键集合、剥除+合并逻辑
- `LoomX/Services/CliVersionService.cs`（新）——npm / GitHub 版本拉取
- `LoomX/Services/CliVersionCache.cs`（新）——AppData 文件缓存 + 24h TTL
- `LoomX/ViewModels/MainWindowViewModel.cs`——`ProviderEditorViewModel` 新增 `CliIdentities` / `CurrentCliIdentity` / `ApplyCliIdentityCommand` / `RefreshCliVersionsCommand`
- `LoomX/Views/ProvidersView.axaml`——「模拟 CLI」下拉按钮 + Popup（Avalonia 无 `DropdownMenu` 控件，用 `ToggleButton`+`ListBox` 或 `Menu` 组合）
- `LoomX/Views/ProvidersView.axaml.cs`——Popup 交互
- `LoomX/Resources/Strings.resx` + `Strings.en-US.resx`——新增 `providers.cli.*` 命名空间键
- `LoomX.Tests/`——新增 `CliIdentityService` 单测（家族剥除、版本获取、缓存 TTL）

**不包含**
- 不改现有 `Provider.HeadersJson` 存储格式（`CliIdentity` 结果仍写入 Headers 字典）
- 不新增独立 tab 或页面（就地嵌入 header 区）
- 不做 CLI 模板自定义编辑器（用户手改 header 已够）
- 不从本地 `grok --version` 读版本（复杂度收益比低）
- 不改数据库 schema / 迁移
- 不改测试连接、模型同步、API Key 存储逻辑
- 不新增国际化新语言（除 en-US 外）
- 不做国际化扫描单测（由 i18n-phase-2 已建立的 `LocalizationNoCjkTest` 覆盖）

## 非目标

- **不做** CLI 模板自定义 UI：用户想自定义 CLI 家族时，直接手改 header（本变更把 `x-grok-client-version` 等键放进 `CliIdentity` 结果即可，用户可编辑）
- **不做** 本地 CLI 二进制版本探测（如 `claude --version`）：依赖 CLI 是否安装、路径探测复杂，收益低
- **不做** 后台自动定时刷新（24h TTL 已足够，用户可手动点按钮触发刷新）
- **不做** 多 provider 间共享 CLI 版本缓存（按 AppData 全局缓存即可，不区分 provider）

## 依赖

- 现有 `Provider.HeadersJson` 存字典字符串的机制（`LoomX/Services/ProviderStore.cs`）
- 现有 `HttpClient` + `AppDataPathResolver`（`LoomX/Services/AppDataPathResolver.cs`）
- 现有 `Strings.resx` / `LocaleService`（i18n-phase-2 已建立）
- 网络访问：`registry.npmjs.org` + `api.github.com`（可被 `LoomX.UseProxy` 走全局代理）

## 风险

- **Grok 版本无公开源**：无 npm 包、无 GitHub releases，只能沿用 LiveAgent 当前 `1.0.6` 作默认，允许用户手改
- **CLI 头结构演进**：三家 CLI 随版本可能改动 SDK 头集合、`originator` / `version` 命名。需在 `CliIdentityService` 集中常量定义，加 TODO 注释指向 LiveAgent 参考位置，便于后期校对
- **Avalonia 无 `DropdownMenu` 控件**：`Menu`/`Popup` 组合有若干交互坑（焦点、Escape、外点关闭）。需选择合适方案：`ToggleButton`+`ListBox`+`Popup` 组合或直接用 `Menu`
- **跨域网络**：`registry.npmjs.org` 在国内可能访问受限。需走 `LoomX.UseProxy` 或降级到缓存
- **GitHub API rate limit**：未认证请求 60 req/hr；缓存 24h 后每小时最多一次拉取，安全

## 验收

1. **UI 存在性**：打开 Provider → 「请求」tab → 「自定义请求头」标题右侧、「添加」按钮左侧出现「模拟 CLI」按钮，带指纹图标
2. **下拉展开**：点按钮 → 弹出下拉，列出 `Claude Code` / `Codex` / `Grok` 三项，每项右侧显示当前版本号（等宽字体、灰字）
3. **动态版本**：首次点按钮 → 触发版本拉取 → 显示当前缓存或默认版本；24h 后自动刷新；显示「网络失败，使用缓存 x.y.z」提示
4. **一键写入**：选中某家 CLI → 「自定义请求头」区自动填入整套头（`User-Agent` + 随附头）；「添加」按钮不激活该头
5. **家族剥除**：已应用 Codex → 切换到 Claude → Codex 的 `originator` / `version` 头被剥除，仅保留 Claude 家族的 `User-Agent` + `x-stainless-*`
6. **保存**：套用的头通过现有 `HeadersJson` 序列化路径落库；下次打开 Provider 恢复显示
7. **手改兼容**：用户手改套用后的任一 header 值 → 不破坏「当前 CLI 身份」标记；点其他 CLI → 按新家族重写
8. **降级**：网络失败 + 缓存缺失 → 使用写死默认版本（Claude 2.1.88、Codex 0.151.0、Grok 1.0.6），不阻塞用户操作
9. **构建**：`dotnet build LoomX.slnx` 零 error 零新 warning；`dotnet test` 全绿
10. **桌面验证**：打包发布后手动验证完整流程

```

## openspec/changes/provider-cli-identity/design.md

- Source: openspec/changes/provider-cli-identity/design.md
- Lines: 1-191
- SHA256: 1199132ff4e3cf63eaf058a7692ebde16f1b97913a42f88f1010ff7baf2fc7ed

[TRUNCATED]

```md
# 高层设计：Provider CLI 身份模拟

## 分层架构

本变更在 LoomX 现有的三层栈上叠加一个薄层，不引入新的架构层：

```
┌─ ProvidersView.axaml (UI)
│    新增「模拟 CLI」ToggleButton + Popup
│    绑定 SelectedProvider.CliIdentities 与 ApplyCliIdentityCommand
├─ MainWindowViewModel.cs (ViewModel)
│    ProviderEditorViewModel 新增 CliIdentities、CurrentCliIdentity
│    新增 ApplyCliIdentityCommand、RefreshCliVersionsCommand
│    订阅 CliIdentityService 版本刷新事件
├─ CliIdentityService.cs (Service, 新)
│    构造 CLI 头集合、家族剥除、合并、序列化
│    调用 CliVersionService 获取版本
├─ CliVersionService.cs (Service, 新)
│    npm / GitHub / 默认三路版本来源
├─ CliVersionCache.cs (Service, 新)
│    AppData JSON 文件缓存 + 24h TTL
└─ 复用现有 HttpClient / AppDataPathResolver / LocaleService
```

## 关键数据模型

### CliIdentityType 枚举
```csharp
enum CliIdentityType { ClaudeCode, Codex, Grok }
```

### CliIdentityProfile（静态配置）
```csharp
sealed record CliIdentityProfile(
    CliIdentityType Type,
    string DisplayName,
    string UaTemplate,          // 例如 "claude-cli/{version} (external, cli)"
    IReadOnlyList<KeyValuePair<string,string>> StaticHeaders,  // 除 UA 外的静态头
    IReadOnlyList<string> FamilyHeaderKeys  // 用于剥除本家族的头键集合
);
```

### CliVersionInfo（动态版本）
```csharp
sealed record CliVersionInfo(
    CliIdentityType Type,
    string Version,
    DateTimeOffset FetchedAt,
    CliVersionSource Source  // NpmRegistry | GitHubReleases | Default | Cached | UserOverridden
);
```

### CliIdentityItemViewModel（下拉项 VM）
```csharp
sealed class CliIdentityItemViewModel : NotifyViewModel {
    public CliIdentityType Type { get; }
    public string DisplayName { get; }
    public string Version { get; set; }           // 支持用户手改
    public string ShortDescription { get; }        // 例如 "claude-cli/2.1.263"
    public bool IsRecommended { get; }             // 当前 provider.ApiMode 匹配时
    public bool IsApplied { get; set; }            // 当前正在使用的
    public bool HasNewVersion { get; }             // 检测到新版本
}
```

## 核心决策

### D1：数据存储位置 —— 直接写入 Headers 字典，不引入新表

`CliIdentity` 结果不落到独立的数据库字段。套用后仍写入 `Provider.HeadersJson`（`LoomX/Configuration/ConfigurationModel.cs` 已有字段），保持与手动加头的一致性。

- **好处**：不动数据库 schema；迁移零成本；用户删掉某家 CLI 的头也自然生效
- **代价**：没有"上次用了哪家 CLI"的持久标记 → 通过 header 内容匹配反推当前身份（用 `FamilyHeaderKeys` 判定）

### D2：版本来源分层

- **Claude**：`GET https://registry.npmjs.org/@anthropic-ai/claude-code/latest`，取 `.version`
- **Codex**：`GET https://api.github.com/repos/openai/codex/releases/latest`，取 `.name`（如 `0.153.4`），回退 `.tag_name` 去 `rust-` 前缀
- **Grok**：写死默认 `1.0.6`，用户可手改


```

Full source: openspec/changes/provider-cli-identity/design.md

## openspec/changes/provider-cli-identity/tasks.md

- Source: openspec/changes/provider-cli-identity/tasks.md
- Lines: 1-109
- SHA256: 844cad6e50a0faeb44a11dcbb5f323836f5b9a4b9b8c319f57ad5f23147f3abc

[TRUNCATED]

```md
# 任务清单

- [ ] 1. 新增 `LoomX/Services/CliIdentityService.cs`
  - 定义 `CliIdentityType` 枚举（ClaudeCode / Codex / Grok）
  - 定义 `CliIdentityProfile` 静态配置（UA 模板、静态头、家族键集合）
  - 实现 `BuildCliIdentityHeaders(type, version)`：返回完整头字典
  - 实现 `ApplyCliIdentity(headers, type, version)`：剥除其他家族头 + 合并目标头
  - 实现 `DetectCliIdentity(headers)`：反推当前身份（可选）
  - 加 TODO 注释指向 LiveAgent 参考位置便于后期校对

- [ ] 2. 新增 `LoomX/Services/CliVersionService.cs`
  - 实现 `GetClaudeVersionAsync()`：`GET https://registry.npmjs.org/@anthropic-ai/claude-code/latest` → `.version`
  - 实现 `GetCodexVersionAsync()`：`GET https://api.github.com/repos/openai/codex/releases/latest` → `.name`（回退 `.tag_name` 去 `rust-` 前缀）
  - 实现 `GetGrokVersionAsync()`：返回默认 `1.0.6`
  - 统一 `HttpClient` 超时 5s，失败抛 `CliVersionFetchException`
  - 支持走 `LoomX.UseProxy`（复用现有代理配置）

- [ ] 3. 新增 `LoomX/Services/CliVersionCache.cs`
  - AppData 路径：`{AppData}/LoomX/cli-versions.json`
  - `LoadAsync()` / `SaveAsync()` / `IsStale()`（24h TTL）
  - `GetOrFetchAsync(type, force=false)`：缓存新鲜 → 返回；过期 → 后台刷新 + 先返回缓存；缺失 → 触发 fetch → 失败降级默认
  - 用户手改 Grok 时，缓存条目不覆盖

- [ ] 4. 创建 `CliIdentityItemViewModel`
  - 属性：`Type`、`DisplayName`、`Version`（可写）、`ShortDescription`（等宽预览）、`IsRecommended`、`IsApplied`、`HasNewVersion`
  - 订阅 `CliVersionService.VersionChanged` → 更新 `Version` / `HasNewVersion`
  - `Version` setter：若当前身份即该 CLI → 触发重新套用

- [ ] 5. 扩展 `ProviderEditorViewModel`
  - 新增 `CliIdentities: ObservableCollection<CliIdentityItemViewModel>`
  - 新增 `CurrentCliIdentity: CliIdentityType?`
  - 新增 `ApplyCliIdentityCommand: IAsyncRelayCommand<CliIdentityItemViewModel>`
  - 新增 `RefreshCliVersionsCommand: IAsyncRelayCommand`
  - `LoadHeaders()` 时反推 `CurrentCliIdentity`
  - `ApplyCliIdentity` 内部：调 `CliIdentityService.ApplyCliIdentity` → 更新 `Headers` → 更新 `CurrentCliIdentity` → 通知变化

- [ ] 6. 修改 `LoomX/Views/ProvidersView.axaml`
  - 「请求」tab 内，「自定义请求头」标题右侧、"添加"按钮左侧，新增 Menu 或 ToggleButton+Popup
  - 下拉项模板：CLI 名称（左）+ 版本号（等宽灰字，右）+「推荐」标签 +「已应用」标记
  - 版本号支持内联编辑（Grok 用 TextBox 支持手改）
  - 顶部按钮：指纹图标 + 「模拟 CLI」+ 下拉箭头
  - 底部：「刷新版本」小按钮 + 「当前：X 版本」状态行

- [ ] 7. 修改 `LoomX/Views/ProvidersView.axaml.cs`
  - Popup 交互（如选 ToggleButton 方案）
  - 版本号内联编辑事件
  - Escape / 外点关闭行为

- [ ] 8. 新增 `LoomX/Resources/Strings.resx` 键
  - `providers.cli.button` = "模拟 CLI"
  - `providers.cli.refresh` = "刷新版本"
  - `providers.cli.recommended` = "推荐"
  - `providers.cli.applied` = "已应用"
  - `providers.cli.current.prefix` = "当前：{0}"
  - `providers.cli.claude` = "Claude Code"
  - `providers.cli.codex` = "Codex"
  - `providers.cli.grok` = "Grok"
  - `providers.cli.version.fetching` = "获取中…"
  - `providers.cli.version.fromCache` = "缓存"
  - `providers.cli.version.fromDefault` = "默认"
  - `providers.cli.version.fromUser` = "手改"
  - `providers.cli.version.failed` = "获取失败，使用缓存"
  - `providers.cli.tooltip` = "一键套用官方 CLI 身份请求头"

- [ ] 9. 新增 `LoomX/Resources/Strings.en-US.resx` 对应翻译
  - `providers.cli.button` = "Mimic CLI"
  - `providers.cli.refresh` = "Refresh versions"
  - `providers.cli.recommended` = "Recommended"
  - `providers.cli.applied` = "Applied"
  - `providers.cli.current.prefix` = "Current: {0}"
  - `providers.cli.claude` = "Claude Code"
  - `providers.cli.codex` = "Codex"
  - `providers.cli.grok` = "Grok"
  - `providers.cli.version.fetching` = "Fetching..."
  - `providers.cli.version.fromCache` = "Cached"
  - `providers.cli.version.fromDefault` = "Default"
  - `providers.cli.version.fromUser` = "User-set"
  - `providers.cli.version.failed` = "Fetch failed, using cache"
  - `providers.cli.tooltip` = "One-click CLI identity headers"


```

Full source: openspec/changes/provider-cli-identity/tasks.md
