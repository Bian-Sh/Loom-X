## Purpose

定义 LoomX Router 插件运行时与 Pipeline 编排能力，使未知第三方或第一方插件可以被发现、加载、注册 Extension 并按顺序执行，同时保证插件故障不会破坏 Router 主流程。

## ADDED Requirements

### Requirement: 插件发现与 Manifest 验证

Runtime SHALL 从约定插件目录发现插件包，并验证其 Manifest 的必备字段（id、version、extensions、capabilities）。Manifest 缺失必备字段或格式非法时，Runtime MUST 拒绝加载该插件且不影响其他插件的发现与加载。

#### Scenario: 有效插件被发现并通过验证

- **WHEN** 插件目录中存在包含合法 Manifest 的插件包
- **THEN** Runtime 发现该插件并完成 Manifest 验证，插件进入可加载状态

#### Scenario: 非法 Manifest 被拒绝且隔离

- **WHEN** 插件目录中存在缺少 id 或 extensions 字段的插件包
- **THEN** Runtime 拒绝加载该插件并记录诊断信息，其余合法插件正常加载

### Requirement: 插件动态加载与契约隔离

Runtime SHALL 通过 AssemblyLoadContext 动态加载插件 Runtime Assembly。插件与宿主之间 MUST 只通过独立的共享契约程序集交换类型，插件不得依赖宿主 UI 或 Router 内部实现类型。

#### Scenario: 插件成功加载并实例化

- **WHEN** Runtime 加载一个通过 Manifest 验证的插件
- **THEN** 插件在独立的 AssemblyLoadContext 中实例化，且仅引用共享契约程序集中的类型

### Requirement: Extension 注册与 Pipeline 归属

一个插件 SHALL 能注册多个 Extension。每个 Extension MUST 且只能归属一个指定 Pipeline，Runtime MUST 拒绝将同一 Extension 挂载到多个 Pipeline。

#### Scenario: 单插件注册多个 Extension

- **WHEN** 一个插件声明 Request Extension 与 Tool Result Extension 两个 Extension
- **THEN** 两个 Extension 分别进入各自指定的 Pipeline 并可独立启用或禁用

### Requirement: 同一 Pipeline 内 Entry 有序执行

Pipeline SHALL 将其中的 Extension Entry 作为有序集合按配置顺序执行。执行顺序 MUST 由 Router Pipeline 配置决定，不得依赖全局 Priority，也不得要求插件声明 before/after/requires 等插件间依赖关系。

#### Scenario: 多插件同 Pipeline 按序执行

- **WHEN** 同一 Pipeline 中存在来自两个插件的三个启用的 Entry
- **THEN** Pipeline 按配置中的 Entry 顺序依次执行，且任一插件无需感知其他插件的存在

### Requirement: 插件与 Entry 的启用禁用

Runtime SHALL 支持启用或禁用插件及其单个 Extension Entry。被禁用的 Entry MUST 不参与 Pipeline 执行，重新启用后按其配置顺序恢复执行。

#### Scenario: 禁用 Entry 不再执行

- **WHEN** 用户禁用某插件的一个 Extension Entry 后触发对应 Pipeline
- **THEN** 该 Entry 被跳过，其余启用的 Entry 按原顺序执行

### Requirement: 插件异常隔离与失败策略

插件执行抛出异常时，Runtime MUST 记录诊断并按所属 Extension Point 的失败策略处理，不得导致 Router 崩溃。数据安全类 Extension 失败时 MUST fail closed，不得将原始数据放行给后续组件。

#### Scenario: 普通 Extension 失败不影响主流程

- **WHEN** 一个 Observability Extension 在执行中抛出异常
- **THEN** Runtime 记录诊断并继续执行后续 Entry，主请求不受影响

#### Scenario: 数据安全 Extension 失败时安全失败

- **WHEN** 数据安全类 Extension 在执行中抛出异常
- **THEN** Runtime 阻止原始数据进入后续组件，并返回安全失败结果而非原始数据