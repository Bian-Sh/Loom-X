---
comet_change: i18n-phase-2-views-and-en-us
role: technical-design
canonical_spec: openspec
archived-with: 2026-09-09-i18n-phase-2-views-and-en-us
status: final
---

# i18n Phase 2 技术设计

## 目标

在 Phase 1（commit `7a66e9d`）建立的 ResX 本地化基础设施之上，完成：

1. `ProvidersView` / `GatewayView` / `PlaceholderView` / `GatewayViewModel` / `MainWindowViewModel` 中所有 UI 文案硬编码中文的迁移
2. 新增 `LoomX/Resources/Strings.en-US.resx` 全量英文翻译
3. 新增静态扫描测试强制后续变更不再回退硬编码中文
4. 新增资源键一致性测试确保 en-US 覆盖 zh-CN 全部键

不改动 Phase 1 已建立的 `LocaleService`、`ResourceLookup`、`LocaleBinding`、`LocaleExtension`、`LoomXStringLocalizerFactory`、`LocalizerFactory` API。不引入新机制。

## 架构与数据流

Phase 2 复用 Phase 1 组合，不引入新组件：

```
Strings.resx (zh-CN, neutral) ─┐
                                ├─ compile → satellite assembly → ResourceManager
Strings.en-US.resx (en-US) ─────┘
                                              │
                                ┌─────────────┴─────────────┐
                                ▼                           ▼
                     LocaleExtension {l:Locale}    IStringLocalizer<T>
                                │                           │
                                ▼                           ▼
                     LocaleBinding (响应式)        Loc(key) / LocFormat(key, args)
                                │                           │
                                └─────────────┬─────────────┘
                                              ▼
                                          UI 刷新
```

关键约束：
- `LocaleService.CurrentCulture` 是唯一文化真源
- `CultureChanged` 事件驱动 `{l:Locale}` 绑定与派生 ViewModel 属性刷新
- `ResourceManager` 自动 fallback 链：satellite → neutral → key name
- 品牌专名不翻译：`Loom-X`, `Provider`, `Gateway`, `Endpoint`, `Combo`, `Ollama`, `OpenAI`, `Anthropic`, `DPAPI`, `Base URL`

## 关键实现

### 1. `.csproj` 卫星程序集配置

在 `LoomX/LoomX.csproj` 的 `<ItemGroup>` 中新增通配符：

```xml
<EmbeddedResource Include="Resources\Strings.*.resx" />
```

`.NET SDK` 会为 `Strings.en-US.resx` 自动生成 `en-US/LoomX.resources.dll` 卫星程序集。保留现有 `Strings.resx` 的 `ManifestResourceName=LoomX.Resources.Strings` 声明不变。

### 2. ViewModel 迁移模式（与 Phase 1 完全一致）

每个受影响的 ViewModel 添加：

```csharp
private readonly IStringLocalizer<TViewModel> _loc;
private readonly IStringLocalizer<TViewModel>? _locInherited; // 仅在嵌套类需要时使用

public TViewModel(..., IStringLocalizer<TViewModel>? localizer = null)
{
    _loc = localizer ?? LocalizerFactory.Create<TViewModel>();
    LocaleService.CultureChanged += OnCultureChanged;
    // ...
}

private string Loc(string key) => _loc[key]?.Value ?? key;
private string LocFormat(string key, params object[] args) =>
    args.Length == 0 ? Loc(key) : string.Format(CultureInfo.CurrentCulture, Loc(key), args);
```

**Status 赋值模式（方案 A，非响应式）**：

```csharp
Status = Loc("providers.save.success");
Status = LocFormat("providers.save.failure", exception.Message);
toastService.Show(Loc("providers.save.failure.toast"), ToastLevel.Error);
```

**`OnCultureChanged` 只刷新派生属性**：

```csharp
private void OnCultureChanged(object? sender, CultureInfo culture)
{
    OnPropertyChanged(nameof(EnabledModelSummary)); // 派生属性
    // 不主动重设 Status —— 与 Phase 1 一致
}
```

**`Dispose` 中反注册**：

```csharp
public void Dispose()
{
    LocaleService.CultureChanged -= OnCultureChanged;
    // ...
}
```

**日志文案不迁移**：`logger.LogInformation("概览刷新失败")` 保持原样。按 `AGENTS.md` 日志规范，日志是开发/运维看的，不走 UI 本地化。

**嵌套类的 Localizer 归属**：`MainWindowViewModel.cs` 内嵌套了 `OverviewViewModel`、`ProviderEditorViewModel`、`ModelEditorViewModel`、`GatewayRouteViewModel`、`RecentRequestRowViewModel` 等多个 `private sealed class`，每个类持有自己的 `IStringLocalizer<T>`（用 `LocalizerFactory.Create<T>()` 创建）。不能共享外层 `_loc`，因为 `IStringLocalizer<T>` 的 `T` 类型参数是资源源类型标记，不影响实际资源集（所有 Localizer 都指向同一 `Strings.resx`）。

### 3. AXAML 迁移模式

**静态文本**：

```xml
<TextBlock Text="{l:Locale providers.header.list}"/>
<Button Content="{l:Locale providers.button.new}"/>
```

**带数字的字符串**（原 `StringFormat='{}{0} 个已启用'` 形式）：

不用 `StringFormat` 嵌套 `{l:Locale}` —— Avalonia 的 `StringFormat` 期望字符串字面量，`{l:Locale}` 返回的是 `Binding`，不能嵌套。改用「数字绑定 + 静态翻译文本」分离显示：

```xml
<StackPanel Orientation="Horizontal" Spacing="3">
  <TextBlock Text="{Binding EnabledProviderCount}" FontWeight="Bold"/>
  <TextBlock Text="{l:Locale providers.count.enabled}"/>
</StackPanel>
```

翻译键值：`providers.count.enabled = 个已启用` / `Enabled providers`。

**ToolTip / Watermark / AutomationProperties.Name**：

```xml
<TextBox Watermark="{l:Locale providers.search.watermark}"/>
<ToolTip.Tip="{l:Locale providers.toggle.tooltip}"/>
<AutomationProperties.Name="{l:Locale providers.toggle.automation}"/>
```

### 4. 键命名与新增键

沿用 `docs/i18n.md` 命名空间：

| 前缀 | 用途 |
|---|---|
| `providers.*` | ProvidersView 及其 ViewModel |
| `gateway.*` | GatewayView 及其 ViewModel |
| `placeholder.*` | PlaceholderView |

新增键示例：

| 键 | zh-CN | en-US |
|---|---|---|
| `providers.header.list` | 供应商列表 | Provider list |
| `providers.button.new` | 新增 Provider | New Provider |
| `providers.count.enabled` | 个已启用 | Enabled |
| `providers.save.success` | Provider 已保存 | Provider saved |
| `providers.save.failure` | 保存失败：{0} | Save failed: {0} |
| `providers.delete.success` | Provider 已删除 | Provider deleted |
| `providers.test.running` | 正在测试连接… | Testing connection… |
| `providers.test.success` | 连接正常 · {0} · {1} ms | Connected · {0} · {1} ms |
| `providers.sync.running` | 正在同步模型… | Syncing models… |
| `gateway.status.loaded` | 已加载 {0} 个 Provider、{1} 个模型、{2} 个 Endpoint 和 {3} 个 Combo | Loaded {0} providers, {1} models, {2} endpoints and {3} combos |
| `gateway.combo.added` | 全局 Combo 已添加 | Global combo added |
| `gateway.combo.saved` | Combo 模型已保存 | Combo model saved |
| `placeholder.loading` | 加载中 | Loading |

**命名规则**：
- 状态类（`Status = Loc(...)`）：`<namespace>.status.<state>` 或 `<namespace>.<action>.success|failure|running`
- Toast 类：`<namespace>.<action>.toast.success|failure|warning`
- 标题类：`<namespace>.header.<name>` 或 `<namespace>.tab.<name>`
- 按钮/链接：`<namespace>.button.<name>`
- 输入提示：`<namespace>.<control>.watermark|placeholder`
- 派生计数：`<namespace>.count.<name>`

### 5. en-US 翻译原则

- **语气**：开发者工具的简洁正式风格（不俏皮、不过度礼貌）
- **大小写**：
  - 标题（`Header`、`Title`）：Title Case（`Provider List`）
  - 按钮：Sentence case（`New Provider`）
  - 正文/状态：Sentence case（`Provider saved`）
  - 首字母大小写遵循现有 zh-CN 对应文案的语义角色
- **占位符**：`{0}` `{1}` 保留原位置与命名风格；不重编号
- **单位/标点**：英文使用半角逗号、句点、空格；数字与单位之间加空格（`15 ms` 而非 `15ms`）
- **时态**：状态类多用过去式或形容词（`Saved`、`Failed`），进行态用 `-ing`（`Loading`、`Saving`）

## 测试策略

### 1. `LocalizationNoCjkTest`（`LoomX.Tests/LocalizationNoCjkTest.cs`）

**目的**：静态扫描禁止中文残留，强制后续变更不再回退硬编码。

**规则**：
- 遍历 `LoomX/Views/**/*.axaml`：任何包含 CJK 字符 `[一-龥]` 的字符串字面量都报失败
- 遍历 `LoomX/**/*.cs`（排除 `Resources/` 目录）：
  - 跳过以 `//` 或 `/*` 开头的注释行
  - 跳过包含 `logger.` / `LogWarning` / `LogError` / `LogInformation` / `LogDebug` / `LogTrace` / `LogCritical` 的行（日志不本地化）
  - 剩余行的字符串字面量中含 CJK 字符即失败
- 失败消息包含首个命中位置与前后上下文，便于定位

**排除测试项目自身**：`LoomX.Tests/` 目录不扫描。

### 2. `LocalizationResourceParityTest`（`LoomX.Tests/LocalizationResourceParityTest.cs`）

**目的**：确保 en-US 覆盖 zh-CN 全部键，防止翻译遗漏。

**规则**：
- 用 `System.Resources.ResourceReader` 加载两个 resx
- 断言 `en-US.keys ⊇ zh-CN.keys`
- 断言每个 en-US 键的 value 非空
- 断言 en-US 无冗余键（防止翻译文件出现幽灵键）

### 3. 构建验证

`dotnet build` 后确认：
- `bin/Debug/net10.0/win-x64/en-US/LoomX.resources.dll` 存在
- 主程序集仍为 `LoomX.dll`

### 4. 全量测试

`dotnet test LoomX.Tests/LoomX.Tests.csproj` 全绿。

## 边界条件与错误处理

- **缺失 en-US 键**：`ResourceManager` 自动 fallback 到 zh-CN neutral；再缺失才 fallback 到键名本身（如 `providers.save.success`）。这是 Phase 1 已建立的机制，Phase 2 不改动。
- **`exception.Message` 参数**：`LocFormat` 使用 `string.Format(CultureInfo.CurrentCulture, ...)`，`CurrentCulture` 由 `LocaleService.SetCulture` 同步更新为 `CultureInfo.DefaultThreadCurrentUICulture`。
- **嵌套类的 Localizer**：`MainWindowViewModel.cs` 内嵌套类各自通过 `LocalizerFactory.Create<T>()` 创建 Localizer。构造参数链上不需要显式传 localizer；只有单元测试需要 mock 时才传。
- **测试环境 CultureInfo**：`LocalizationResourceParityTest` 在加载 resx 时不依赖 `CurrentCulture`，因为 `ResourceReader` 直接读文件。

## 与 Phase 1 的边界

Phase 1 完成：
- ResX/XLIFF 基础设施（`Strings.resx`、`LocaleService`、`{l:Locale}`、`IStringLocalizer<T>`）
- Settings / MainWindow 主体 / Overview / Activity / Console 视图迁移
- zh-CN 单一语言 UI

Phase 2 完成：
- Providers / Gateway / Placeholder 视图迁移
- MainWindowViewModel 中 Provider / Gateway 业务文案迁移
- en-US 卫星程序集与全量翻译
- 中文残留扫描测试与键一致性测试
- 桌面端可切换 en-US 看到全英文 UI

Phase 3 待办：
- 接入 `XliffTasks` NuGet 建立翻译协作工作流（`.xlf` 导出与编译）
- 可选：其他语言（ja-JP / ko-KR 等）

## 完成定义

- 所有 Phase 2 目标文件不含硬编码中文（除注释与日志）
- `Strings.en-US.resx` 存在且键覆盖 `Strings.resx`
- `dotnet build` 生成 `en-US/LoomX.resources.dll`
- `dotnet test` 全绿，包括新增的 `LocalizationNoCjkTest` 与 `LocalizationResourceParityTest`
- 手工走查：Settings → Language 切到 `en-US` 后 UI 无中文残留、无 `[key]` 占位回退

