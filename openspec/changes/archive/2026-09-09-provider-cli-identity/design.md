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

三路统一走 `HttpClient`，超时 5s，失败静默降级到缓存 → 默认。

### D3：缓存策略

- 路径：`%LocalAppData%/LoomX/cli-versions.json`（复用 `AppDataPathResolver`）
- TTL：24h；过期时后台刷新，同时先用旧值显示
- 格式：
  ```json
  {
    "claude_code": { "version": "2.1.263", "fetchedAt": "...", "source": "npm_registry" },
    "codex":       { "version": "0.153.4", "fetchedAt": "...", "source": "github_releases" },
    "grok":        { "version": "1.0.6",   "fetchedAt": null,    "source": "default" }
  }
  ```
- 用户手改 Grok 版本时，缓存条目不覆盖用户值；Grok 的缓存仅记录"上次用户手改值"

### D4：家族剥除逻辑

沿用 LiveAgent 的 `CLI_IDENTITY_HEADER_FAMILIES` 思路：

```csharp
// 三家 CLI 各家族的完整头键集合
static readonly Dictionary<CliIdentityType, string[]> FamilyHeaders = new() {
    { ClaudeCode, ["User-Agent", "x-stainless-*", "anthropic-version"] },
    { Codex,      ["User-Agent", "originator", "version"] },
    { Grok,       ["User-Agent", "x-grok-client-identifier", "x-grok-client-version",
                    "x-grok-client-mode", "X-XAI-Token-Auth", "x-authenticateresponse"] },
};

ApplyCliIdentity(headers, targetType) {
    // 1. 剥除：移除所有 family 中出现过的头键
    foreach (var (family, _) in headers)
        if (IsInAnyFamily(family)) headers.Remove(family);
    // 2. 构造：把 target 家族的完整头（UA + 静态头）合并写入
    foreach (var (k, v) in BuildTargetHeaders(targetType, version))
        headers[k] = v;
}
```

### D5：UI 交互 —— ToggleButton + Popup 组合

Avalonia 无 `DropdownMenu` 控件。三个候选：

- **A. Menu**（标准菜单）：交互符合系统预期，Escape 关闭、外点关闭免费；但样式定制受限
- **B. ToggleButton + Popup**（LiveAgent 风格）：完全可控样式，需自己处理焦点、Escape、外点关闭
- **C. CheckBox + ListBox**：简单但视觉不像下拉

**选择 A（Menu）**：交互可靠，Avalonia 内置行为；样式通过 Menu / MenuItem 类选择器微调即可。
理由：本项目已有 `Menu` 使用经验（`MainWindow.axaml` 顶部导航），无需新学习曲线；`Menu` 的默认外观已足够接近 LiveAgent 的下拉效果。

### D6：命令绑定方式

沿用现有 `NotifyViewModel` + `RelayCommand` 模式（`LoomX/ViewModels/MainWindowViewModel.cs`）：

```csharp
public ICommand ApplyCliIdentityCommand { get; }
// CanExecute: SelectedProvider != null
// Execute: provider.ApplyCliIdentity(param as CliIdentityItemViewModel)
```

## 数据流

### 打开 Provider → 请求 tab
1. `ProviderEditorViewModel` 构造 → 读 `HeadersJson` → 匹配 `FamilyHeaderKeys` 反推当前 CLI → 设置 `CurrentCliIdentity`
2. 订阅 `CliIdentityService.VersionChanged` 事件
3. 初始化 `CliIdentities` 为三家 profile，版本从 `CliVersionCache` 读

### 打开 Popup
1. 若 `CliVersionCache.IsStale()` → 后台触发 `CliVersionService.RefreshAsync()`
2. 后台刷新完成 → 通知 `VersionChanged` → ViewModel 更新 `CliIdentities` 版本 → 若检测到新版本 → 置 `HasNewVersion=true`

### 用户选中某家 CLI
1. `ApplyCliIdentityCommand.Execute(item)` → `provider.ApplyCliIdentity(item.Type, item.Version)`
2. `provider` 内部：调用 `CliIdentityService.ApplyCliIdentity(headers, type, version)`
3. 剥除其他家族头 → 合并目标家族头
4. `HeadersJson` 重新序列化 → 触发 `NotifyPropertyChange` → 保存路径拾取

### 用户手改版本
1. 编辑 `item.Version`（TextBox）
2. `Version` setter 触发 `CliIdentityItemViewModel.OnVersionChanged`
3. 若当前身份就是该 CLI → 重新套用；否则不立即生效

### 保存 → 关闭 → 重开
- `HeadersJson` 已持久化，重开时反推当前身份，`CliIdentities` 版本号从缓存读，展示一致状态

## 与 LiveAgent 的对齐 / 差异

| 项 | LiveAgent | LoomX 本变更 |
|---|---|---|
| UI 位置 | Provider 弹窗「请求」tab 内 header 区旁边 | 同款（LoomX 无 modal，就地嵌入 tab 内容） |
| 三家 CLI | claude_code / codex / xai | claude_code / codex / xai |
| UA 模板 | 写死在 customHeaders.ts | 集中在 `CliIdentityService` 常量，便于校对 |
| 版本来源 | 全部写死 | 动态获取（Claude/Codex）+ 默认（Grok） |
| 家族剥除 | `CLI_IDENTITY_HEADER_FAMILIES` + `buildCliIdentityHeaders` | 同款思路，C# 实现 |
| 缓存 | 无 | AppData JSON + 24h TTL |
| 用户手改 | 可以（直接改 header） | 可以（改 item.Version 或直接改 header） |

## 测试策略

- **单元测试**：`LoomX.Tests/CliIdentityServiceTests.cs`
  - 剥除家族逻辑：给定包含三家混头的 headers → 应用 Claude → 只留 Claude 家族
  - 版本解析：给一段 npm JSON / GitHub JSON → 正确解析出 version
  - 缓存 TTL：过期 / 未过期 / 缺失三种状态
- **手动测试**：打包发布后跑验收场景 1-10（见 proposal.md）

## 后续可演进（非本次范围）

- 支持"从当前 headers 自动识别当前 CLI 身份"（用 `FamilyHeaderKeys` 匹配）—— 本次已在 ViewModel 反推逻辑中初步实现
- CLI 模板自定义 UI（用户可创建自定义 CLI 家族）
- 后台定时刷新（如每小时检查一次）
- 支持更多 CLI（如 Gemini、DeepSeek 官方 CLI）
