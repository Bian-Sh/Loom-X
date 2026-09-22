---
comet_change: global-combo-endpoint-bindings
role: technical-design
canonical_spec: openspec
language: zh-CN
---

# 全局 Combo 与 Endpoint 多选绑定设计

## 1. 背景与目标

当前 Combo 存储在 Endpoint 下，导致同一个逻辑模型需要在多个 Endpoint 中重复创建。现有拓扑和网关页面也把 Endpoint、Combo 和 Provider 组织成了联动层级，用户选择左侧 Endpoint 后右侧内容随之切换，无法直接维护全局 Combo。

本变更将 Combo 定义为全局逻辑模型组：

- Combo 名称是客户端请求使用的稳定模型别名；
- Combo 成员是跨 Provider 的具体 Model，按顺序执行故障转移；
- Endpoint 只保存是否暴露某个全局 Combo 的绑定状态；
- Provider 只作为物理 Model 的连接和能力元数据；
- 一个 Endpoint 可以通过多选 Flag 暴露多个 Combo；
- 保留现有网关页面左右布局，但取消左右面板之间的选择联动。

本变更包含配置数据库、运行时配置、管理服务、桌面 ViewModel、Gateway XAML、拓扑投影和测试的协同调整。对外 HTTP 路由和客户端使用方式保持不变。

## 2. 现状与问题边界

当前配置关系如下：

```text
GatewayEndpoint
    └── GatewayCombo
            └── GatewayRoute
                    └── Model → Provider
```

`GatewayCombo` 带有 `EndpointKey`，同名 Combo 可以在不同 Endpoint 下重复存在。`GatewayViewModel` 使用 `SelectedEndpoint` 和 `SelectedCombo` 让右侧 Combo 编辑区依赖左侧选择；`RuntimeGraphProjection` 还将 Route 投影为 `Combo → Provider → Model`，放大了 Combo 指向 Provider 的误解。

本变更不解决以下问题：

- 不自动判断不同 ModelId 是否属于“同等性能”；
- 不自动将等效模型加入 Combo；
- 不改变 Provider 和 Model 管理页的职责；
- 不改变公开 Endpoint 路径、鉴权格式或响应协议；
- 不保留新旧两套运行时模型或长期双表兼容层。

## 3. 目标模型

### 3.1 数据关系

新的逻辑关系如下：

```text
GatewayEndpoint
    └──< GatewayEndpointComboBinding >── GatewayCombo
                                             └──< GatewayRoute >── Model → Provider
```

`GatewayCombo` 是全局实体，名称全局唯一。`GatewayEndpointComboBinding` 是 Endpoint 对 Combo 的多对多绑定，每个绑定独立保存启用状态和在该 Endpoint 中的显示顺序。

### 3.2 实体职责

`GatewayComboEntity`：

- `Id`：全局稳定标识；
- `Name`：对外暴露的逻辑模型名，全局不区分大小写唯一；
- `Enabled`：Combo 全局启用状态；
- `SortOrder`：全局管理页排序；
- `Routes`：跨 Provider 的有序候选 Model 列表。

`GatewayEndpointComboBindingEntity`：

- `EndpointKey`：Endpoint 标识；
- `ComboId`：全局 Combo 标识；
- `Enabled`：该 Endpoint 是否暴露该 Combo；
- `SortOrder`：该 Endpoint 返回模型目录时的顺序。

`GatewayRouteEntity`：

- 只归属于 `ComboId`；
- 指向一个具体 `ModelId`；
- 保存成员启用状态和故障转移顺序；
- 不再以 `EndpointKey` 表达路由归属。

运行时一个 Combo 是否可被某个 Endpoint 使用，必须同时满足：

```text
Endpoint.Enabled
&& EndpointComboBinding.Enabled
&& Combo.Enabled
&& 存在可用的 Route
```

Provider 和 Model 的启用状态继续在解析配置和请求尝试阶段生效，不改变现有 Provider/Model 的所有权关系。

## 4. 数据库迁移

### 4.1 迁移策略

采用一次性原地 SQLite 迁移，不建立 V2 长期双表，不在运行时同时读取旧结构和新结构。

迁移在配置数据库初始化阶段执行，必须使用事务。迁移成功后只保留新结构和新数据；迁移失败时回滚事务、记录结构化错误并阻止继续使用不完整配置。

新安装直接创建目标结构。已有数据库执行一次性表重建：

1. 创建目标 `GatewayCombos`、`GatewayEndpointComboBindings` 和 `GatewayRoutes` 临时表；
2. 读取旧 `GatewayCombos` 和带 `ComboId` 的旧 `GatewayRoutes`；
3. 按 Combo 名称不区分大小写建立全局 Combo；
4. 为旧 Combo 所属的每个 Endpoint 创建绑定；
5. 将旧 Route 合并到对应的全局 Combo；
6. 删除没有 `ComboId` 的历史裸 Route，因为它们当前本来就不会被公开；
7. 删除旧表并将目标表重命名为正式表名；
8. 创建全局名称、绑定关系和 Combo 成员的唯一约束；
9. 更新数据库 schema 标记，保证重启不会重复迁移。

迁移不得依赖当前进程内的旧 EF 导航属性完成复制，以避免新旧外键约束同时存在时产生不一致。迁移逻辑应使用明确的 SQL/数据读取步骤，并在事务内完成。

### 4.2 重名 Combo 合并规则

旧库中不同 Endpoint 下存在同名 Combo 时，合并为一个全局 Combo：

- 全局 Combo 的 `Enabled` 为旧记录中至少一个启用记录的结果；
- 每个旧记录转为一个独立 Endpoint 绑定，绑定状态保留旧 Combo 的启用状态；
- 全局 `SortOrder` 取旧记录中最小的排序值；
- 每个 Endpoint 的绑定 `SortOrder` 保留该 Endpoint 原 Combo 的排序值；
- 相同 `ModelId` 的 Route 只保留一条；
- 不同 Route 按旧 Combo 的排序顺序合并，无法比较时按 `ModelId` 稳定排序；
- 如果所有同名旧 Combo 均停用，则全局 Combo 停用，所有绑定也停用。

这样既避免同名 Combo 重复，又保留旧 Endpoint 的暴露范围。合并后用户可以在全局 Combo 面板中调整成员集合。

### 4.3 迁移完整性

迁移完成后必须验证：

- 目标表和必要列全部存在；
- 全局 Combo 名称不重复；
- 所有绑定引用存在的 Endpoint 和 Combo；
- 所有 Route 引用存在的 Combo 和 Model；
- 同一 Combo 中不存在重复 Model；
- SQLite `PRAGMA integrity_check` 返回 `ok`。

不保留旧 `EndpointKey` 的运行时字段、旧查询分支或旧表读取逻辑。旧数据库文件本身仍按项目既有迁移规则保留，但正式配置库内不保留未使用的旧网关结构。

## 5. 管理服务与运行时配置

### 5.1 管理 API

保持现有桌面内部服务边界，调整方法语义：

- `ListGatewayEndpointsAsync` 返回 Endpoint 基础设置和该 Endpoint 的 Combo 绑定摘要；
- 新增或调整 `ListGatewayCombosAsync`，返回全部全局 Combo、成员和 Endpoint 暴露摘要；
- `CreateGatewayComboAsync` 不再接收 `endpointKey`；
- `UpdateGatewayComboAsync` 只更新全局 Combo 名称、启用状态和全局排序；
- `CreateGatewayRouteAsync`、`UpdateGatewayRouteAsync`、`DeleteGatewayRouteAsync` 只以 Combo 为边界；
- 新增 Endpoint Combo 绑定的批量或单项更新操作，支持多选 Flag 的即时保存；
- Endpoint API Key 和 Reasoning effort 操作保持现有方法边界。

DTO 需要明确区分两种视图：

- Endpoint 视图使用轻量 `GatewayEndpointComboResponse`，包含 ComboId、名称、全局状态、绑定状态和 Endpoint 顺序；
- Combo 视图使用 `GatewayComboResponse`，包含成员 Route 和暴露 Endpoint 摘要；
- 不在 Endpoint 响应中嵌套完整 Route，避免左右面板独立加载时出现重复数据和循环结构。

### 5.2 运行时解析

`DatabaseConfigurationProvider` 将全局 Combo 与 Endpoint 绑定投影为：

```text
ResolvedAppConfig
  ├── Providers
  ├── Models
  ├── GatewayCombos
  └── GatewayEndpoints
          └── ComboBindings
```

网关请求仍然从当前 Endpoint 找到已启用的全局 Combo，再按 Route 顺序尝试物理 Model。客户端请求的 Combo 名称和对外 `/v1/models`、`/api/tags` 返回的模型名保持不变；上游请求继续使用实际 ModelId。

Endpoint 之间可以独立绑定同一个 Combo。一个 Endpoint 解绑 Combo 不会影响其他 Endpoint；全局停用 Combo 才会影响所有 Endpoint。

## 6. 桌面 UI 与交互

### 6.1 页面结构

保留现有网关页面左右两栏：

```text
左栏：Endpoint 编辑面板        右栏：全局 Combo 编排面板
```

两栏不再共享选中上下文：

- 左栏不通过 `SelectedEndpoint` 驱动右栏内容；
- 右栏不显示某个 Endpoint 的局部 Combo 列表；
- 点击或编辑左栏 Endpoint 只更新该卡片；
- 右栏始终展示全部全局 Combo；
- Combo 成员拖拽、展开、折叠和排序只影响 Combo 自身。

### 6.2 Endpoint 卡片

每个 Endpoint 卡片继续显示启用开关、对外 Base URL 和 API Key。新增模型暴露行：

- 左侧为多选 Flag 下拉菜单；
- 下拉内容为全部全局 Combo，每项有复选状态；
- 勾选或取消勾选立即更新该 Endpoint 的绑定；
- 已选数量和必要的已选名称在卡片内可见；
- 全局停用的 Combo 仍可显示，但标记为全局停用，不能产生有效公开模型；
- 绑定更新失败时恢复原选中状态并显示错误 Toast。

Ollama 卡片在模型暴露控件右侧显示 Reasoning effort 下拉菜单：

- 只对 `ollama` Endpoint 渲染；
- 选项来自现有 `GatewayEndpointSettings.ReasoningEfforts`；
- 始终有合法默认值；
- 保存失败恢复原值并显示错误 Toast。

其他 Endpoint 不渲染该控件，也不为其保留空的布局占位。

### 6.3 全局 Combo 面板

右栏保留现有 Combo 编辑卡片和成员列表的主要交互：

- 页面标题改为全局 Combo 管理；
- 新建 Combo 不要求选择 Endpoint；
- Combo 名称全局唯一；
- 展开后显示按故障转移顺序排列的成员 Model；
- 成员行继续显示 Model 名称和 Provider 名称；
- 保留启停、删除、拖拽排序和添加成员；
- Combo 卡片补充暴露 Endpoint 摘要，帮助用户确认影响范围；
- 模型选择器的已选状态只依据当前编辑 Combo，不依据左栏 Endpoint。

ViewModel 取消跨栏选择状态：

- `Endpoints` 和 `Combos` 是两个独立集合；
- Endpoint Combo Flag 绑定以 Endpoint Key 和 ComboId 定位；
- Route 拖拽操作显式接收 Combo，不依赖全局 `SelectedCombo`；
- Combo 模型选择器显式接收当前 Combo，不依赖当前 Endpoint。

## 7. 运行时拓扑

为了让可视化与新语义一致，拓扑投影调整为：

```text
Endpoint → Combo → Model
```

不再创建 Provider 节点和 `ProviderToModel` 结构边。Model 节点继续使用 `ProviderId + ModelId` 作为稳定身份，并携带 Provider 显示名、协议和连接摘要作为元数据。

遥测高亮直接使用：

- Endpoint 节点；
- Combo 节点；
- 实际命中的 Provider/Model 节点。

同一个 Model 被多个 Combo 或 Endpoint 复用时只保留一个物理 Model 节点。Provider 信息在 Model 详情、标签或选中信息中展示，不再作为请求路径中间层。

## 8. 错误处理与并发

- 全局 Combo 重命名冲突时拒绝保存，不自动覆盖其他 Combo；
- 删除 Combo 前检查绑定和 Route，删除操作必须级联删除绑定与成员；
- 删除 Model 前继续拒绝存在 Route 引用；
- Endpoint 多选更新使用单个管理操作或事务，避免逐项保存造成半更新；
- 配置保存成功后继续通过 `configurationProvider.ReloadAsync` 和 `AppDataStore.RefreshAsync` 更新运行时快照；
- 配置变更期间左栏和右栏都以最新快照刷新，不恢复已经删除的 Combo 或绑定；
- 迁移失败、唯一约束冲突和外键异常记录结构化错误日志，禁止把密钥或请求正文写入日志；
- 用户可见反馈使用现有 `ToastService`，详细过程仍放在页面 `Status`。

## 9. 测试计划

### 数据与迁移

- 新数据库创建全局 Combo 和 Endpoint 绑定表；
- 旧数据库同名 Combo 合并、Route 去重和绑定状态保留；
- 旧数据库裸 Route 不被公开且迁移后不残留；
- 迁移重复执行不会重复创建 Combo、Route 或绑定；
- 迁移完整性检查、全局唯一名称和外键约束通过。

### 管理服务与网关

- 一个全局 Combo 可绑定多个 Endpoint；
- Endpoint 解绑只影响自身，Combo 全局停用影响所有 Endpoint；
- `/v1/models`、`/api/tags` 和请求路由按 Endpoint 绑定筛选 Combo；
- 同一个 Combo 的 Route 在多个 Endpoint 上按同一顺序执行故障转移；
- 客户端看到 Combo 名称，日志和遥测同时记录 Combo、Provider 和实际 Model。

### 桌面 UI

- Endpoint 与 Combo 集合独立加载，不存在左侧选中驱动右侧内容的契约；
- 多选 Flag 可以添加、移除多个全局 Combo，并在失败时恢复状态；
- Ollama 显示并保存 Reasoning effort，其他 Endpoint 不显示该控件；
- 全局 Combo 的成员添加、启停、排序和删除不依赖 Endpoint；
- 已有拖拽、Toast、API Key 和 Endpoint 选择交互不回归。

### 拓扑

- 拓扑边只有 Endpoint→Combo 和 Combo→Model；
- Provider 不作为拓扑节点，但 Model 节点保留 Provider 元数据；
- 一个 Combo 绑定多个 Endpoint 时结构关系去重且遥测高亮准确；
- 实际命中的 Provider/Model 节点可以被活动请求点亮。

## 10. 非目标与收尾标准

本变更不实现自动模型能力评分、不引入模型等效关系表、不增加新的长期兼容表、不保留旧 Combo 查询分支。完成标准是：新数据库使用新关系，旧数据库一次迁移后也只使用新关系；全量测试通过；桌面发布包能够验证多个 Endpoint、多选 Combo、跨 Provider 故障转移和 Ollama Reasoning effort。
