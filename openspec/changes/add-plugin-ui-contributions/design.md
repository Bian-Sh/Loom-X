---
comet_change: add-plugin-ui-contributions
role: technical-design
canonical_spec: openspec
---

## Context

参见 [proposal.md](./proposal.md) 的动机。当前 Plugin Runtime 只交换 `LoomX.Plugin.Abstractions` 中的平台无关类型；插件页面则由 Router 的 Avalonia XAML 固定绘制。Credential Protection 已通过独立 AssemblyLoadContext 动态加载，Router 桌面工程不静态引用其程序集。新方案必须保持这一依赖方向，并遵守现有凭据保护约束：任何观测或诊断都不得携带敏感正文、token 对应原值或认证信息。

现有插件系统设计曾讨论动态 XAML 与 SettingsProvider，但直接加载插件 Avalonia View 会引入类型身份、主题、卸载和宿主私有 API 耦合。本次采用更窄且可演进的声明式 Contribution，不把完整 Avalonia UI 权限交给插件。

## Goals / Non-Goals

**Goals:**

- 建立命名不过度承诺、又能覆盖卡片和未来详情区域的 `IPluginUiContributionProvider` 契约。
- 让 Manifest 声明静态挂载点，让插件运行时提供动态、安全、已本地化的节点快照。
- 让 Router 统一验证、主题化渲染、调度 UI 线程、处理导航并隔离坏 UI。
- 让 Credential Protection 成为首个完整使用者，验证动态计数、事件刷新和敏感信息隔离。
- 为未来插件设置页保留 `DetailBody` 与详情外壳，而不提前固化未知的表单、命令和持久化协议。

**Non-Goals:**

- 本次不允许插件提供任意 XAML、Avalonia Control、ViewModel、脚本或 Web 内容。
- 本次不定义输入框、开关、选择器、按钮命令、表单校验或设置保存协议。
- 本次不新增独立窗口、顶级导航项或插件控制的 Router 导航。
- 本次不持久化 Credential Protection 观测计数，也不改变 Credential Vault 的长期映射行为。
- 本次不实现插件卸载或 Hot Reload，只保证新 UI 契约不扩大现有生命周期耦合。

## Decisions

### 1. 使用 UI Contribution，而不是 Canvas 或 SettingsProvider 作为顶层抽象

公共接口命名为 `IPluginUiContributionProvider`，数据模型使用 `PluginUiContribution`、`PluginUiSlot` 和 `PluginUiNode`。Contribution 表示插件向宿主指定区域贡献内容，既不暗示插件拥有整个 UI，也不把能力限制为统计 Canvas 或设置表单。

替代方案：

- `IPluginCanvasProvider`：容易被理解为像素或自由绘制接口，且对未来详情/设置区域过窄。
- `IPluginSettingsProvider`：会把卡片观测能力错误限定为设置。
- `IPluginUiProvider`：范围过宽，容易让插件作者误认为可以控制宿主窗口与导航。

### 2. Manifest 声明静态贡献，Provider 返回动态快照

Manifest 新增可选 `ui.contributions`，每项只包含稳定 ID 与 Slot。运行时插件可选实现 Provider，按 `PluginUiContext` 返回当前节点快照，并通过无负载的 `UiInvalidated` 事件通知数据变化。

静态声明用于决定齿轮入口、预先验证 ID/Slot 和展示能力；动态快照用于数字、语言和状态。Provider 不在事件参数中携带节点或业务数据，避免后台线程直接把数据推入 UI。

Router 在每次读取时校验：

- Provider 返回的 ID 与 Slot 必须与 Manifest 完全匹配。
- 同一插件的 Contribution ID 唯一。
- 协议版本必须受支持。
- 节点树必须通过深度、数量、字符串长度、尺寸和图标 Geometry 限制。

UI 声明或 Provider 失败只产生安全诊断并隐藏该 Contribution，不影响 Runtime Extension。

### 3. 初版节点采用受限组合树和语义样式

`PluginUiNode` 使用封闭的抽象记录层级，初版包含：

- `PluginUiStackNode`：纵向或横向排列。
- `PluginUiGridNode`：固定列数及可选列跨度。
- `PluginUiSurfaceNode`：主题化背景、边框、圆角和内边距容器。
- `PluginUiTextNode`：文本与语义文字角色。
- `PluginUiIconNode`：受限 Geometry 字符串与语义色。
- `PluginUiDividerNode`：主题化分隔线。

样式不接受任意 Brush、字体对象或宿主资源键，只接受稳定枚举，例如 `Default`、`Accent`、`Success`、`Warning`、`Danger`，以及 `Title`、`Body`、`Metric`、`Caption`。Renderer 将语义映射到当前 Avalonia DynamicResource。

节点模型保留 `SchemaVersion`，未来增加交互式设置节点时采用新节点类型和兼容规则；本次不预埋未经验证的通用 Action 参数字典。

### 4. Plugin Runtime 代理 Provider，桌面端不直接持有插件实例

`PluginRuntime` 在加载插件后识别可选 Provider，并负责：

1. 订阅 Provider 的 `UiInvalidated`。
2. 将通知转换为带 Plugin ID 的 Runtime 事件。
3. 根据 Manifest 过滤和验证 Provider 快照。
4. 向桌面端提供按 Plugin ID 与 Slot 查询 Contribution 的方法。
5. 在未来卸载前统一解除订阅。

桌面 ViewModel 只依赖 Runtime 的查询和事件，不获取动态加载插件实例。这样可以集中诊断、限制和生命周期处理。

### 5. Router 使用通用 Presenter，并拥有详情导航外壳

新增 `PluginUiPresenter`，把通过验证的节点树递归映射为 Avalonia 控件。Presenter 只接受抽象层节点，不接受插件程序集 UI 类型。构建和销毁控件都发生在 Avalonia UI 线程。

`PluginsViewModel` 为每个插件维护 CardBody 快照，并根据 Manifest 是否存在 DetailBody 决定 `HasDetailUi`。插件事件可能来自 Provider 请求线程，因此 ViewModel 使用 Dispatcher 投递并合并同一插件尚未处理的刷新，避免高频请求产生无界 UI 工作项。

插件页面内部有两种状态：

`PluginList → PluginDetail → PluginList`

Router 渲染齿轮、返回按钮、插件名称、版本和状态等外壳。DetailBody 缺失、验证失败或刷新后插件不存在时自动退回列表。Credential Protection 本次只声明 CardBody，因此不会显示齿轮；契约与测试使用测试插件验证 DetailBody 骨架。

### 6. 插件拥有文案与显示格式

`PluginUiContext` 至少包含当前 `CultureName`。Router 在首次显示和语言变化时重新请求快照。插件自行选择本地化资源并返回最终安全文本，因此 Router 资源文件中不出现 Credential Protection 指标文案，也不根据业务字段拼接 `15/150`。

Credential Protection 负责声明卡片标题、辅助文字、图标 Geometry、语义色和格式化数字。Router 只解释节点类型和语义样式。

### 7. Credential Protection 使用共享线程安全观测状态

插件初始化时创建单个进程内观测对象，并把它共享给 Request Extension、Response Extension 和 UI Provider。计数使用 `Interlocked`，快照通过原子读取生成：

- `TotalRequests`：Request Extension 每次进入即加一。
- `SanitizedRequests`：`CredentialEngine` 实际替换至少一个敏感词项后加一。
- `SanitizedTerms`：累计实际替换次数；重复出现的敏感值按实际替换位置计数。
- `RestoredResponses`：一个响应恢复至少一个本地 placeholder 后加一，不按 placeholder 数量重复计数。
- `Errors`：Request 或 Response Extension 捕获内部异常并返回 Blocked 时加一。

`CredentialEngine` 增加返回替换数量的兼容重载，现有只返回 `changed` 的 API 继续保留并委托新实现。完整性指令注入不增加已脱敏请求或词项计数。

观测对象只存 long 计数，不接收或保存 payload、原值、token、规则命中片段或 Header。计数变化触发无负载失效事件。

### 8. 诊断必须与插件文本隔离

Manifest 结构错误可以记录插件 ID、Contribution ID、Slot 和错误类型。节点验证失败不得把节点文本、图标原始内容或序列化节点树写入日志；只记录安全位置和失败原因代码。UI 可展示宿主本地化的通用“插件内容不可用”，不得回显潜在敏感插件文本。

## Risks / Trade-offs

- **[节点协议过早固化]** → 初版只实现展示所需的组合节点和语义样式，使用版本字段；交互式设置协议等需求明确后再新增。
- **[插件事件频率过高造成 UI 队列压力]** → Provider 事件无负载，ViewModel 按插件合并待处理刷新，只在 UI 线程读取并更新快照。
- **[声明式协议表达力弱于任意 XAML]** → 接受此限制以换取主题一致性、跨平台性、验证能力和 ALC 生命周期安全；未来按真实插件需求增加节点。
- **[插件返回恶意或过大的节点树]** → Runtime 执行节点数量、深度、文本长度、数值范围和 Geometry 长度限制，失败时隔离单个 Contribution。
- **[运行时快照多个计数并非事务一致]** → 指标是近实时观测，不用于计费或审计；逐字段原子读取足够，避免引入锁竞争。
- **[当前主分支仍包含未归档的凭据保护 change]** → 本 change 以实际代码为准，不修改其他 change 的流程状态；新增规格独立描述观测能力。

## Migration Plan

1. 先扩展抽象契约和 Manifest 解析，保持 `ui` 完全可选，现有插件无需修改即可继续加载。
2. 在 Runtime 加入 Provider 代理、验证和安全诊断，并用 Fake Plugin 锁定兼容行为。
3. 加入通用 Presenter、插件卡 CardBody 和详情外壳状态；没有 Contribution 时保持现有页面外观和行为。
4. 为 Credential Protection 增加进程内计数与 CardBody 声明，并补齐插件自有本地化。
5. 运行单元、契约、Avalonia UI、构建和发布包验证后交付。

回滚时可整体移除 UI Contribution 契约和 Renderer；由于 Manifest 的 `ui` 为可选字段且观测计数不持久化，回滚不需要数据迁移，Credential Vault 不受影响。
