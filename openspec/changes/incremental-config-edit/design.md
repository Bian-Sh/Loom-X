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
