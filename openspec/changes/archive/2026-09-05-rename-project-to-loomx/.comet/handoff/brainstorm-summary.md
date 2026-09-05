# Brainstorm Summary

- Change: rename-project-to-loomx
- Date: 2026-09-05

## 确认的技术方案

- 产品显示名使用 `Loom-x`；C# 根 namespace、项目、程序集和机器可读标识使用 `LoomX`/`loomx`。
- 物理结构直接去掉历史 `Desktop` 后缀：`LoomX/`、`LoomX.Tests/`、`LoomX.slnx`、`LoomX.csproj`、`LoomX.Tests.csproj`、`LoomX.exe`。
- 通过 `git mv` 完成结构改名，并同步更新 namespace、Avalonia `x:Class`/`avares://` URI、项目引用、程序集属性、静态测试路径、运行时互斥锁、环境变量和启动参数。
- 正常数据目录固定为 `%LOCALAPPDATA%\\LoomX`，配置库为 `LoomX.db`，活动库为 `LoomX.Activity.db`，日志目录为 `logs`。
- LoomX 在创建数据库连接前执行应用数据迁移。配置库和活动库分别处理：仅当目标不存在且源存在时迁移；目标已存在时新库优先。
- 迁移使用现有 `Microsoft.Data.Sqlite` 执行 SQLite `VACUUM INTO`，先写临时文件，再执行完整性/必要表检查，最后原子移动为正式文件。
- 若旧版 OllamaHub 仍占用源库、无法取得一致快照、源库损坏或任一迁移步骤失败，LoomX 记录安全摘要并阻止启动；不静默创建空库。
- 配置库和活动库独立提交。配置库成功、活动库失败时保留已验证配置库，应用仍阻止启动，下次只重试活动库；旧目录从不删除或覆盖。
- 旧配置库中的 DPAPI 密文原样保留；旧日志不迁移，旧 `%LOCALAPPDATA%\\OllamaHub` 目录作为回滚备份保留。
- HTTP 路由、请求/响应结构和 Provider/Model 配置语义保持不变；根健康响应名称为 `Loom-x`，OpenAI 模型列表 `owned_by` 为 `loomx`。

## 关键取舍与风险

- `VACUUM INTO` 不增加新依赖，能够从 SQLite 一致性快照读取 WAL 中已提交数据；代价是源库必须可读且不能被并发写入到无法取得快照。
- 不采用文件复制，避免遗漏 WAL/SHM；不采用 SQLitePCLRaw，避免引入底层句柄互操作和发布复杂度。
- 不保留旧 namespace/程序集/exe 兼容层，减少双套身份；代价是引用旧程序集的外部开发者需要改用 LoomX。
- 两个数据库不能跨文件原子提交，因此按文件独立迁移并在任一失败时阻止启动。

## 测试策略

- 更新所有项目、namespace、XAML URI、静态源码路径、程序集属性和发布契约测试。
- 测试新安装路径、配置库迁移、活动库迁移、WAL 快照、重复启动幂等、目标已存在、旧版占用、源库损坏、临时目标失败、活动库重试和旧库保留。
- 验证 DPAPI 密文不被改写，迁移日志不包含 API Key、请求正文或数据库内容。
- 运行 `dotnet test LoomX.slnx`、`dotnet build LoomX.slnx`、发布脚本和 LoomX 窗口级 CUA 验证。

## Spec Patch

- `specs/app-data-migration/spec.md` 已明确迁移机制为 SQLite `VACUUM INTO`。
- 已补充“配置库成功、活动库失败时保留配置库并仅重试活动库”的验收场景。
