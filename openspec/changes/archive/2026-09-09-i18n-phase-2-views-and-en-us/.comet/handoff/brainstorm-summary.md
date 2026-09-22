# Brainstorm Summary

- Change: i18n-phase-2-views-and-en-us
- Date: 2026-09-06

## 确认的技术方案

- **Phase 2 范围**：迁移 `ProvidersView` / `GatewayView` / `PlaceholderView` / `GatewayViewModel` / `MainWindowViewModel` 中所有 UI 文案硬编码中文；新增 `Strings.en-US.resx` 全量翻译；新增 CJK 扫描测试与键一致性测试。
- **架构**：完全沿用 Phase 1 的 `LocaleService` + `ResourceManager` + `{l:Locale}` 标记扩展 + `IStringLocalizer<T>` 组合。不引入新机制。
- **ViewModel Status 模式（方案 A）**：`Status = Loc(key)` 或 `Status = string.Format(CultureInfo.CurrentCulture, Loc(key), args)`；`OnCultureChanged` 只刷新派生属性，不主动重设 Status。与 Phase 1 `SettingsViewModel` / `OverviewViewModel` 完全一致。
- **AXAML 模式**：静态文本用 `{l:Locale}`；带数字的字符串改用「数字绑定 + 静态翻译文本」分离显示，不用 `StringFormat` 嵌套 `{l:Locale}`。
- **csproj**：新增 `<EmbeddedResource Include="Resources\Strings.*.resx" />` 通配符，让 SDK 自动为 `Strings.en-US.resx` 生成卫星程序集。
- **日志文案不迁移**：`logger.Log*("...中文...")` 保持原样，按 `AGENTS.md` 日志规范不本地化。
- **en-US 翻译策略**：品牌专名（Loom-X / Provider / Gateway / Endpoint / Combo / Ollama / OpenAI / Anthropic / DPAPI / Base URL）保留；开发者工具简洁正式风格；占位符 `{0}` `{1}` 保留原位置。

## 关键取舍与风险

- **取舍**：Status 属性不响应式刷新，切语言时短暂保持旧文本，下次业务事件覆盖。选择理由：与 Phase 1 完全一致，改动最小，Status 语义是"操作反馈快照"而非"UI 静态文案"。
- **风险 1**：`MainWindowViewModel.cs` 内嵌套了 `OverviewViewModel` / `ProviderEditorViewModel` / `ModelEditorViewModel` 等多个 `private sealed class`，每个类都需要自己的 `IStringLocalizer<T>`，不能共享外层 `_loc`。已在设计中标注，实施时需要逐个类添加。
- **风险 2**：`LocalizationNoCjkTest` 可能误伤日志文案（`logger.LogError("概览刷新失败")`）。缓解：白名单跳过含 `logger.` / `LogWarning` / `LogError` / `LogInformation` / `LogDebug` / `LogTrace` / `LogCritical` 的行。
- **风险 3**：en-US 翻译过长可能破 UI 布局。缓解：依赖现有弹性布局，Phase 2 不引入新布局；UI 走查作为验收的一部分。

## 测试策略

- `LocalizationNoCjkTest`：扫描 `LoomX/**/*.axaml` 与 `LoomX/**/*.cs`（排除 `Resources/`、日志调用行、注释行）不含 CJK 字符。
- `LocalizationResourceParityTest`：断言 `en-US.keys ⊇ zh-CN.keys` 且每个 en-US 值非空。
- `dotnet build`：确保卫星程序集 `bin/Debug/<tfm>/en-US/LoomX.resources.dll` 生成。
- `dotnet test`：全部测试通过。
- 手工走查：切 en-US 后无中文残留、无 `[key]` 占位回退。

## Spec Patch

新增 `openspec/changes/i18n-phase-2-views-and-en-us/specs/localization/spec.md`，包含 4 个 Requirement + Scenario：
- UI text must not be hardcoded in Chinese
- en-US translation coverage parity
- Reactive culture switching
- Culture fallback chain（satellite → neutral → key name）
