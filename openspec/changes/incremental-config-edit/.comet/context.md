# Comet Design Handoff

- Change: incremental-config-edit
- Phase: design
- Mode: compact
- Context hash: 8cbb0ca304c523696950d1112bebe88139d2c87b1d73beadbaee06e464b73918

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/incremental-config-edit/proposal.md

- Source: openspec/changes/incremental-config-edit/proposal.md
- Lines: 1-28
- SHA256: e3a318ec0c3c13e1dd6c350724c313d019dbd72c9aa2e41b600e209aae20bd2f

```md
# 配置控件增量保存与定向刷新

## 背景

Provider、Model、Settings 和 Gateway 页面中的单个 Toggle、CheckBox、ComboBox、TextBox 或数值输入保存后，会触发完整配置读取、运行时配置重载和多个页面刷新。一次模型启用切换因此产生大量无关数据库读取与日志，甚至重复应用窗口透明度。

## 目标

- 单字段编辑只修改目标记录的必要字段或关系行。
- 数据库写入成功后只更新受影响的桌面快照和运行时投影，并发布一次带实体、标识和字段的 `LocalSave` 事件。
- 各页面只响应自己关心的配置变化，保留正在编辑的控件实例、搜索和展开状态。
- Settings 外观控件继续即时预览，但其它配置变化不触发透明度应用。
- 网关运行中使用局部原子替换，不再因为单字段编辑完整 `ReloadAsync`。

## 范围

覆盖现有 Provider/Model、Settings、Gateway Endpoint/Combo/Route 的持久化单字段控件和逐字输入；覆盖 AppDataStore、ConfigSnapshotService、DatabaseConfigurationProvider 及相关 ViewModel 的事件和快照链路。

## 非目标

不修改数据库路径或 schema，不改变手动刷新和首次初始化语义，不新增通用事件总线，不重做创建/删除/同步的业务流程。

## 验收

1. 单字段保存不调用 `ReloadCoreAsync`、`LoadAsync` 或运行时完整 `ReloadAsync`。
2. SQLite 只更新目标行/列，Endpoint 绑定只更新目标关系。
3. 一次操作最多产生一次定向 `LocalSave`，非外观事件不应用透明度。
4. 快速切换、连续输入、保存失败和模型重新启用均保持最终值、编辑版本和网关路由一致。

```

## openspec/changes/incremental-config-edit/design.md

- Source: openspec/changes/incremental-config-edit/design.md
- Lines: 1-25
- SHA256: 1fb0eaef2353d0ea32d1879e8daf1faa226a7111381388d23c89db70f407e158

```md
# 高层设计

## 事件契约

沿用 `ConfigurationChanged`，为事件增加类型安全的变化字段 flags、Endpoint key 和 Route 所属 Combo 标识。兼容现有只提供实体类型和 Guid 的调用方。字段集合为空时表示兼容的实体级变化，订阅者仍按实体类型过滤。

## 写入边界

`ConfigSnapshotService` 的管理写入不再在操作前后创建并完整重载配置 Provider。`ConfigurationManagementService` 保留直接调用时的默认重载语义，桌面数据中心通过受控构造参数关闭该副作用。现有 EF 校验、规范化、密钥保护和错误语义保持不变。

## 快照与运行时

`AppDataStore` 在写入成功后对目标 response 做局部替换，保持无关列表及对象引用；`CurrentConfig` 同步替换受影响的 Provider、Model、Endpoint、Combo、Route 或 Settings 投影。运行中的 `DatabaseConfigurationProvider` 增加局部应用入口，使用锁和一次 `Volatile.Write` 原子替换配置对象，不读取整库。

## 消费者

- MainWindow 仅处理 Settings 外观字段。
- Overview 仅对影响拓扑/统计的实体变化做内存重算。
- Providers 保留编辑器实例，仅同步同一 Provider/Model 的外部变化。
- Gateway 只更新对应 Endpoint、Combo、Route 或可用模型集合，不重建无关列表。
- Settings 对本机 LocalSave 不回读；外部 Snapshot 才完整加载。

## 并发与日志

沿用现有保存锁和编辑版本。数据库成功、局部投影更新、事件发布按固定顺序执行，失败不广播成功事件。新增日志只记录实体、标识、字段、耗时和结果，不记录密钥或正文。

```

## openspec/changes/incremental-config-edit/tasks.md

- Source: openspec/changes/incremental-config-edit/tasks.md
- Lines: 1-9
- SHA256: 78dae1c91f2112b59ad70fe842397b33c2ec743b270257433d6024a3e9e44964

```md
# 实施任务

- [ ] 增补类型安全的字段级配置变更事件契约及兼容构造。
- [ ] 关闭桌面单字段管理写入前后的完整配置 Provider 重载，同时保留直接管理服务的兼容语义。
- [ ] 为 AppDataStore 增加 Settings、Provider、Model、Endpoint、Combo、Route 的局部快照更新和定向事件发布。
- [ ] 为 DatabaseConfigurationProvider 增加 Model、Provider、Endpoint、Combo、Route、Settings 的局部运行时投影替换。
- [ ] 修改 MainWindow、Overview、Providers、Gateway、Settings 的配置事件过滤和局部同步逻辑。
- [ ] 补充 SQLite 列更新、事件次数、运行时投影、透明度回调和并发保存测试。
- [ ] 完成构建、完整测试、桌面交互复验并生成带时间的发布包。

```
