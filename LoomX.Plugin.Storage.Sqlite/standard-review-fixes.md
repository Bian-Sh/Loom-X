# SQLite standard 首审修复交接

日期：2026-10-12。只修改独立 SQLite 工作包内文件；未提交/推送，未修改契约、Comet checkbox/状态或其它模块。

## 输入验证

已读取 reviewer 的 Temp/LoomX-Sqlite-standard-review-7jfI2f/reproduction.log，确认 INVALID_PRAGMA query_only=1、NATIVE_RETURNING finalize=5/persisted_count=1 与列/变量 InvalidSql 的独立原生复现。

本轮不依赖该报告代替自己的 RED，先新增并实际运行 14 个用例。

## 修复与源码位置

### I1：写语句 QuerySingle 必须确认完成

SqliteDatabase.cs:135–177，使用 sqlite3_stmt_readonly；写语句只缓存首行，其余 RETURNING 行继续消费至 DONE，并检查所有 step 错误。

PluginSqliteBridgeTests.cs:332–360，真实 DELETE 日志模式、外部活动 SELECT Reader 持共享读锁；原代码返回首行而不抛 Busy，修复后稳定抛 Busy。释放读锁后确认失败记录没有持久化、下一写入正常；另测多行 RETURNING 单行/多行查询后全部记录已提交且另一连接可见。

### I2：Bind 验证早于真实 PRAGMA prepare

SqliteDatabase.cs:181–205，无副作用首轮 stmt 上先 Bind，之后才真实 PRAGMA prepare；两轮使用参数快照。

PluginSqliteBridgeTests.cs:363–378，query_only/busy_timeout 各覆盖额外参数、非法值种类及 257 参数超限；错误后状态分别仍为 0/100，后续 DDL 保持可写。

### M1：确定性资源分类

SqliteDatabase.cs:208–231，仅对未被 authorizer 拒绝的 SQLITE_ERROR 做受控探测，仅改变 COLUMN/VARIABLE 两项限额且不 step。更宽 native 限额下准备成功才证明策略超限，映射 ResourceLimit；探测失败仍保留原始错误。限额 finally 恢复，无消息匹配，无全局 SQLITE_ERROR 重分类。

PluginSqliteBridgeTests.cs:382–406，真实 257 结果列、257 变量和 257 表字段均为 ResourceLimit；同码真实语法错误仍为 InvalidSql；256 边界与失败后查询可用。

### M2：确定性后置检查

PluginStoragePaths.cs:9/58 与 PluginSqliteStorageBridge.cs:42，新增每实例内部 Checkpoint，默认空、无静态共享状态、不进入公共契约。

PluginStoragePathTests.cs:127–157，在创建 cache 后、后置检查前把刚创建的空目录换为目录链接；在连接已打开、后置检查前添加 journal 链接。两例均 InvalidPath，外部文件未改变；打开失败的数据库可独占重新打开，证明连接已释放。

这些测试验证检查点，不声称消灭所有文件系统 TOCTOU，更不是 OS 沙箱。

## 实际 RED

工作目录：D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge。

首次编译运行：

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --filter "FullyQualifiedName~首审" --verbosity quiet -m:1`

结果：14 例实际运行，12 失败、2 通过、0 跳过。I1 没有预期 Busy；I2 状态实际为 query_only=1/busy_timeout=999；M1 返回 InvalidSql；M2 缺受控检查点。多行 RETURNING 正常路径和 256 边界原先通过。

修复前再次保存 RED：

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-build --filter "FullyQualifiedName~首审" --logger "trx;LogFileName=sqlite-standard-review-red.trx" --results-directory LoomX.Plugin.Storage.Sqlite/.artifacts/TestResults --verbosity minimal`

结果相同：12 失败、2 通过。

## 实际 GREEN

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-restore --filter "FullyQualifiedName~首审" --logger "trx;LogFileName=sqlite-standard-review-green.trx" --results-directory LoomX.Plugin.Storage.Sqlite/.artifacts/TestResults --verbosity minimal -m:1`

正常重建引用工程并运行：14 通过、0 失败、0 跳过，约 617ms。未使用 BuildProjectReferences=false，非旧 DLL。

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-build --filter "FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests" --logger "trx;LogFileName=sqlite-standard-review-package-green.trx" --results-directory LoomX.Plugin.Storage.Sqlite/.artifacts/TestResults --verbosity minimal`

SQLite 工作包：94 通过、0 失败、0 跳过，约 3 秒。

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-build --filter "FullyQualifiedName~CredentialProtectionTests|FullyQualifiedName~CredentialProtectionIdempotencyTests|FullyQualifiedName~CredentialProtectionObservabilityTests|FullyQualifiedName~CredentialStorageBridgeTests|FullyQualifiedName~CredentialStorageMigrationTests|FullyQualifiedName~PluginBridgeContractTests|FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests" --logger "trx;LogFileName=sqlite-standard-review-regression-green.trx" --results-directory LoomX.Plugin.Storage.Sqlite/.artifacts/TestResults --verbosity minimal`

扩展回归：186 通过、0 失败、0 跳过，约 5 秒。非全仓所有测试。

`dotnet build LoomX.Plugin.Storage.Sqlite/LoomX.Plugin.Storage.Sqlite.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-restore --verbosity quiet -m:1`

结果：0 警告、0 错误。依赖版本未改变。

TRX 均位于本项目 .artifacts/TestResults，与主会话默认 Tests 输出隔离。

## 本轮写入文件

- LoomX.Plugin.Storage.Sqlite/SqliteDatabase.cs
- LoomX.Plugin.Storage.Sqlite/PluginStoragePaths.cs
- LoomX.Plugin.Storage.Sqlite/PluginSqliteStorageBridge.cs
- LoomX.Tests/Plugins/PluginSqliteBridgeTests.cs
- LoomX.Tests/Plugins/PluginStoragePathTests.cs
- LoomX.Plugin.Storage.Sqlite/design.md
- LoomX.Plugin.Storage.Sqlite/verification.md
- LoomX.Plugin.Storage.Sqlite/standard-review-fixes.md（本记录）

没有修改 .csproj、.slnx 或其它 session 文件。
