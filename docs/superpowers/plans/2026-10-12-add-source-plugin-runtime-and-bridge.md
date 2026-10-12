---
change: add-source-plugin-runtime-and-bridge
design-doc: openspec/changes/add-source-plugin-runtime-and-bridge/design.md
base-ref: bf799431e0d2ec3c2ff1820e8b4a7b4a97ed6f7a
archived-with: 2026-10-12-add-source-plugin-runtime-and-bridge
---

# 源码插件与宿主 Bridge 实施计划

<!-- comet-task-authority: openspec/changes/add-source-plugin-runtime-and-bridge/tasks.md -->

## 执行约束

已确认 autonomous + tdd + standard。唯一需求来源为三个 delta spec；唯一正式设计为 design.md；唯一任务完成清单为 tasks.md。本计划只建立任务映射，不维护第二套勾选框。

开发前基线 1450/1450 通过。每个工作包保留实际 RED/GREEN 证据，不伪造或回退已有实现重演 RED。每个有风险工作包完成后安排独立审查，先解决重要问题再验收。Verify 负责唯一最终集成审查。

## 依赖与工作包

1. 公共契约和 Manifest：桥接 DTO 与能力协商由主会话实施；Manifest 联合模型/解析可作为不重叠工作包委派。先建立可编译的失败契约与解析测试，再写生产实现。保留旧初始化二参数构造、旧 Manifest、现有 Pipeline/UI 语义。
2. SQLite Bridge：依赖公共契约。新增单一宿主实现项目；路径、SQL prepare/authorizer、事务所有权与资源回收为高风险边界，必须真实集成测试。核对并处理基线原生依赖 NU1903，不能仅屏蔽告警。开发测试只用临时数据库。
3. 日志/通知：依赖契约；与 SQLite 可独立推进。组合根适配 ILogger 与 ToastService，安全摘要不带敏感数据。
4. 源码编译与 Runtime：依赖 Manifest 和 Bridge；固定发布引用集、缓存摘要和本地化负载、稳定安全诊断、候选加载、请求级租约。旧代码路径保留测试覆盖。
5. Credential Protection：依赖经过审查的 SQLite Bridge 与 Runtime 装配。保留同步解析与单一适配点，同名 Vault、表、索引、密文及算法不变；独立冲突事务和 fail closed，不访问用户生产 Vault。
6. 插件开发工具与 Skill：依赖生产编译/Runtime/Bridge；共享服务、工具授权、模板、隔离测试与可执行文档。工具不能用假成功代替真实行为。
7. 集成发布：所有功能通过相关审查后进行完整测试、Playground、时间目录自包含发布、真实发布包无 SDK 闭环及仅截 APP 的 CUA 验收。

## 回退与边界

小步中文提交并推送当前分支，保留其他会话产物。候选失败只释放本次资源，不替换当前实例。回退恢复宿主/插件程序集，不删除缓存、Vault 或用数据库快照覆盖新 token。同步适配不捕获 UI 上下文，旧 SSE 完成前不关闭旧 Bridge。

## 验收命令分组

- 第 1 组：dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~PluginBridgeContractTests|FullyQualifiedName~SourcePluginManifestTests|FullyQualifiedName~ManifestParserTests"
- 第 2 组：dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~PluginSqliteBridgeTests|FullyQualifiedName~PluginStoragePathTests"
- 第 3 组：dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~PluginFeedbackBridgeTests"
- 第 4 组：dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~SourcePluginCompilerTests|FullyQualifiedName~PluginReloadTests|FullyQualifiedName~SourcePluginRuntimeTests"
- 第 5 组：dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~Credential|FullyQualifiedName~PluginPublishContractTests|FullyQualifiedName~PluginPayloadLayoutTests"
- 第 6 组：dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~PluginDevelopment"
- 第 7 组：dotnet test LoomX.Tests/LoomX.Tests.csproj；dotnet run --project LoomX.PluginPlayground；scripts/publish-desktop.ps1 默认新时间目录；发布包工具闭环与 CUA。

最终命令按实际测试类调整名称，不以空匹配或间接源码断言替代行为验收。RED/GREEN 原始结果存本 change 的 .comet/evidence，精简审查/验证报告可提交，完整测试结果留本地。

## 逐项结果映射
### 任务 1.1

<!-- comet-task-ref:72235196-cb85-4513-8f30-f0e12ce1e207 -->

在 `LoomX.Plugin.Abstractions` 增加 Runtime 联合模型、`host_capabilities`、`PluginDiagnostic` 及稳定错误码，并通过契约序列化测试验证字段可确定性往返且不含宿主私有类型。

依赖：按上方工作包顺序；验收：第 1 组命令及本任务明确场景。

### 任务 1.2

<!-- comet-task-ref:a3f4b72b-c955-45b4-9d4a-7b7fc318dfd5 -->

定义 `IPluginBridge`、Storage/Logging/Notifications 接口及封闭值 DTO，增加反射契约测试，验证公共签名不出现 `object`、`Stream`、delegate、`DbConnection`、`DbTransaction`、`IServiceProvider` 或宿主程序集类型。

依赖：按上方工作包顺序；验收：第 1 组命令及本任务明确场景。

### 任务 1.3

<!-- comet-task-ref:728d1069-edb4-40b7-a62a-7ba66415a0aa -->

扩展 Manifest 解析与验证以支持 `assembly`、`csharp-source` 和旧顶层程序集字段，测试有效声明、双重 Runtime、缺失入口、空/重复 sources、非法能力版本及旧插件兼容。

依赖：按上方工作包顺序；验收：第 1 组命令及本任务明确场景。

### 任务 1.4

<!-- comet-task-ref:d8c17566-c4d5-4982-9b28-5b80b738a707 -->

扩展 `PluginInitializationContext` 以提供 Bridge 且保留现有属性和兼容构造方式，使用既有第一方插件和测试插件验证二进制入口及初始化行为未回归。

依赖：按上方工作包顺序；验收：第 1 组命令及本任务明确场景。

### 任务 1.5

<!-- comet-task-ref:3c8df5e2-e9ab-4207-9668-c442a28d7f6b -->

实现 host capability 精确协商与未声明能力拒绝代理，测试不兼容能力在执行插件代码前被拒绝、未声明调用返回 `CapabilityNotDeclared`、不同插件授权互不影响。

依赖：按上方工作包顺序；验收：第 1 组命令及本任务明确场景。

### 任务 2.1

<!-- comet-task-ref:b28eb15e-361c-45ea-80bc-8745f78f06e7 -->

新建宿主侧 `LoomX.Plugin.Storage.Sqlite` 项目并把 `Microsoft.Data.Sqlite`/`SQLitePCLRaw` 依赖集中于该项目，构建测试验证 Plugin Abstractions 和普通插件项目不传递这些依赖。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 2.2

<!-- comet-task-ref:172a5347-d48a-44a6-a8c4-acd4cb6407cd -->

实现与 plugin id 绑定的数据库路径解析和目录创建，覆盖相对嵌套路径、绝对路径、UNC、设备路径、盘符相对路径、`..`、空路径及大小写边界测试。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 2.3

<!-- comet-task-ref:40bda651-a7a0-4193-9a6b-707d23986624 -->

增加 Windows reparse point/符号链接逃逸检查并在测试临时目录验证现有父目录链接、创建前后竞态校验和跨插件路径均返回 `InvalidPath`。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 2.4

<!-- comet-task-ref:e2aca947-f284-42f6-b367-7a145da607fb -->

实现 SQLite 打开、单语句参数化 Execute、QuerySingle、Query 和 Null/Integer/Real/Text/Blob 映射，使用 CRUD、DDL、索引、Blob、空结果、重复列名和资源上限测试验证行为。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 2.5

<!-- comet-task-ref:3423f7ba-5076-4d87-87e3-3e2214f81f73 -->

实现显式事务、提交、回滚、释放回滚、完成后拒绝复用和取消传播，使用原子写入、失败回滚、并发 busy/locked 与取消测试验证生命周期。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 2.6

<!-- comet-task-ref:fb8c5646-1a63-41fd-ad62-55d3f2bba90a -->

实现 `PluginSqlException` 错误映射，逐项测试 ConstraintViolation、Busy、Locked、Corrupt、ReadOnly、PermissionDenied、InvalidPath、InvalidSql、Cancelled、ResourceLimit 和 Unknown，确认消息不含参数值或数据库绝对路径。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 2.7

<!-- comet-task-ref:cd65eb11-68d9-4122-bfac-a7ffb98f1341 -->

关闭 SQLite 扩展加载并增加 authorizer/语句限制，测试 ATTACH、外部路径型 VACUUM 和提供程序替换被拒绝，而普通 PRAGMA、DDL、DML 与查询保持可用。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 2.8

<!-- comet-task-ref:fe042dc6-143e-4987-a09c-5227006fe633 -->

把数据库句柄和事务登记到插件 Bridge 生命周期，在插件卸载时关闭资源并回滚未完成事务；通过文件可重新打开、句柄不可复用和其他插件不受影响的测试验证。

依赖：按上方工作包顺序；验收：第 2 组命令及本任务明确场景。

### 任务 3.1

<!-- comet-task-ref:2d120113-1d43-464b-8fff-24f9f8b880cc -->

实现 Logging Bridge 到 `ILogger`/Serilog 的级别、模板、字段和 PluginId 作用域映射，测试结构化字段输出以及敏感键名、凭据形态、超长字段和高频调用的拒绝或净化。

依赖：按上方工作包顺序；验收：第 3 组命令及本任务明确场景。

### 任务 3.2

<!-- comet-task-ref:0f824d00-7a70-4edd-a25b-5e7c1b524e0b -->

实现 Notifications Bridge 到现有 `ToastService` 的 Success/Info/Warning/Error 映射，测试自动归属、长度/频率限制、敏感内容拦截以及插件无法控制窗口或导航。

依赖：按上方工作包顺序；验收：第 3 组命令及本任务明确场景。

### 任务 3.3

<!-- comet-task-ref:254f980b-7683-4dc5-b0d2-039d3ce6d497 -->

在宿主组合根为每个插件创建授权后的 Bridge，并用多插件集成测试验证数据库根、日志作用域、通知来源和故障均按插件隔离。

依赖：按上方工作包顺序；验收：第 3 组命令及本任务明确场景。

### 任务 4.1

<!-- comet-task-ref:b8fb2332-bcc6-4f6c-bb6c-1b7ece04c426 -->

在 PluginHost 引入随应用发布的 Roslyn 编译能力和固定编译选项，使用没有外部 `dotnet` SDK 参与的测试进程验证最小源码插件可生成 DLL 与 portable PDB。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.2

<!-- comet-task-ref:55e9dd95-5f68-494b-8ddb-7455b9e96b91 -->

实现 Manifest 显式 sources 收集和插件包路径验证，测试未声明 `.cs` 不参与、缺失文件被诊断、目录穿越和 reparse point 逃逸在读取前被拒绝。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.3

<!-- comet-task-ref:f95170f5-995f-48bb-a3d1-701c1e993205 -->

实现受控引用集合与禁止任意 NuGet/插件目录 DLL 解析的规则，测试框架 API 与 Abstractions 可用、未允许第三方引用产生结构化编译错误且无网络恢复行为。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.4

<!-- comet-task-ref:bb87d631-934a-4a8d-af9a-e32a390d30fd -->

实现包含规范化 Manifest、源码路径及内容、Plugin API、编译器和选项的缓存键，使用单项输入变化测试验证缓存命中、失效和不同插件隔离。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.5

<!-- comet-task-ref:5bd5d58d-a15d-49c3-bbb1-2bf4de8727a6 -->

实现候选目录、产物摘要、完整性元数据和原子缓存发布，测试残缺/篡改缓存触发重编译、失败编译不覆盖最近可用缓存且临时候选可清理。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.6

<!-- comet-task-ref:0ba5923d-d44d-4722-b34a-70b3097a9f1a -->

把 Roslyn、缓存、能力、加载与初始化错误统一映射为 `PluginDiagnostic`，测试文件、行、列、错误码与阶段完整，并验证诊断不含源码正文、外部绝对路径或敏感运行数据。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.7

<!-- comet-task-ref:80c29900-6775-41c0-971e-aaa986b0effe -->

将成功编译产物接入现有 collectible ALC、Manifest 一致性、Extension/Pipeline 和 UI Contribution 流程，使用同功能的源码/预编译插件对照测试验证行为一致。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.8

<!-- comet-task-ref:1f5a8d6d-aa48-4d4f-bf1c-ef9bcb3a1948 -->

实现“先验证新实例、再原子替换”的 reload，测试成功切换、首次失败拒绝、更新失败保留当前实例和旧 ALC 回收失败仅产生安全诊断。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.9

<!-- comet-task-ref:f7d28e73-a9a8-4bd8-97f4-db9621bb7705 -->

在安装/启用源码插件入口增加可信代码边界披露及本地化文案，UI 测试验证不会把 ALC 或 Bridge 表述为安全沙箱。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.10

<!-- comet-task-ref:8fa61a79-c51c-4b2a-904e-a417c7a5a922 -->

随发布包提供固定编译引用集，并将引用和本地化负载纳入缓存输入，验证无 SDK 编译、引用缺失诊断、四语言缓存布局及文案更新生效。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 4.11

<!-- comet-task-ref:d8c634f9-404e-406f-bd8c-6a4f79d97345 -->

为 Provider 调用绑定 request/response 代际租约，覆盖新请求切换、旧请求/SSE 完成前资源不释放、启停竞争及组合根旧引用回归。

依赖：按上方工作包顺序；验收：第 4 组命令及本任务明确场景。

### 任务 5.1

<!-- comet-task-ref:220c7e73-88a7-4abf-a655-44c164eb1484 -->

为 Credential Protection Manifest 声明所需 Bridge 能力，并把 `CredentialTokenStore` 改为通过 `storage.sqlite/v1` 打开 `credential-tokens.db`；使用新建 Vault 测试验证表名、列名、索引和初始化 SQL 不变。

依赖：按上方工作包顺序；验收：第 5 组命令及本任务明确场景。

### 任务 5.2

<!-- comet-task-ref:fb7a6c1c-25d2-4edd-9b80-ba6ff74c9ac2 -->

将 token 创建、精确查询和批量恢复接入异步 Bridge，并保留同步解析链与单一同步适配点；测试同一明文稳定复用 token、跨请求恢复、重复 token 去重、取消、释放以及 UI SynchronizationContext 下无死锁。

依赖：按上方工作包顺序；验收：第 5 组命令及本任务明确场景。

### 任务 5.3

<!-- comet-task-ref:7a9cc787-0334-4a30-ad7f-55b0db5a029a -->

保留“唯一约束冲突后释放失败事务并重新精确查询”的算法，使用并发冲突测试验证不会签出第二个 token 或产生嵌套 placeholder。

依赖：按上方工作包顺序；验收：第 5 组命令及本任务明确场景。

### 任务 5.4

<!-- comet-task-ref:60e5ec3a-0df3-4b54-b76b-15ba477d2ce2 -->

使用由旧实现创建的真实 `credential-tokens.db` 作为兼容夹具，验证升级后无需迁移即可恢复历史 placeholder，DPAPI 前缀、密文和 OriginalHash 保持原样且不创建第二个 Vault。

依赖：按上方工作包顺序；验收：第 5 组命令及本任务明确场景。

### 任务 5.5

<!-- comet-task-ref:5107c058-d1e3-4d5b-9163-2a356ad46433 -->

覆盖 Bridge Busy/Locked/Corrupt、DPAPI 解密失败与未知 token 场景，验证 Credential Protection 保持原有确定性解析和 fail closed，不把明文发送给 Provider。

依赖：按上方工作包顺序；验收：第 5 组命令及本任务明确场景。

### 任务 5.6

<!-- comet-task-ref:554527df-f334-4923-ad69-45133e24c64d -->

从 Credential Protection csproj 移除 `Microsoft.Data.Sqlite`，更新宿主/测试负载复制并扩展发布契约测试，验证插件目录不存在 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*`、`e_sqlite3`，宿主侧实现仍可运行。

依赖：按上方工作包顺序；验收：第 5 组命令及本任务明确场景。

### 任务 6.1

<!-- comet-task-ref:9efd77a2-d371-419d-94b2-d92ac7aee6d9 -->

实现共享插件开发应用服务和 `loomx.plugin.create/validate/compile/test/reload/diagnostics` 工具注册，使用工具契约测试验证稳定名称、机器可读结果和统一 `PluginDiagnostic`。

依赖：按上方工作包顺序；验收：第 6 组命令及本任务明确场景。

### 任务 6.2

<!-- comet-task-ref:cbe387c5-15a1-472c-8835-1a9d43f1acd2 -->

实现 create 模板生成器，验证生成目录包含合法 Manifest、显式 sources、入口源码、测试占位和 i18n 负载，且不强制生成 `.csproj`。

依赖：按上方工作包顺序；验收：第 6 组命令及本任务明确场景。

### 任务 6.3

<!-- comet-task-ref:f1c02aae-561b-434f-ae2c-4093feef867e -->

让 validate、compile 和 diagnostics 复用生产 Manifest、能力协商、路径和 Roslyn 服务，使用多错误输入测试验证一次返回可安全收集的问题且无规则分叉。

依赖：按上方工作包顺序；验收：第 6 组命令及本任务明确场景。

### 任务 6.4

<!-- comet-task-ref:70fd1107-f8c9-48d6-bca4-aaa931855cac -->

实现隔离 test 工具和临时 Bridge，测试默认不连接真实 Provider、不读取生产 Vault、不修改 LoomX 配置，并能验证 Pipeline、声明式 UI 和 SQLite 插件测试。

依赖：按上方工作包顺序；验收：第 6 组命令及本任务明确场景。

### 任务 6.5

<!-- comet-task-ref:2d75515d-0de5-475e-a04c-485bf438e7bf -->

让 reload 工具调用生产 Runtime 的原子替换路径，使用正在运行的测试宿主验证成功生效和失败保留旧实例。

依赖：按上方工作包顺序；验收：第 6 组命令及本任务明确场景。

### 任务 6.6

<!-- comet-task-ref:70f4eb88-0074-4135-8522-eb08501e8967 -->

编写版本化插件开发 Skill，覆盖 Manifest Schema、源码模板、Pipeline、声明式 UI、SQLite/Logging/Notifications Bridge、权限边界、安全规则和 AI 修复循环；按 Skill 中步骤生成并加载示例插件验证文档可执行。

依赖：按上方工作包顺序；验收：第 6 组命令及本任务明确场景。

### 任务 7.1

<!-- comet-task-ref:44d21847-c7bb-4800-aeef-0c9bd92e1ce3 -->

扩展 `LoomX.PluginPlayground` 端到端覆盖源码编译、Bridge SQLite CRUD、日志、通知、Pipeline、reload 和 Credential Protection 历史 Vault，并验证全部检查通过。

依赖：按上方工作包顺序；验收：第 7 组命令及本任务明确场景。

### 任务 7.2

<!-- comet-task-ref:277f9bc1-0799-4bea-b20a-174aefa98e65 -->

运行完整 `dotnet test`，修复所有 Plugin Runtime、Credential Protection、UI、本地化和发布布局回归，并保存通过结果作为 Verify 输入。

依赖：按上方工作包顺序；验收：第 7 组命令及本任务明确场景。

### 任务 7.3

<!-- comet-task-ref:0019cb6e-914f-465e-8cf6-5b0152ca5bf0 -->

执行 Windows 发布与打包，检查宿主只携带一套 SQLite 实现、各插件负载目录无 SQLite 动态链、源码插件示例布局正确，并将可读时间命名的成品放入 `outputs/`。

依赖：按上方工作包顺序；验收：第 7 组命令及本任务明确场景。

### 任务 7.4

<!-- comet-task-ref:8ba86c5c-6fb5-48ae-afec-ed3561f0506c -->

在真实发布包中执行无 SDK 源码插件创建→编译→诊断修复→测试→加载→reload 流程，并验证重启后缓存命中、插件数据仍位于 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\`。

依赖：按上方工作包顺序；验收：第 7 组命令及本任务明确场景。

### 任务 7.5

<!-- comet-task-ref:4a03cea1-9a66-4c87-808e-befb55568744 -->

使用现有 Credential Protection 端到端请求/响应用例复验 placeholder 完整性指令、跨 chunk/SSE 恢复、未知或损坏 token fail closed、禁用与卸载生命周期均未改变。

依赖：按上方工作包顺序；验收：第 7 组命令及本任务明确场景。
