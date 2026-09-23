# Comet Design Handoff

- Change: add-plugin-ui-contributions
- Phase: design
- Mode: compact
- Context hash: a7eed4db152d4b8d466f30853666a2e8109eaa9bab19c24df950c844631efedb

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/add-plugin-ui-contributions/proposal.md

- Source: openspec/changes/add-plugin-ui-contributions/proposal.md
- Lines: 1-32
- SHA256: 2a641945c1f92aaba90555ecb80e03c371852f5ecf9634a35fe92f84ddf073cf

```md
## Why

当前插件页面只能由 Router 预先写死卡片结构，无法让未知插件声明各自不同的观测信息，也没有为后续插件详情与设置区域建立稳定扩展边界。需要引入受宿主管控的声明式 UI Contribution 契约，使插件能够表达内容和布局，而 Router 继续统一负责加载、主题化渲染、导航、安全限制与生命周期。

## What Changes

- 为插件抽象层新增版本化的声明式 UI Contribution 契约，采用 `IPluginUiContributionProvider`、`PluginUiContribution`、`PluginUiSlot` 与受限节点树表达插件 UI。
- 扩展插件 Manifest，允许插件声明挂载到 `card-body` 或预留的 `detail-body` 的 UI Contribution；声明与运行时 Provider 不一致时只隔离对应 UI，不影响插件 Pipeline。
- 在 Plugin Runtime 中加载、校验并暴露插件 UI Contribution，禁止插件直接向宿主返回 Avalonia Control、XAML 或宿主 ViewModel。
- 在桌面插件页面加入通用 `PluginUiPresenter`，按主题资源绘制插件声明的布局；Router 不理解任何插件专用指标。
- 为声明了 `detail-body` 的插件卡片预留齿轮入口和插件列表/详情区域切换骨架；本次不定义交互式设置表单和保存协议。
- Credential Protection 作为首个使用者，自行声明三栏观测区域，展示已脱敏请求数/总请求数、累计脱敏词项、已还原回复数和异常数。
- Credential Protection 的观测数据仅在当前进程内累计，使用线程安全计数，只向 UI 暴露安全摘要，禁止包含敏感原文、请求/响应正文、凭据 token 或 Header 值。

## Capabilities

### New Capabilities

- `plugin-ui-contributions`：插件通过 Manifest 和平台无关 Provider 向卡片正文或未来详情正文贡献声明式 UI，由 Router 统一验证、渲染和管理生命周期。
- `credential-protection-observability`：Credential Protection 统计并通过自身 UI Contribution 展示当前进程内的安全运行指标。

### Modified Capabilities

- 无。

## Impact

- 影响 `LoomX.Plugin.Abstractions` 的公共插件契约与 Manifest 数据结构。
- 影响 `LoomX.PluginHost` 的 Manifest 解析、插件加载、诊断和 UI Contribution 暴露接口。
- 影响 `LoomX` 桌面端插件页面、通用声明式节点渲染、详情区域导航状态与本地化。
- 影响 `plugins/LoomX.CredentialProtection` 的请求/响应统计、脱敏词项计数和插件自有 UI 声明。
- 需要新增插件契约、Runtime、UI 渲染、线程安全、敏感信息隔离和发布包回归测试；不引入新的第三方运行时依赖。

```

## openspec/changes/add-plugin-ui-contributions/design.md

- Source: openspec/changes/add-plugin-ui-contributions/design.md
- Lines: 1-138
- SHA256: 8a60ddf01d230d7b014283cf568d03463335cdb4bebc7aff6f41f2253da3590c

[TRUNCATED]

```md
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

```

Full source: openspec/changes/add-plugin-ui-contributions/design.md

## openspec/changes/add-plugin-ui-contributions/tasks.md

- Source: openspec/changes/add-plugin-ui-contributions/tasks.md
- Lines: 1-31
- SHA256: decd5f868e2b44ecbe8ac0a332c47f87ff9ff7fafde63c23e99e1a552be7be63

```md
## 1. 插件 UI 契约与 Manifest

- [ ] 1.1 先为声明式节点、Slot、Provider 和失效事件补充抽象层单元测试，再实现版本化 `IPluginUiContributionProvider` 与节点模型，并验证 `dotnet test` 对应契约测试通过
- [ ] 1.2 先补充 Manifest 合法、重复 ID、未知 Slot 与可选 UI 的解析测试，再扩展 Manifest 模型和解析器，并验证现有无 UI 插件仍可加载

## 2. Runtime 加载、校验与隔离

- [ ] 2.1 先为 Provider 发现、Manifest/Provider 一致性和无效 UI 不影响 Pipeline 编写 Runtime 测试，再实现 Contribution 查询与安全诊断，并验证插件 Runtime 测试通过
- [ ] 2.2 实现节点树协议版本、深度、数量、文本、尺寸和图标限制，补充拒绝超限节点且诊断不回显节点文本的测试
- [ ] 2.3 实现 Runtime 对 Provider 失效事件的代理与订阅生命周期，补充只通知对应插件且事件不携带业务数据的测试

## 3. Router 通用渲染与详情外壳

- [ ] 3.1 先为各声明式节点到 Avalonia 控件的映射编写 UI 线程测试，再实现 `PluginUiPresenter` 的主题化递归渲染并验证未知或无效内容安全降级
- [ ] 3.2 重构插件卡片以通用方式渲染 CardBody，删除凭据插件专用观测字段的可能性，并用契约测试验证 Router 不包含 Credential Protection 指标名称
- [ ] 3.3 实现基于 DetailBody 声明的齿轮可见性、插件列表/详情状态、通用详情外壳与返回行为，并用测试插件验证详情失效后自动返回列表
- [ ] 3.4 实现插件 UI 失效通知的 UI 线程投递和按插件合并刷新，并验证语言切换会带新 Culture 重新请求 Contribution

## 4. Credential Protection 观测贡献

- [ ] 4.1 先为总请求、已脱敏请求、实际替换词项、已还原回复和异常计数编写失败测试，再实现线程安全的进程内观测状态并验证并发累计正确
- [ ] 4.2 扩展 CredentialEngine 返回实际替换数量，同时保持现有 `Sanitize(..., out changed)` 兼容，并用现有及新增脱敏测试验证 JSON、自由文本和重复值计数
- [ ] 4.3 将观测状态接入 Request/Response Extension，验证完整性指令注入不计为脱敏、单响应多 placeholder 只计一次还原、异常仍保持 fail-closed
- [ ] 4.4 由 Credential Protection 实现 CardBody Contribution、插件自有多语言文案、图标和三栏布局，验证主值按 `m/total` 格式显示且 Contribution 不含敏感原文或 placeholder

## 5. 集成、界面与交付验证

- [ ] 5.1 更新插件页面与插件发布契约测试，运行相关测试项目和完整解决方案构建，确认现有插件卡、Pipeline 与发布复制行为无回归
- [ ] 5.2 启动重新发布的桌面包并使用 CUA 验证浅色/当前主题下三张观测卡、刷新行为、无 DetailBody 时不显示齿轮以及通用详情测试入口布局
- [ ] 5.3 将可运行发布包输出到 `outputs/` 下带可读时间的目录，验证其中包含 Credential Protection Manifest、程序集和本地化资源
- [ ] 5.4 完成最终敏感信息审查、代码审查、OpenSpec/Comet 验证记录，并用中文提交消息提交和推送当前分支

```

## openspec/changes/add-plugin-ui-contributions/.openspec.yaml

- Source: openspec/changes/add-plugin-ui-contributions/.openspec.yaml
- Lines: 1-2
- SHA256: e002504731ceb6e8fcf026b601ec3f0568d075aa00e43292bbd0642986212f03

```md
schema: spec-driven
created: 2026-09-24

```

## openspec/changes/add-plugin-ui-contributions/specs/credential-protection-observability/spec.md

- Source: openspec/changes/add-plugin-ui-contributions/specs/credential-protection-observability/spec.md
- Lines: 1-66
- SHA256: 69ddf7de03041b297ba009cd2cd48639926ca5cc3d260574a1b9874d7f0d54af

```md
## Purpose

定义 Credential Protection 如何在不记录或泄露敏感内容的前提下统计当前进程内的脱敏、恢复和异常结果，并通过插件自有声明式 UI 展示安全摘要。

## ADDED Requirements

### Requirement: Credential Protection 统计请求脱敏结果
Credential Protection SHALL 对当前进程内进入请求 Extension 的请求总数、实际发生敏感内容替换的请求数以及实际替换的敏感词项数进行线程安全累计。只注入 placeholder 完整性指令而未替换敏感内容的请求 MUST NOT 计入已脱敏请求数。

#### Scenario: 请求包含多个敏感词项
- **WHEN** 一个请求实际替换三个敏感词项
- **THEN** 总请求数增加一
- **THEN** 已脱敏请求数增加一
- **THEN** 累计脱敏词项数增加三

#### Scenario: 普通请求原样通过
- **WHEN** 一个请求不包含需要替换的敏感内容
- **THEN** 总请求数增加一
- **THEN** 已脱敏请求数和累计脱敏词项数保持不变

#### Scenario: 请求只包含既有 placeholder
- **WHEN** 一个请求只触发 placeholder 完整性指令而没有替换新的敏感内容
- **THEN** 总请求数增加一
- **THEN** 该请求不计入已脱敏请求数

### Requirement: Credential Protection 统计恢复与异常
Credential Protection SHALL 按响应统计成功恢复至少一个本地签发 placeholder 的已还原回复数，并累计请求或响应处理过程中被捕获且转换为 fail-closed 结果的异常数。

#### Scenario: 一个响应恢复多个 placeholder
- **WHEN** 一个 Provider 响应成功恢复多个本地 placeholder
- **THEN** 已还原回复数只增加一

#### Scenario: 响应没有可恢复 placeholder
- **WHEN** 一个 Provider 响应未恢复任何本地 placeholder
- **THEN** 已还原回复数保持不变

#### Scenario: 脱敏或恢复抛出异常
- **WHEN** 请求脱敏或响应恢复过程中发生内部异常
- **THEN** 异常数增加一
- **THEN** 现有 fail-closed 行为继续阻止未安全处理的数据流动

### Requirement: Credential Protection 自行声明观测 UI
Credential Protection SHALL 通过通用插件 UI Contribution 契约声明卡片正文，不得要求 Router 根据插件 ID、指标名称或凭据语义写死界面。观测区域 SHALL 展示已脱敏请求数与总请求数的组合值、累计脱敏词项、已还原回复数和异常数。

#### Scenario: 展示请求比例和词项数
- **WHEN** 当前进程已处理一百五十个请求，其中十五个发生脱敏且累计替换二十个词项
- **THEN** 插件声明的已脱敏请求主值显示为 `15/150`
- **THEN** 辅助文字显示累计脱敏词项二十

#### Scenario: 展示恢复和异常
- **WHEN** 当前进程已还原五个响应并发生两个处理异常
- **THEN** 插件声明的已还原回复主值显示五
- **THEN** 插件声明的异常主值显示二并使用警示语义色

### Requirement: 观测数据不得包含敏感内容
Credential Protection 的观测状态、UI Contribution、失效通知和相关日志 MUST 只包含计数与安全标识，不得保存或公开敏感原文、请求正文、响应正文、用户 prompt、凭据 placeholder、Authorization 或自定义 Header 值。观测计数 SHALL 仅存在于当前进程内并在应用重启后清零。

#### Scenario: 获取观测 Contribution
- **WHEN** Router 获取 Credential Protection 的观测 Contribution
- **THEN** 返回内容只包含布局、插件自有本地化文案、图标和数字摘要
- **THEN** 返回内容不包含任何命中的敏感值或 placeholder

#### Scenario: 应用重新启动
- **WHEN** LoomX 进程结束后重新启动
- **THEN** Credential Protection 的观测计数从零开始
- **THEN** 长期 Credential Vault 映射不受观测计数清零影响

```

## openspec/changes/add-plugin-ui-contributions/specs/plugin-ui-contributions/spec.md

- Source: openspec/changes/add-plugin-ui-contributions/specs/plugin-ui-contributions/spec.md
- Lines: 1-76
- SHA256: 98dc47e470ac407135685f622ecf4e6adfafdc0e3946458c6f4db332c4b2fdb7

```md
## Purpose

定义插件如何以平台无关、受宿主管控的声明式结构向插件卡片和未来插件详情区域贡献界面内容，同时确保 Router 统一掌握主题、导航、安全限制与生命周期。

## ADDED Requirements

### Requirement: 插件声明 UI Contribution
插件 Manifest SHALL 允许声明零个或多个 UI Contribution，每个声明 MUST 包含插件内唯一 ID 和宿主支持的挂载位置。初版挂载位置 SHALL 包含插件卡片正文和插件详情正文；没有 UI 声明的插件 MUST 继续正常加载和运行。

#### Scenario: 插件只声明卡片正文
- **WHEN** 插件 Manifest 只声明卡片正文 Contribution
- **THEN** Router 在该插件卡片正文中渲染其声明内容
- **THEN** Router 不为该插件显示详情页齿轮入口

#### Scenario: 插件声明详情正文
- **WHEN** 插件 Manifest 声明详情正文 Contribution
- **THEN** Router 在插件卡片中显示通用齿轮入口
- **THEN** 点击入口后在插件页面原内容区域显示该插件的详情正文

#### Scenario: 插件没有 UI 声明
- **WHEN** 插件 Manifest 不包含 UI Contribution
- **THEN** Router 仍加载该插件的 Runtime Extension
- **THEN** 插件卡片保持通用信息展示且不出现插件专用空白区域

### Requirement: 插件通过平台无关契约提供声明式 UI
插件 SHALL 通过版本化的 UI Contribution Provider 返回声明式节点树，不得向宿主返回 Avalonia Control、XAML、宿主 ViewModel 或其他宿主私有 UI 类型。声明式协议 SHALL 支持组合布局、表面、文本、图标和分隔线，并使用语义化文字角色与颜色角色表达视觉意图。

#### Scenario: Router 渲染合法节点树
- **WHEN** 插件返回与 Manifest 声明一致且符合当前协议版本的节点树
- **THEN** Router 使用当前主题资源将节点树渲染到对应挂载位置
- **THEN** Router 无需理解节点所展示数据的业务含义

#### Scenario: 插件数据变化
- **WHEN** 插件发出不包含业务数据的 UI 失效通知
- **THEN** Router 在 UI 线程重新获取该插件的安全 Contribution 快照
- **THEN** 其他插件卡片不需要重新构建

#### Scenario: 当前语言变化
- **WHEN** 用户切换桌面端语言
- **THEN** Router 使用新的文化信息重新请求插件 Contribution
- **THEN** 插件负责返回该语言对应的自有文案

### Requirement: Router 隔离无效 UI Contribution
Router MUST 验证 Contribution ID、挂载位置、协议版本、节点类型、节点深度、节点数量、尺寸值和图标数据。单个 Contribution 无效时 MUST 隔离该 UI 并记录不含敏感内容的安全诊断，不得因此停止同一插件的 Pipeline Extension 或其他插件。

#### Scenario: Provider 与 Manifest 不一致
- **WHEN** Provider 返回未在 Manifest 声明的 Contribution ID 或错误挂载位置
- **THEN** Router 不渲染该 Contribution
- **THEN** 插件的请求或响应 Extension 仍可继续运行

#### Scenario: 节点树超过宿主限制
- **WHEN** 插件返回超过宿主深度、数量或尺寸限制的节点树
- **THEN** Router 拒绝该 Contribution 并显示安全降级状态
- **THEN** 诊断不得包含插件节点中的潜在敏感文本

#### Scenario: 未知协议版本
- **WHEN** 插件声明的 UI 协议版本高于 Router 支持版本
- **THEN** Router 不尝试猜测或部分执行未知语义
- **THEN** Router 隔离该 Contribution 并保留插件 Runtime 能力

### Requirement: Router 管理插件列表与详情区域
Router SHALL 拥有插件列表、齿轮入口、详情外壳和返回行为。插件只贡献详情正文，不得创建新的顶级窗口、侧边栏导航项或控制宿主导航状态。

#### Scenario: 打开插件详情
- **WHEN** 用户点击具有详情正文声明的插件卡片齿轮按钮
- **THEN** 插件页面切换为该插件的通用详情外壳
- **THEN** 左侧导航仍保持在插件页面
- **THEN** 详情外壳正文渲染插件的详情 Contribution

#### Scenario: 返回插件列表
- **WHEN** 用户在插件详情外壳点击返回
- **THEN** 插件页面恢复通用插件列表

#### Scenario: 详情插件失效
- **WHEN** 当前详情对应的插件在刷新后不存在或不再声明详情正文
- **THEN** Router 自动退出详情状态并返回插件列表

```
