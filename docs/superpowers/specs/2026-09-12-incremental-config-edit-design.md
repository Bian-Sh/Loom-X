---
comet_change: incremental-config-edit
role: technical-design
canonical_spec: openspec
language: zh-CN
status: draft
---

# LoomX 配置控件增量保存与定向通知设计

## 背景与范围

目前 `AppDataStore` 中 Settings、Provider、Gateway Endpoint/Combo/Route 的本地保存普遍调用 `ReloadCoreAsync`；`ConfigSnapshotService.ExecuteManagementAsync` 在写入前创建并重载配置 Provider，管理服务写入后又执行 `ReloadAsync`。一个 Toggle 或 TextBox 字段变化因此会读取整库配置、重建多个页面，甚至将非外观变更传给主窗口的透明度处理。

本次覆盖 Provider/Model、Settings、Gateway 页中**会持久化的单字段控件与逐字输入**，包括开关、复选框、下拉框、数值控件、文本框、Header 编辑、Combo 名称和 Endpoint 组合选择。搜索、展开、排序方向等纯 UI 状态不写库。手动刷新、首次初始化及明确的外部配置变更仍可全量加载；创建/删除、同步和拖拽排序属于独立的复合操作，后续也不能以无差别页面刷新作为单字段保存的副作用，但本次优先消除上述控件路径。

## 不变量

1. 本地单字段操作只更新目标实体的目标列；关系字段（Endpoint 绑定等）只更新受影响的关系行。校验、唯一性检查和恢复敏感配置时允许读取必要的目标行，不读取整套 Provider/Settings/Endpoint/Combo 配置。
2. 数据库成功后才更新桌面与运行时内存投影，并发布一次 `LocalSave` 事件。失败不广播成功事件，编辑控件保留/回退状态由具体操作决定，不把未保存输入伪装成已保存。
3. 事件沿用 `ConfigurationChanged`，包含实体类型、实体标识及变化字段；Endpoint 使用稳定的 `Key`，Route 事件同时标识所属 Combo。订阅者按字段和依赖关系过滤，不用另建事件总线。
4. 正在运行的网关要及时看到路由与模型变更，但只原子替换受影响的运行时配置投影，不做全库 `ReloadAsync`。未运行的网关也保持已初始化容器的内存配置一致；后续新容器启动仍从数据库初始化。
5. 外观预览只由 Settings 页的透明度相关控件即时驱动；Settings 落库事件不再重复应用同一预览。外部快照加载时才按完整设置重新应用外观。其它配置字段绝不能触发透明度处理。

## 写入与快照边界

- `ConfigSnapshotService` 提供受控的字段更新入口，复用现有 EF Core 上下文及数据库路径。按实体和字段分支执行参数化更新，保留现有字段标准化、验证、密钥保护及错误语义；不能把任意字段名拼进 SQL。对于不能从保存结果重建的敏感运行时字段，仅定向读取目标记录或复用现有已解析值，不在事件或日志中传递密钥。
- `AppDataStore` 在写入成功后对对应 `ProviderResponse`、`ModelResponse`、`AppSettingsResponse`、`GatewayEndpointResponse`、`GatewayComboResponse` 或其 Route 列表执行局部替换；其它列表和无关对象保持原引用。`EnabledGatewayModels` 只在 Provider/Model 的可用性或显示信息变化时更新。
- `CurrentConfig` 与 `IDatabaseConfigurationProvider.Current` 使用同一类局部投影规则：Provider/Model 字段影响相应模型解析；Combo/Route/Endpoint 字段影响对应路由或 Endpoint；Settings 字段只改变对应运行时设置。一次写入完成后发布事件，消费者不能观察到“数据库已变、内存仍旧”的成功通知。
- Model Enable Toggle 已有窄写入作为首个回归基线；实现时将其运行时配置同步收敛到上述局部机制，避免网关运行时仍全量 `ReloadAsync`。模型重新启用若无法仅凭现有内存恢复完整路由信息，只针对该 Model/其必要关联行读取，不回退到 `LoadAsync` 全量配置。
- 对逐字输入沿用现有串行保存锁与编辑版本：保存锁内取最新值，连续输入可合并过时的待存版本；旧响应不能覆盖新编辑。Settings 滑块与文本输入不应在每个中间值上排队执行一次全量保存。

## 事件消费者

| 事件 | 必要响应 | 不应发生 |
| --- | --- | --- |
| Settings 外观字段 | Settings 本地预览；外部快照时 MainWindow 应用外观 | Provider/Gateway/Overview 读库或重建 |
| Settings 语言字段 | 更新本地化文本及相关设置投影 | 透明度应用、Gateway 配置刷新 |
| Settings 代理/更新/日志字段 | 需要该值的功能从最新定向投影读取 | 外观应用、其它页面重建 |
| Provider/Model 字段 | Provider 编辑区保留输入；可用模型、运行路由及 Overview 的相关项按需更新 | Settings/Gateway 整页重建、全量读库 |
| Endpoint 字段或绑定 | Gateway 对应 Endpoint、运行时 Endpoint 与 Overview 对应节点更新 | Provider 列表或 Settings 回读 |
| Combo/Route 字段 | Gateway 对应 Combo/Route、运行时路由与 Overview 对应连接更新 | 无关 Combo/Endpoint 重建、透明度应用 |

Gateway 的正在编辑的 Combo 名称、推理力度与 Endpoint 绑定选项保留实例身份；成功响应只确认目标字段，不重建整个 Endpoint/Combo 列表。Overview 对真正影响拓扑/计数的事件从内存投影更新受影响节点/边，对 Settings 等无关事件直接返回。对于确需整体重算的手动刷新，继续走现有 `Snapshot` 入口。

## 错误、并发与日志

- 同一实体的快速切换/连续输入必须顺序提交最新状态；写库失败不发布 `LocalSave`，控件显示失败并可重试，后续编辑仍可继续保存。
- 跨实体操作通过数据库约束和现有锁协调；局部快照更新与事件发布有明确顺序，不能在异步运行时更新过程中泄露半成品快照。
- 每次操作只记开始/完成/失败或必要降级的结构化安全摘要，包含实体类型、标识、字段、耗时及结果；禁止记录密钥、Header 值、输入正文或工具参数。不得为每个流式字符额外记录多条重复诊断。
- 网关已经开始处理的请求使用其进入时的配置快照；后续请求使用局部替换后的快照，避免在请求中途修改共享对象。

## 验收与测试

1. 为每类持久化控件建立“控件事件 -> 写入方法 -> 局部投影 -> 定向消费者”的清单；逐一确认不存在 `ExecuteManagementAsync` 前后重载、`ReloadCoreAsync`、`LoadAsync` 或无差别 `RefreshAsync` 的单字段调用链。
2. SQLite 回归测试验证每次单字段操作只改变目标行/列；Endpoint 绑定只修改目标关系；其它 Settings、Provider、Combo 和运行时对象保持不变。
3. 日志及事件测试验证一次操作最多一次相应 `LocalSave`，不出现全库配置重载/快照读取/其它页面加载日志，非外观操作的透明度回调为零。
4. 快速反复 Toggle、连续输入、保存失败及重新启用模型场景覆盖最终值、编辑版本和运行网关的路由可见性；不能丢失密钥或 Header。
5. 完整构建和回归测试通过，并对 Provider、Settings、Gateway 主要控件做桌面交互复验；无编辑器预览的发布包按项目约定重新打包到 `outputs/`，命名包含可读时间。

## 非目标

不迁移数据库路径或 schema，不改变手动刷新语义，不新增通用事件总线、ORM 框架或日志明细。性能验收是消除无关 I/O 和刷新链，不能用简单减少日志条数掩盖仍在发生的全量读取。
