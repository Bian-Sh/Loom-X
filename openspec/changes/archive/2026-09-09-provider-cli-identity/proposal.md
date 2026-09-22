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
