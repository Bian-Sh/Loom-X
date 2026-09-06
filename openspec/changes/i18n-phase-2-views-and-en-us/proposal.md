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
