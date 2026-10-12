# SQLite Bridge 独立工作包验证记录

## RED（2026-10-12）

工作目录：`D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge`。

命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests" --verbosity quiet -m:1`。

结果：58 个测试实际运行，58 失败、0 通过。反射断言失败说明缺少新服务，而不是编译失败。

确认命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-build --filter "FullyQualifiedName~实现位于独立宿主程序集" --verbosity normal`。

结果：1 个行为测试失败，信息为“缺少独立 SQLite Bridge 服务；应由宿主实现公共存储契约。”

首次构建遇到其它进程锁定共享 LoomX.pdb；未终止进程、未修改其它模块，串行重试后正常执行 RED。

## 追加 RED

独立产物目录：`LoomX.Plugin.Storage.Sqlite/.artifacts`，避免与主会话共用 Tests 输出。

首轮实现：58 个用例，46 通过、12 失败；11 个 authorizer 拒绝分类错误，1 个损坏库提前在 Open 被拒绝导致测试假设错误。

补充测试后：65 个用例，63 通过、2 失败。写入中断确认 SQLite 自动回滚后 Bridge 保留幽灵事务；另一例 Windows 禁止重命名打开的目录，测试改用打开后的侧车链接。

追加真实 SQL RED：PRAGMA 批处理拒绝后仍改变 query_only，以及 64 KiB 既有库页大小导致磁盘上限计算错误，均实际运行并失败。5 个定向用例为 4 失败、1 通过；其中 Junction 路径拒绝通过，但测试临时链接清理失败。

追加 RED：既有 CredentialTokenStore 使用 busy_timeout=5000，新 Bridge 的 1000ms 上限导致真实 SQL 测试失败；调整为允许 0–5000ms。空 Text/Blob 参数测试实际通过。安全 Unicode/下划线插件 id 与整个 AppData 根校验实际运行 4 例，3 失败、1 通过后修复。

SQLitePCLRaw 2.1.13 的 prepare/authorizer/progress/interrupt/get_autocommit 签名已通过本地程序集反射核对；官方 SQLitePCL.raw raw.cs 的相关实现也已读取。未提供 Context7 MCP，本任务未写范围外的配置或索引文件。

## 阶段 GREEN（2026-10-12）

### 工作包测试

命令：

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-restore --filter "FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests" --verbosity quiet -m:1`

结果：77 通过、0 失败、0 跳过，测试耗时约 3 秒。

### 既有兼容回归

命令：

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-build --filter "FullyQualifiedName~CredentialProtectionTests|FullyQualifiedName~CredentialProtectionIdempotencyTests|FullyQualifiedName~CredentialProtectionObservabilityTests|FullyQualifiedName~PluginBridgeContractTests|FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests" --verbosity minimal`

结果：142 通过、0 失败、0 跳过，测试耗时约 4 秒。未运行全仓所有测试。

### 构建与审计

`dotnet build LoomX.Plugin.Storage.Sqlite/LoomX.Plugin.Storage.Sqlite.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-restore --verbosity quiet -m:1`：0 警告、0 错误。

`dotnet list LoomX.Plugin.Storage.Sqlite/LoomX.Plugin.Storage.Sqlite.csproj package --vulnerable --include-transitive`：当前 NuGet 源审计未发现易受攻击的包。

新项目实际依赖 Microsoft.Data.Sqlite 10.0.0 与 SQLitePCLRaw bundle/core/provider/lib 全部 2.1.13。隔离 Tests assets 同样解析到 2.1.13。宿主及旧 Credential Protection 自身仍有 2.1.11 警告，未擅自修改范围外项目。

`git diff --check -- LoomX.Tests/LoomX.Tests.csproj LoomX.slnx` 无差异空白错误；共享文件仅各增加一条 ProjectReference/Project。未提交、未推送、未修改 Comet 状态。

共享工作期间 PluginHost 新增 Roslyn 依赖曾导致隔离 assets 缺引用，重新恢复独立产物后通过；未改其它代理的实现。生成产物均在本项目 .artifacts/bin、.artifacts/obj 中，与主会话默认测试输出隔离。

## 最终独立工作包 GREEN

最后补充 Windows 设备名空格/上标数字别名测试，3 例先实际 RED（通过路径解析器反射断言，避免真的触达设备），随后修复。

完整共享重建曾因其它会话的 ProviderExecutionPipeline 缺 PluginLeaseStream 和 responsePipeline 参数失败；未修改范围外源码。该失败之后直接 no-build 会读取旧 DLL，不能算当前修复验证，已明确排除。

先独立 build 最新 SQLite 项目（0 警告、0 错误），再重新编译测试、不重建其他正在编辑的项目：

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-restore -p:BuildProjectReferences=false --filter "FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests" --verbosity quiet -m:1`

实际结果：**80 通过、0 失败、0 跳过**，约 4 秒。此结果验证最新 Bridge 和测试代码，依赖工程采用此前成功构建的独立产物；不声称整个共享工程此刻可以重建。

重新运行包含 Credential Protection/公共契约的回归：

`dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path LoomX.Plugin.Storage.Sqlite/.artifacts --no-build --filter "FullyQualifiedName~CredentialProtectionTests|FullyQualifiedName~CredentialProtectionIdempotencyTests|FullyQualifiedName~CredentialProtectionObservabilityTests|FullyQualifiedName~PluginBridgeContractTests|FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests" --logger "trx;LogFileName=sqlite-bridge-green.trx" --results-directory LoomX.Plugin.Storage.Sqlite/.artifacts/TestResults --verbosity minimal`

实际结果：**145 通过、0 失败、0 跳过**，约 5 秒。TRX 保存在本项目 .artifacts/TestResults/sqlite-bridge-green.trx。

最后 diff 检查仍仅有两个共享项目文件各一行新增；其它会话的改动完整保留。未修改公共契约、Comet 状态或 Credential Protection，未提交、未推送。

## standard 首审返修（2026-10-12）

I1/I2/M1/M2 先新增并实际运行 RED：14 例，12 失败、2 通过；修复后 GREEN：14 全通过，SQLite 全包 94 全通过，扩展 Credential Bridge/Vault 迁移及公共契约回归 186 全通过。新项目 build 0 警告、0 错误。

本轮正常重建引用工程成功，无需使用旧产物或 BuildProjectReferences=false；完整命令、源码行号及各 TRX 见 standard-review-fixes.md。未修改范围外文件、契约、Comet 状态或 checkbox，未提交/推送。
