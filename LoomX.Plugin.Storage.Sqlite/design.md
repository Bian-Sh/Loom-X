# SQLite Bridge 实现边界

本文件仅记录独立工作包 2.1–2.8 的实现策略，不修改正式 change、公共契约或 Runtime 编排。

## 路径和身份

- 构造器接收包含各插件目录的数据根，直接组合绑定的安全 plugin id；不推断为整个 AppData，不自动添加其它产品目录。
- 禁止磁盘根、UNC/设备根及整个 AppData、用户目录、Windows/System 目录作为数据根。
- 相对路径拒绝根路径、盘符、URI、ADS、空段、点段、穿越和 Windows 保留设备名；插件身份不强制改名。
- 打开前、逐段创建后、连接打开后以及每次公开 SQL 操作前，检查父目录、根、数据库和 journal/wal/shm 的 reparse point。
- 这是 Bridge 保证，不是恶意同权限进程的 OS 沙箱；逐段文件系统检查与原生打开之间仍有 TOCTOU 窗口，不声称消灭同权限恶意代码的文件替换。

## SQL 和连接

- 连接 ReadWriteCreate、Private cache、禁用池化；原生扩展加载关闭。
- SQLite prepare 返回尾部后仅接受空白/注释；不是用分号正则判断 SQL。
- authorizer 拒绝 ATTACH/DETACH、扩展/文件函数、虚拟表、路径型/未授权 PRAGMA 及插件提交的裸事务。内部事务控制单独授权。
- 第一轮 prepare 通过 authorizer IGNORE 抑制 PRAGMA 的准备期副作用；在该无副作用 stmt 上确认单语句尾部并完成参数数量、名称、类型和资源 Bind 校验，才进行真实 prepare。两轮使用相同参数快照。触发器内部多个语句仍是合法单条 CREATE TRIGGER。
- 支持 WAL、synchronous、busy_timeout、user_version 和列出的数据库内 PRAGMA。busy_timeout 允许 0–5000ms，兼容既有 Vault；原生 busy 等待期间取消可能要等待当前有界 timeout。

## 资源、事务与释放

- 每个存储实例最多 64 个活动数据库句柄；SQL UTF-8 最多 256 KiB，最多 256 列/参数。
- 单值最多 1 MiB，输入参数/缓冲结果总值字节最多 8 MiB，缓冲结果最多 10000 行。原生 LENGTH 上限还会限制一条记录的大小。
- 主库最多 256 MiB，以既有数据库真实 page_size 计算 max_page_count；该数值不声称包含 SQLite 所有临时/侧车文件总量。
- 数据库/事务共享串行门；活动事务期间非事务调用稳定拒绝。取消通过门等待、progress 与 interrupt 传播。同步消费异步接口不会捕获 UI SynchronizationContext。
- SQLite 自动回滚后根据 get_autocommit 同步清除事务；提交/回滚/释放后的句柄不可复用。
- 事务释放默认回滚；Storage 释放等待在途操作并回滚、关闭登记的全部连接。不把普通禁用自动理解为 Runtime 退休。
- 日志只含绑定 plugin id、操作类别、稳定错误与数字码；不含 SQL、参数、绝对路径或提供程序异常。

## 接线和验收边界

Runtime 在插件实际退休时调用此服务的 IAsyncDisposable，具体组合根接线由主会话负责。未修改 Credential Protection 的 DPAPI、token、生命周期或既有数据库访问代码。

Constraint/Busy/Corrupt/ReadOnly/Permission/InvalidSql/Cancelled/ResourceLimit/Disposed 通过真实数据库行为覆盖；难以通过本 Bridge 的串行私有连接稳定制造的 Locked/Unknown 等码通过安全错误映射测试覆盖，不伪称真实设备故障测试。

## standard 首审修复

- QuerySingle 用原生 sqlite3_stmt_readonly 区分只读与写入：只读可返回首行；写入只缓存首行但消费至 SQLITE_DONE，并传播之后的 Busy/提交错误，不依赖忽略返回码的 finalize 完成写入。
- 列/变量策略超限若返回 SQLITE_ERROR，只放宽这两个收紧的限额做无副作用 prepare 探测；探测成功证明策略超限并映射 ResourceLimit，所有限额在 finally 恢复。探测失败保持原错误分类，不匹配消息、不把所有 SQLITE_ERROR 当 ResourceLimit。若连 SQLite 编译硬上限下也不能准备成功，不猜测其原因。
- 路径有每实例内部 Checkpoint，生产默认空；只供确定性测试在目录创建/连接打开和后置检查之间置换，不新增公共 Bridge API，不写路径日志。创建后父目录链接拒绝，打开后侧车链接拒绝并释放连接；依然承认其它未覆盖的 TOCTOU 窗口，不声称 OS 沙箱。
