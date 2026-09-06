# Comet Design Handoff

- Change: i18n-phase-2-views-and-en-us
- Phase: design
- Mode: compact
- Context hash: 70b980c7021419b3db56c7cd8d01804f1fcbe311324789e8d331071a1d40a8a7

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/i18n-phase-2-views-and-en-us/proposal.md

- Source: openspec/changes/i18n-phase-2-views-and-en-us/proposal.md
- Lines: 1-62
- SHA256: 6e801add1f90df2c72fbc0480ca752582c9f60da6f5cc9ae5f948048c64eea40

```md
# i18n Phase 2：剩余视图迁移与 en-US 首版翻译

## 背景

Loom-X 在 Phase 1（commit `7a66e9d`）已建立 ResX/XLIFF 本地化基础设施：
- `LoomX/Resources/Strings.resx` 作为唯一源文件，当前 zh-CN 含 234 个键
- `LocaleService` 全局文化状态管理，`CultureChanged` 事件驱动 UI 刷新
- `{l:Locale}` AXAML 标记扩展、`IStringLocalizer<T>` ViewModel 访问
- 已迁移 `SettingsView`、`MainWindow`、`MainWindowViewModel`、`SettingsViewModel`、`OverviewView`、`ActivityView`、`ConsoleView`

Phase 1 结束时 `docs/i18n.md` 明确列出「ProvidersView、GatewayView、ConsoleView、ActivityView、OverviewView（后续阶段）」为待办。实际扫描显示 `Overview/Activity/Console` 已在 Phase 1 内完成，`ProvidersView`、`GatewayView` 与两个 ViewModel 中仍有中文硬编码残留，且 `Strings.en-US.resx` 尚未创建。

## 目标

1. 迁移 `ProvidersView`、`GatewayView`、`PlaceholderView` 与 `GatewayViewModel`、`MainWindowViewModel` 中所有剩余的硬编码中文到 `Strings.resx`，键名沿用 `providers.*` / `gateway.*` / `placeholder.*` 命名空间。
2. 新增 `LoomX/Resources/Strings.en-US.resx`，覆盖 `Strings.resx` 中全部键的英文翻译，由助手基于 zh-CN 语境直接翻译并校对。
3. 新增单测断言 `LoomX/**/*.axaml` 与 `LoomX/**/*.cs`（排除 `Resources/*.resx` 与测试自身）不含 CJK 字符，强制后续变更不再回退硬编码中文。

## 范围

**包含**
- `LoomX/Resources/Strings.resx`（补充 Phase 1 遗漏的键与 Phase 2 新增键）
- `LoomX/Resources/Strings.en-US.resx`（新增）
- `LoomX/Views/ProvidersView.axaml`
- `LoomX/Views/GatewayView.axaml`
- `LoomX/Views/PlaceholderView.axaml`
- `LoomX/ViewModels/GatewayViewModel.cs`
- `LoomX/ViewModels/MainWindowViewModel.cs`
- `LoomX.Tests/`（新增中文残留扫描测试）
- `LoomX/LoomX.csproj`（如需为 resx 声明 `<EmbeddedResource>` 卫星程序集输出）

**不包含**
- 其他语言（ja-JP / ko-KR 等）
- XLIFF 工作流插件接入（`XliffTasks` NuGet，后续单独 change）
- 后端服务、CLI、数据库、日志文案
- 已迁移视图的回归（Settings / MainWindow / Overview / Activity / Console）
- UI 布局、样式、业务逻辑改动

## 非目标

- 不做完整 XLIFF 协作流程（本轮只交付 resx 双语）
- 不改动 `LocaleService` / `IStringLocalizer<T>` API
- 不引入新的文化切换持久化机制（复用 Phase 1 已有）

## 依赖

- Phase 1 已合入的 `feature/i18n-xliff` 分支（当前所在分支）
- `dotnet build` / `dotnet test` 可用

## 风险

- `MainWindowViewModel.cs` Phase 1 声称已迁移但扫描仍有 69 处中文，需先分辨是注释、字符串模板还是真硬编码 UI 文案，避免误迁移
- en-US 翻译需保持 UI 空间与语义，某些键（如 `settings.proxy.password.saved.hint`）需要按开发者工具语气处理
- 卫星程序集生成要求 `.csproj` 明确声明 `<EmbeddedResource>` + `<EmbeddedResourceType>`；Phase 1 已有配置，Phase 2 需确认 `en-US` 卫星程序集自动产出

## 验收

- `Culture=en-US` 时 UI 无中文残留、无 `[key]` 回退占位
- 单测扫描 `LoomX/**/*.axaml` + `LoomX/**/*.cs`（排除 `Resources/`）零 CJK 字符
- `dotnet build` 无警告、`dotnet test` 全绿
- `Strings.en-US.resx` 键数 ≥ `Strings.resx` 键数
- `CultureChanged` 事件触发后 UI 及时刷新（复用 Phase 1 已有断言）

```

## openspec/changes/i18n-phase-2-views-and-en-us/design.md

- Source: openspec/changes/i18n-phase-2-views-and-en-us/design.md
- Lines: 1-88
- SHA256: 082935ae4d6220299f6d1dce4b9923ed8fbd4b1318c49fe32a7285bd45cab7c4

[TRUNCATED]

```md
# 高层架构决策

本 change 复用 Phase 1 已建立的本地化基础设施，只补齐视图迁移与英文资源，不改动基线机制。

## 关键决策

### D1. 键命名沿用现有约定

沿用 `docs/i18n.md` 定义的命名空间：

| 视图 | 前缀 |
|---|---|
| Providers | `providers.*` |
| Gateway | `gateway.*` |
| Placeholder | `placeholder.*` |
| 应用级共享 | `app.*` |

新增键必须落在已有命名空间下，避免引入新的 top-level 命名空间。

### D2. AXAML 用 `{l:Locale}`，ViewModel 用 `IStringLocalizer<T>`

- 静态 UI 文案：`Text="{l:Locale providers.create.button}"`
- 动态/格式化文案：ViewModel 内 `_loc["providers.test.success"]` 或 `Loc("providers.test.failure", args)`
- 不引入 `ResourceManager` 直接调用，避免破坏响应式刷新

### D3. en-US 翻译策略

- 品牌/专名保留：`Loom-X`、`Provider`、`Gateway`、`Endpoint`、`Combo`、`Ollama`、`OpenAI`、`Anthropic`
- 语气：开发者工具的简洁正式风格（不俏皮、不过度礼貌）
- 占位符：`{0}`、`{1}` 保留原位置与命名风格
- 大小写：标题（`Title`、`Header`）用 Title Case，正文/按钮用 Sentence case

### D4. 翻译交付方式

- 由助手逐条翻译 zh-CN → en-US，直接写入 `Strings.en-US.resx`
- 本轮不接入 `XliffTasks` NuGet，`.xlf` 协作工作流放到后续 change
- 翻译完成后按 `docs/i18n.md` 命名规范校验键一致性

### D5. 中文残留扫描测试

新增 `LoomX.Tests/LocalizationNoCjkTest.cs`：
- 遍历 `LoomX/**/*.axaml`（排除 `.g.axaml` 生成物）
- 遍历 `LoomX/**/*.cs`（排除 `Resources/`、`*.Designer.cs`、`*.AssemblyInfo.cs`）
- 使用正则 `[一-鿿㐀-䶿]` 检测 CJK 字符
- 命中即失败，输出首个命中位置供定位

测试本身排除路径需在断言说明中清晰列出，防止误伤。

### D6. `.csproj` 卫星程序集

Phase 1 已配置 `<EmbeddedResource>` + `<GenerateResxSource>true</GenerateResxSource>`。Phase 2 需确认：
- `Strings.en-US.resx` 自动识别为 `NeutralResourcesLanguage` 之外的文化文件
- 构建后 `bin/Debug/<tfm>/en-US/LoomX.resources.dll` 存在

## 数据流

```
┌─────────────────────┐         ┌────────────────────┐
│  Strings.resx       │         │  Strings.en-US.resx│
│  (zh-CN, neutral)   │         │  (en-US satellite) │
└──────────┬──────────┘         └─────────┬──────────┘
           │                              │
           └──────────────┬───────────────┘
                          │ compile → satellite assembly
                          ▼
              ┌────────────────────────┐
              │ ResourceManager        │
              │ (LoomX.Strings)        │
              └────────────┬───────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
   ┌────────────────────┐    ┌────────────────────────┐
   │ LocaleService      │    │ {l:Locale} /            │
   │ (CurrentCulture)   │───▶│ IStringLocalizer<T>     │
   └─────────┬──────────┘    └───────────┬────────────┘
             │ CultureChanged             │
             ▼                            ▼
   ┌─────────────────────────────────────────┐
   │ AXAML 视图 + ViewModel 响应式刷新       │

```

Full source: openspec/changes/i18n-phase-2-views-and-en-us/design.md

## openspec/changes/i18n-phase-2-views-and-en-us/tasks.md

- Source: openspec/changes/i18n-phase-2-views-and-en-us/tasks.md
- Lines: 1-18
- SHA256: cd6d5e71ccdcab7933f446dd41479b5ce55aee13b1a88f366dea8d0b0face655

```md
## 任务

- [ ] 扫描 `LoomX/Views/*.axaml`、`LoomX/ViewModels/*.cs` 中所有硬编码中文，产出迁移清单（分文件、分命名空间）
- [ ] 补充 `LoomX/Resources/Strings.resx`：新增 `providers.*`、`gateway.*`、`placeholder.*` 键，同时补齐 MainWindowViewModel 残留键
- [ ] 迁移 `LoomX/Views/ProvidersView.axaml`：`{l:Locale}` 替换所有硬编码中文
- [ ] 迁移 `LoomX/Views/GatewayView.axaml`：`{l:Locale}` 替换所有硬编码中文
- [ ] 迁移 `LoomX/Views/PlaceholderView.axaml`：`{l:Locale}` 替换所有硬编码中文
- [ ] 迁移 `LoomX/ViewModels/GatewayViewModel.cs`：改为 `IStringLocalizer<T>`
- [ ] 迁移 `LoomX/ViewModels/MainWindowViewModel.cs`：确认 Phase 1 覆盖度，补齐漏迁的 UI 文案
- [ ] 新增 `LoomX/Resources/Strings.en-US.resx`：翻译全部键（键名与 zh-CN 完全一致）
- [ ] 校验 `.csproj` 卫星程序集配置，`dotnet build` 后确认 `en-US/LoomX.resources.dll` 存在
- [ ] 新增 `LoomX.Tests/LocalizationNoCjkTest.cs`：扫描 AXAML/CS 无 CJK 字符（排除 Resources/）
- [ ] 新增 `LoomX.Tests/LocalizationResourceParityTest.cs`：en-US 键数 ≥ zh-CN，无遗漏
- [ ] 运行 `dotnet build` 与 `dotnet test` 全绿
- [ ] 更新 `docs/i18n.md`：Phase 2 迁移范围与已支持语言列表
- [ ] 更新 `docs/i18n.md`：Phase 3 待办（XLIFF 工作流接入）

<!-- review skipped: pending build phase selection -->

```

## openspec/changes/i18n-phase-2-views-and-en-us/specs/localization/spec.md

- Source: openspec/changes/i18n-phase-2-views-and-en-us/specs/localization/spec.md
- Lines: 1-113
- SHA256: 92f4a4b158dbd5bd5046d2a4079aaea4df51d62f976129bc9fb1d115e5e8b928

[TRUNCATED]

```md
# Localization

## Purpose

Provide i18n for the Loom-X desktop UI across zh-CN (source) and en-US, with reactive culture switching, no hardcoded Chinese in UI sources, and automated coverage checks.

## Requirements

### Requirement: UI text must not be hardcoded in Chinese

All user-visible UI text (AXAML control content, ViewModel Status/toast/derived labels) must be resolved through the localization layer (`Strings.resx` via `{l:Locale}` or `IStringLocalizer<T>`). The test suite must fail when any hardcoded CJK character is found in `LoomX/Views/**/*.axaml` or in user-visible ViewModel fields.

Log messages (`logger.Log*`) and code comments are exempt — they are not user-facing.

#### Scenario: New AXAML file contains Chinese text

- **Given** an AXAML file under `LoomX/Views/` is added or modified to include a Chinese string literal
- **When** the test suite runs `LocalizationNoCjkTest`
- **Then** the test fails with the file path and line number of the first CJK hit
- **And** the developer must extract the string to `Strings.resx` and reference it via `{l:Locale}` before merging

#### Scenario: ViewModel assigns Chinese string to Status or toast

- **Given** a ViewModel under `LoomX/ViewModels/` assigns a Chinese string literal to a user-visible property (e.g. `Status = "..."`) or passes it to `toastService.Show(...)`
- **When** the test suite runs `LocalizationNoCjkTest`
- **Then** the test fails and the developer must replace the literal with `Loc("<key>")` and add the key to `Strings.resx`

#### Scenario: Log message contains Chinese

- **Given** a ViewModel calls `logger.LogInformation("概览刷新失败")`
- **When** the test suite runs `LocalizationNoCjkTest`
- **Then** the test skips this line because the file content on that line contains `logger.` (log statements are exempt per `AGENTS.md`)

### Requirement: en-US translation coverage parity

`Strings.en-US.resx` must contain at least every key defined in `Strings.resx` (zh-CN), and every en-US value must be non-empty. The test suite must fail when coverage drops.

#### Scenario: New key added to zh-CN without en-US counterpart

- **Given** a developer adds `<data name="providers.new.key"><value>新键</value></data>` to `Strings.resx`
- **And** `Strings.en-US.resx` is not updated
- **When** the test suite runs `LocalizationResourceParityTest`
- **Then** the test fails listing the missing key

#### Scenario: en-US value is empty

- **Given** `<data name="providers.x"><value></value></data>` exists in `Strings.en-US.resx`
- **When** the test suite runs `LocalizationResourceParityTest`
- **Then** the test fails listing the empty value

### Requirement: Reactive culture switching

Changing `LocaleService.CurrentCulture` must trigger `CultureChanged`, causing all reactive bindings (`{l:Locale}` via `LocaleBinding`) and derived ViewModel properties (via `OnCultureChanged` handlers) to refresh within the same UI frame without app restart.

#### Scenario: User switches language in Settings

- **Given** the app is running with `CurrentCulture = zh-CN`
- **When** the user selects `en-US` in `Settings → Language` and confirms
- **Then** within 100 ms all UI text updates to English without app restart
- **And** any subsequent UI update (page navigation, action status) uses the new language

#### Scenario: Culture changes while an operation is running

- **Given** a long-running operation (e.g. model sync) is in progress with `Status = "正在同步模型…"` (zh-CN)
- **When** the user switches to `en-US`
- **Then** the in-flight Status text stays as-is until the operation completes (non-reactive by design, aligned with Phase 1)
- **And** the operation's next Status assignment uses English

### Requirement: Culture fallback chain

Missing translations fall back in this order: current culture satellite assembly → neutral (zh-CN) assembly → key name itself. The key-name fallback ensures translators and developers can spot missing translations at runtime without a crash.

#### Scenario: Key missing from en-US satellite

- **Given** a key exists in `Strings.resx` (zh-CN) but not in `Strings.en-US.resx`
- **And** the current culture is `en-US`
- **When** the UI resolves the key via `{l:Locale}` or `Loc(key)`
- **Then** the zh-CN value is returned (satellite missing → neutral fallback)
- **And** the application does not throw


```

Full source: openspec/changes/i18n-phase-2-views-and-en-us/specs/localization/spec.md
