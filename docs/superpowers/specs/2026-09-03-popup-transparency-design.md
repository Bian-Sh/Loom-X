# 弹窗透明外观统一设计

## 背景

桌面端主窗口已经支持透明开关、透明程度、磨砂程度和 Acrylic/Blur/Mica 材质选择，但弹窗没有复用完整的窗口级外观配置：Provider 删除确认使用 Avalonia 默认标题栏和默认窗口背景，网关模型选择 Popup 只引用普通玻璃画刷。两者因此与主窗口的玻璃层级、背景不透明度和边框表现不一致。

本次以用户提供的两张界面截图为视觉问题参考，不把截图中的文字或布局当作额外需求。

## 目标

- Provider 删除确认窗口和模型选择 Popup 使用与主窗口相同的透明设置。
- 透明设置运行时变化时，已打开的独立确认窗口即时刷新；Popup 通过动态资源即时刷新。
- 透明关闭时所有弹窗表面使用完全不透明的回退背景。
- 保留现有模态返回值、按钮命令、Popup 定位、轻点关闭、搜索、排序、分组折叠和键盘焦点行为。
- 为后续新增独立弹窗提供单一的外观接入点。

## 非目标

- 不改变设置数据库字段、配置保存协议或设置页控件范围。
- 不改变 Provider、Gateway 的业务命令、数据绑定和数据流。
- 不重做主窗口或六个业务页面的整体视觉令牌。
- 不把所有弹窗改成主窗口内嵌遮罩，不改变窗口层级和模态机制。

## 方案

采用轻量的 `WindowAppearanceCoordinator` 作为外观协调器。`MainWindow` 持有协调器，继续保留现有 `ApplyAppearance(bool, int, int, string)` 作为设置页的唯一入口。协调器保存经过钳制和规范化的当前快照，更新共享动态画刷，应用主窗口窗口级透明材质，并通过 `ApplyTo(Window)` 和 `AppearanceChanged` 支持独立窗口。

模型选择 Popup 仍然是当前窗口内的 Popup，不创建新的 Window；它使用专用 Popup 表面令牌，因此共享画刷变化会自动反映到已打开的 Popup。Provider 删除确认继续使用 `ShowDialog<bool>`，但改为可复用的无系统装饰玻璃对话框窗口，协调器负责配置窗口级透明属性和回退背景。

## 组件设计

### WindowAppearanceCoordinator

协调器位于 `OllamaHub.Desktop/Services`，包含以下职责：

- 保存 `TransparencyEnabled`、`TransparencyOpacity`、`BlurAmount`、`TransparencyAlgorithm` 的当前快照。
- 以 `MainWindow` 现有的固定基线颜色、透明度因子和磨砂色调计算方式更新全局 `SolidColorBrush`，不基于上一次修改后的 Alpha 累积计算。
- 根据算法生成材质优先级：Mica、AcrylicBlur、Blur、Transparent；Blur 和 Acrylic 按现有回退顺序处理。
- 将 `TransparencyBackgroundFallback`、`Background` 和 `TransparencyLevelHint` 应用到主窗口或独立弹窗。
- 透明开启时使用 `Transparent` 窗口背景并保留系统材质回退；透明关闭时使用颜色相同但 Alpha 为 255 的窗口背景副本。
- 发布 `AppearanceChanged` 事件。事件只携带安全的外观快照，不携带配置凭据或业务内容。

现有 `MainWindow.BuildTransparencyLevels`、`CalculateOpacityFactor`、`CalculateBlurTintFactor`、`CalculateBrushAlpha` 和 `AppearanceBrushUpdater` 的行为保持兼容，必要时由协调器调用或由 `MainWindow` 保留转发，以避免现有测试和调用方回退。

### GlassDialogWindow

新增可复用的玻璃对话框窗口外壳，使用 `SystemDecorations=None`、扩展客户区和 32px 自定义标题栏。外壳提供标题文本、拖动区域、关闭按钮和内容承载区，关闭按钮返回 `false`。Provider 删除确认只负责填充正文和按钮，不再手写窗口级透明属性。

对话框外壳的背景、边框、按钮和文字全部使用全局动态资源；关闭按钮沿用主窗口的语义红色令牌。保留 `WindowStartupLocation.CenterOwner`、不可调整大小和 `ShowDialog<bool>`。

### Popup 表面

在 `Styles/VisualTokens.axaml` 新增：

- `DialogBackgroundBrush`：删除确认窗口内容表面。
- `PopupBackgroundBrush`：模型选择 Popup 表面。

两者的 RGB 基线与现有浅色玻璃层一致，Alpha 由协调器根据透明程度和磨砂程度计算。透明关闭时，协调器为这两个令牌应用 Alpha 255 的不透明颜色；透明开启时按当前设置变化。Popup 继续使用现有宽度、最大高度、定位偏移和内容结构，只调整表面、边框、悬停和焦点状态。

## 运行时数据流

```text
设置页 Toggle/Slider/ComboBox
        |
        v
SettingsViewModel.ApplyAppearancePreview
        |
        v
MainWindow.ApplyAppearance
        |
        v
WindowAppearanceCoordinator
   |                 |
   v                 v
主窗口窗口属性      全局动态画刷 + AppearanceChanged
                           |
                 +---------+---------+
                 |                   |
                 v                   v
          已打开 GlassDialog     已打开 Popup
```

Provider 删除确认窗口打开后订阅协调器事件，在关闭或 `Closed` 时解除订阅，避免窗口被回收后继续持有引用。Popup 不需要单独订阅，因为其 `DynamicResource` 直接读取被协调器更新的共享画刷。

## 视觉规则

- 主窗口、独立对话框和 Popup 使用同一套材质优先级和边框色；对话框与 Popup 通过专用表面令牌保持层级，不改变主窗口页面表面的 Alpha。
- 删除确认正文使用紧凑的 24px 外边距、稳定的按钮行和可换行文本；标题栏与正文之间使用细分隔线，避免默认系统标题栏的白色实心条。
- Popup 保持当前 360px 宽度和 420px 最大高度，统一 8px 内边距、10px 圆角和细边框；分组标题、模型条目沿用现有青灰选中态和成功色勾选标记。
- 所有新增关闭、确认和取消动作保留 `AutomationProperties.Name`；图标继续使用 `PathIcon` 或现有几何路径，不新增 Unicode 图标。

## 错误与兼容性

- 外观参数在协调器入口继续钳制到透明度 `0-100`、磨砂程度 `0-64`，算法继续按 `acrylic`、`blur`、`mica` 规范化并提供 Acrylic 回退。
- 资源缺失时使用透明或固定冷灰不透明回退，不抛出窗口初始化异常。
- 平台不支持所选材质时，Avalonia 按材质优先级使用可用级别，并由 `TransparencyBackgroundFallback` 保证窗口内容可见。
- 对话框关闭、取消和窗口销毁都解除事件订阅；删除操作仍只在 `ShowDialog<bool>` 返回 `true` 时执行。

## 验证

### 自动化验证

- 单元测试验证协调器保存快照、透明开关、不透明回退和材质优先级。
- 单元测试验证透明度和磨砂程度变化始终基于固定基线，连续应用不会累积误差。
- 契约测试验证 `GlassDialogWindow` 使用无系统装饰、自定义标题栏、动态背景资源和可访问关闭按钮。
- 契约测试验证 Gateway Popup 使用 `PopupBackgroundBrush`，保留搜索、排序、轻点关闭和原有尺寸约束。
- 运行完整 `dotnet test OllamaHub.slnx`，再运行 `dotnet build OllamaHub.slnx`。

### 手动验证

启动桌面端后依次检查：

1. 打开 Provider 删除确认，确认标题栏、背景和边框与主窗口玻璃层级一致，关闭按钮和拖动区域可用。
2. 打开 Gateway 模型选择 Popup，确认搜索、排序、分组折叠、勾选和轻点关闭行为不变。
3. 在设置页切换透明开关、透明程度、磨砂程度和三种算法，确认主窗口、已打开确认窗口和 Popup 同步刷新；关闭透明后两个弹窗均不透出底层内容。
4. 将窗口缩放到最小尺寸，确认标题栏、按钮、确认文本和 Popup 没有裁切或重叠。

验证完成后重新发布桌面包到 `outputs`，使用可读时间命名，不处理或删除已有未跟踪目录。

## 变更清单

- 新增 `OllamaHub.Desktop/Services/WindowAppearanceCoordinator.cs`。
- 新增玻璃对话框窗口外壳及其必要的视图代码文件。
- 修改 `OllamaHub.Desktop/MainWindow.axaml` 与 `MainWindow.axaml.cs` 的外观入口和共享样式。
- 修改 `OllamaHub.Desktop/Styles/VisualTokens.axaml` 的弹窗令牌。
- 修改 `OllamaHub.Desktop/Views/ProvidersView.axaml.cs` 接入玻璃确认窗口。
- 修改 `OllamaHub.Desktop/Views/GatewayView.axaml` 的 Popup 表面样式。
- 新增或扩展桌面端外观与 Popup 契约测试。
