# 任务清单

- [x] 1. 新增 `LoomX/Services/CliIdentityService.cs`
  - 定义 `CliIdentityType` 枚举（ClaudeCode / Codex / Grok）
  - 定义 `CliIdentityProfile` 静态配置（UA 模板、静态头、家族键集合）
  - 实现 `BuildCliIdentityHeaders(type, version)`：返回完整头字典
  - 实现 `ApplyCliIdentity(headers, type, version)`：剥除其他家族头 + 合并目标头
  - 实现 `DetectCliIdentity(headers)`：反推当前身份（可选）
  - 加 TODO 注释指向 LiveAgent 参考位置便于后期校对

- [x] 2. 新增 `LoomX/Services/CliVersionService.cs`
  - 实现 `GetClaudeVersionAsync()`：`GET https://registry.npmjs.org/@anthropic-ai/claude-code/latest` → `.version`
  - 实现 `GetCodexVersionAsync()`：`GET https://api.github.com/repos/openai/codex/releases/latest` → `.name`（回退 `.tag_name` 去 `rust-` 前缀）
  - 实现 `GetGrokVersionAsync()`：返回默认 `1.0.6`
  - 统一 `HttpClient` 超时 5s，失败抛 `CliVersionFetchException`
  - 支持走 `LoomX.UseProxy`（复用现有代理配置）

- [x] 3. 新增 `LoomX/Services/CliVersionCache.cs`
  - AppData 路径：`{AppData}/LoomX/cli-versions.json`
  - `LoadAsync()` / `SaveAsync()` / `IsStale()`（24h TTL）
  - `GetOrFetchAsync(type, force=false)`：缓存新鲜 → 返回；过期 → 后台刷新 + 先返回缓存；缺失 → 触发 fetch → 失败降级默认
  - 用户手改 Grok 时，缓存条目不覆盖

- [x] 4. 创建 `CliIdentityItemViewModel`
  - 属性：`Type`、`DisplayName`、`Version`（可写）、`ShortDescription`（等宽预览）、`IsRecommended`、`IsApplied`、`HasNewVersion`
  - 订阅 `CliVersionService.VersionChanged` → 更新 `Version` / `HasNewVersion`
  - `Version` setter：若当前身份即该 CLI → 触发重新套用

- [x] 5. 扩展 `ProviderEditorViewModel`
  - 新增 `CliIdentities: ObservableCollection<CliIdentityItemViewModel>`
  - 新增 `CurrentCliIdentity: CliIdentityType?`
  - 新增 `ApplyCliIdentityCommand: IAsyncRelayCommand<CliIdentityItemViewModel>`
  - 新增 `RefreshCliVersionsCommand: IAsyncRelayCommand`
  - `LoadHeaders()` 时反推 `CurrentCliIdentity`
  - `ApplyCliIdentity` 内部：调 `CliIdentityService.ApplyCliIdentity` → 更新 `Headers` → 更新 `CurrentCliIdentity` → 通知变化

- [x] 6. 修改 `LoomX/Views/ProvidersView.axaml`
  - 「请求」tab 内，「自定义请求头」标题右侧、"添加"按钮左侧，新增 Menu 或 ToggleButton+Popup
  - 下拉项模板：CLI 名称（左）+ 版本号（等宽灰字，右）+「推荐」标签 +「已应用」标记
  - 版本号支持内联编辑（Grok 用 TextBox 支持手改）
  - 顶部按钮：指纹图标 + 「模拟 CLI」+ 下拉箭头
  - 底部：「刷新版本」小按钮 + 「当前：X 版本」状态行

- [x] 7. 修改 `LoomX/Views/ProvidersView.axaml.cs`
  - Popup 交互（如选 ToggleButton 方案）
  - 版本号内联编辑事件
  - Escape / 外点关闭行为

- [x] 8. 新增 `LoomX/Resources/Strings.resx` 键
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

- [x] 9. 新增 `LoomX/Resources/Strings.en-US.resx` 对应翻译
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

- [x] 10. 新增 `LoomX.Tests/CliIdentityServiceTests.cs`
  - `BuildCliIdentityHeaders_ClaudeCode_ReturnsFullHeaderSet`
  - `BuildCliIdentityHeaders_Codex_ReturnsOriginatorAndVersionHeaders`
  - `BuildCliIdentityHeaders_Grok_ReturnsXaiClientHeaders`
  - `ApplyCliIdentity_RemovesOtherFamilyHeaders`（核心：三家混头 → 应用一家 → 只留一家）
  - `ApplyCliIdentity_SameFamilyOverwrites`
  - `DetectCliIdentity_ReturnsMatchedFamily`
  - `DetectCliIdentity_ReturnsNoneWhenEmpty`

- [x] 11. 新增 `LoomX.Tests/CliVersionServiceTests.cs`
  - `ParseClaudeVersion_ExtractsVersionFromNpmJson`
  - `ParseCodexVersion_ExtractsVersionFromGitHubJson`
  - `ParseCodexVersion_FallsBackToTagName`
  - `FetchFailure_ReturnsCachedValue`
  - `FetchFailure_NoCache_ReturnsDefault`

- [x] 12. 新增 `LoomX.Tests/CliVersionCacheTests.cs`
  - `IsStale_FreshCache_ReturnsFalse`
  - `IsStale_ExpiredCache_ReturnsTrue`
  - `IsStale_MissingCache_ReturnsTrue`
  - `GetOrFetchAsync_FreshCache_ReturnsCached`
  - `GetOrFetchAsync_ExpiredCache_TriggersRefresh`
  - `UserOverrides_GrokVersion_NotOverwrittenByCache`

- [x] 13. 构建 + 桌面验证
  - `dotnet build LoomX.slnx` 零 error 零新 warning
  - `dotnet test` 全绿
  - `dotnet publish -c Release` 生成安装包
  - 手动跑 proposal.md 验收场景 1-10
