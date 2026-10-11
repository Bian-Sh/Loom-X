# Tasks

## 1. 公共契约与 Manifest

- [x] 1.1 在 `LoomX.Plugin.Abstractions` 增加 Runtime 联合模型、`host_capabilities`、`PluginDiagnostic` 及稳定错误码，并通过契约序列化测试验证字段可确定性往返且不含宿主私有类型。 <!-- comet-task:72235196-cb85-4513-8f30-f0e12ce1e207 -->
- [x] 1.2 定义 `IPluginBridge`、Storage/Logging/Notifications 接口及封闭值 DTO，增加反射契约测试，验证公共签名不出现 `object`、`Stream`、delegate、`DbConnection`、`DbTransaction`、`IServiceProvider` 或宿主程序集类型。 <!-- comet-task:a3f4b72b-c955-45b4-9d4a-7b7fc318dfd5 -->
- [x] 1.3 扩展 Manifest 解析与验证以支持 `assembly`、`csharp-source` 和旧顶层程序集字段，测试有效声明、双重 Runtime、缺失入口、空/重复 sources、非法能力版本及旧插件兼容。 <!-- comet-task:728d1069-edb4-40b7-a62a-7ba66415a0aa -->
- [ ] 1.4 扩展 `PluginInitializationContext` 以提供 Bridge 且保留现有属性和兼容构造方式，使用既有第一方插件和测试插件验证二进制入口及初始化行为未回归。 <!-- comet-task:d8c17566-c4d5-4982-9b28-5b80b738a707 -->
- [ ] 1.5 实现 host capability 精确协商与未声明能力拒绝代理，测试不兼容能力在执行插件代码前被拒绝、未声明调用返回 `CapabilityNotDeclared`、不同插件授权互不影响。 <!-- comet-task:3c8df5e2-e9ab-4207-9668-c442a28d7f6b -->

## 2. 宿主 SQLite Bridge

- [x] 2.1 新建宿主侧 `LoomX.Plugin.Storage.Sqlite` 项目并把 `Microsoft.Data.Sqlite`/`SQLitePCLRaw` 依赖集中于该项目，构建测试验证 Plugin Abstractions 和普通插件项目不传递这些依赖。 <!-- comet-task:b28eb15e-361c-45ea-80bc-8745f78f06e7 -->
- [x] 2.2 实现与 plugin id 绑定的数据库路径解析和目录创建，覆盖相对嵌套路径、绝对路径、UNC、设备路径、盘符相对路径、`..`、空路径及大小写边界测试。 <!-- comet-task:172a5347-d48a-44a6-a8c4-acd4cb6407cd -->
- [x] 2.3 增加 Windows reparse point/符号链接逃逸检查并在测试临时目录验证现有父目录链接、创建前后竞态校验和跨插件路径均返回 `InvalidPath`。 <!-- comet-task:40bda651-a7a0-4193-9a6b-707d23986624 -->
- [x] 2.4 实现 SQLite 打开、单语句参数化 Execute、QuerySingle、Query 和 Null/Integer/Real/Text/Blob 映射，使用 CRUD、DDL、索引、Blob、空结果、重复列名和资源上限测试验证行为。 <!-- comet-task:e2aca947-f284-42f6-b367-7a145da607fb -->
- [x] 2.5 实现显式事务、提交、回滚、释放回滚、完成后拒绝复用和取消传播，使用原子写入、失败回滚、并发 busy/locked 与取消测试验证生命周期。 <!-- comet-task:3423f7ba-5076-4d87-87e3-3e2214f81f73 -->
- [x] 2.6 实现 `PluginSqlException` 错误映射，逐项测试 ConstraintViolation、Busy、Locked、Corrupt、ReadOnly、PermissionDenied、InvalidPath、InvalidSql、Cancelled、ResourceLimit 和 Unknown，确认消息不含参数值或数据库绝对路径。 <!-- comet-task:fb8c5646-1a63-41fd-ad62-55d3f2bba90a -->
- [ ] 2.7 关闭 SQLite 扩展加载并增加 authorizer/语句限制，测试 ATTACH、外部路径型 VACUUM 和提供程序替换被拒绝，而普通 PRAGMA、DDL、DML 与查询保持可用。 <!-- comet-task:cd65eb11-68d9-4122-bfac-a7ffb98f1341 -->
- [ ] 2.8 把数据库句柄和事务登记到插件 Bridge 生命周期，在插件卸载时关闭资源并回滚未完成事务；通过文件可重新打开、句柄不可复用和其他插件不受影响的测试验证。 <!-- comet-task:fe042dc6-143e-4987-a09c-5227006fe633 -->

## 3. Logging 与 Notifications Bridge

- [ ] 3.1 实现 Logging Bridge 到 `ILogger`/Serilog 的级别、模板、字段和 PluginId 作用域映射，测试结构化字段输出以及敏感键名、凭据形态、超长字段和高频调用的拒绝或净化。 <!-- comet-task:2d120113-1d43-464b-8fff-24f9f8b880cc -->
- [ ] 3.2 实现 Notifications Bridge 到现有 `ToastService` 的 Success/Info/Warning/Error 映射，测试自动归属、长度/频率限制、敏感内容拦截以及插件无法控制窗口或导航。 <!-- comet-task:0f824d00-7a70-4edd-a25b-5e7c1b524e0b -->
- [ ] 3.3 在宿主组合根为每个插件创建授权后的 Bridge，并用多插件集成测试验证数据库根、日志作用域、通知来源和故障均按插件隔离。 <!-- comet-task:254f980b-7683-4dc5-b0d2-039d3ce6d497 -->

## 4. C# 源码插件运行时

- [ ] 4.1 在 PluginHost 引入随应用发布的 Roslyn 编译能力和固定编译选项，使用没有外部 `dotnet` SDK 参与的测试进程验证最小源码插件可生成 DLL 与 portable PDB。 <!-- comet-task:b8fb2332-bcc6-4f6c-bb6c-1b7ece04c426 -->
- [ ] 4.2 实现 Manifest 显式 sources 收集和插件包路径验证，测试未声明 `.cs` 不参与、缺失文件被诊断、目录穿越和 reparse point 逃逸在读取前被拒绝。 <!-- comet-task:55e9dd95-5f68-494b-8ddb-7455b9e96b91 -->
- [ ] 4.3 实现受控引用集合与禁止任意 NuGet/插件目录 DLL 解析的规则，测试框架 API 与 Abstractions 可用、未允许第三方引用产生结构化编译错误且无网络恢复行为。 <!-- comet-task:f95170f5-995f-48bb-a3d1-701c1e993205 -->
- [ ] 4.4 实现包含规范化 Manifest、源码路径及内容、Plugin API、编译器和选项的缓存键，使用单项输入变化测试验证缓存命中、失效和不同插件隔离。 <!-- comet-task:bb87d631-934a-4a8d-af9a-e32a390d30fd -->
- [ ] 4.5 实现候选目录、产物摘要、完整性元数据和原子缓存发布，测试残缺/篡改缓存触发重编译、失败编译不覆盖最近可用缓存且临时候选可清理。 <!-- comet-task:5bd5d58d-a15d-49c3-bbb1-2bf4de8727a6 -->
- [ ] 4.6 把 Roslyn、缓存、能力、加载与初始化错误统一映射为 `PluginDiagnostic`，测试文件、行、列、错误码与阶段完整，并验证诊断不含源码正文、外部绝对路径或敏感运行数据。 <!-- comet-task:0ba5923d-d44d-4722-b34a-70b3097a9f1a -->
- [ ] 4.7 将成功编译产物接入现有 collectible ALC、Manifest 一致性、Extension/Pipeline 和 UI Contribution 流程，使用同功能的源码/预编译插件对照测试验证行为一致。 <!-- comet-task:80c29900-6775-41c0-971e-aaa986b0effe -->
- [ ] 4.8 实现“先验证新实例、再原子替换”的 reload，测试成功切换、首次失败拒绝、更新失败保留当前实例和旧 ALC 回收失败仅产生安全诊断。 <!-- comet-task:1f5a8d6d-aa48-4d4f-bf1c-ef9bcb3a1948 -->
- [ ] 4.9 在安装/启用源码插件入口增加可信代码边界披露及本地化文案，UI 测试验证不会把 ALC 或 Bridge 表述为安全沙箱。 <!-- comet-task:f7d28e73-a9a8-4bd8-97f4-db9621bb7705 -->

- [ ] 4.10 随发布包提供固定编译引用集，并将引用和本地化负载纳入缓存输入，验证无 SDK 编译、引用缺失诊断、四语言缓存布局及文案更新生效。 <!-- comet-task:8fa61a79-c51c-4b2a-904e-a417c7a5a922 -->
- [ ] 4.11 为 Provider 调用绑定 request/response 代际租约，覆盖新请求切换、旧请求/SSE 完成前资源不释放、启停竞争及组合根旧引用回归。 <!-- comet-task:d8c634f9-404e-406f-bd8c-6a4f79d97345 -->

## 5. Credential Protection 迁移

- [ ] 5.1 为 Credential Protection Manifest 声明所需 Bridge 能力，并把 `CredentialTokenStore` 改为通过 `storage.sqlite/v1` 打开 `credential-tokens.db`；使用新建 Vault 测试验证表名、列名、索引和初始化 SQL 不变。 <!-- comet-task:220c7e73-88a7-4abf-a655-44c164eb1484 -->
- [ ] 5.2 将 token 创建、精确查询和批量恢复接入异步 Bridge，并保留同步解析链与单一同步适配点；测试同一明文稳定复用 token、跨请求恢复、重复 token 去重、取消、释放以及 UI SynchronizationContext 下无死锁。 <!-- comet-task:fb7a6c1c-25d2-4edd-9b80-ba6ff74c9ac2 -->
- [ ] 5.3 保留“唯一约束冲突后释放失败事务并重新精确查询”的算法，使用并发冲突测试验证不会签出第二个 token 或产生嵌套 placeholder。 <!-- comet-task:7a9cc787-0334-4a30-ad7f-55b0db5a029a -->
- [ ] 5.4 使用由旧实现创建的真实 `credential-tokens.db` 作为兼容夹具，验证升级后无需迁移即可恢复历史 placeholder，DPAPI 前缀、密文和 OriginalHash 保持原样且不创建第二个 Vault。 <!-- comet-task:60e5ec3a-0df3-4b54-b76b-15ba477d2ce2 -->
- [ ] 5.5 覆盖 Bridge Busy/Locked/Corrupt、DPAPI 解密失败与未知 token 场景，验证 Credential Protection 保持原有确定性解析和 fail closed，不把明文发送给 Provider。 <!-- comet-task:5107c058-d1e3-4d5b-9163-2a356ad46433 -->
- [ ] 5.6 从 Credential Protection csproj 移除 `Microsoft.Data.Sqlite`，更新宿主/测试负载复制并扩展发布契约测试，验证插件目录不存在 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*`、`e_sqlite3`，宿主侧实现仍可运行。 <!-- comet-task:554527df-f334-4923-ad69-45133e24c64d -->

## 6. 插件开发 Skill 与工具闭环

- [ ] 6.1 实现共享插件开发应用服务和 `loomx.plugin.create/validate/compile/test/reload/diagnostics` 工具注册，使用工具契约测试验证稳定名称、机器可读结果和统一 `PluginDiagnostic`。 <!-- comet-task:9efd77a2-d371-419d-94b2-d92ac7aee6d9 -->
- [ ] 6.2 实现 create 模板生成器，验证生成目录包含合法 Manifest、显式 sources、入口源码、测试占位和 i18n 负载，且不强制生成 `.csproj`。 <!-- comet-task:cbe387c5-15a1-472c-8835-1a9d43f1acd2 -->
- [ ] 6.3 让 validate、compile 和 diagnostics 复用生产 Manifest、能力协商、路径和 Roslyn 服务，使用多错误输入测试验证一次返回可安全收集的问题且无规则分叉。 <!-- comet-task:f1c02aae-561b-434f-ae2c-4093feef867e -->
- [ ] 6.4 实现隔离 test 工具和临时 Bridge，测试默认不连接真实 Provider、不读取生产 Vault、不修改 LoomX 配置，并能验证 Pipeline、声明式 UI 和 SQLite 插件测试。 <!-- comet-task:70fd1107-f8c9-48d6-bca4-aaa931855cac -->
- [ ] 6.5 让 reload 工具调用生产 Runtime 的原子替换路径，使用正在运行的测试宿主验证成功生效和失败保留旧实例。 <!-- comet-task:2d75515d-0de5-475e-a04c-485bf438e7bf -->
- [ ] 6.6 编写版本化插件开发 Skill，覆盖 Manifest Schema、源码模板、Pipeline、声明式 UI、SQLite/Logging/Notifications Bridge、权限边界、安全规则和 AI 修复循环；按 Skill 中步骤生成并加载示例插件验证文档可执行。 <!-- comet-task:70f4eb88-0074-4135-8522-eb08501e8967 -->

## 7. 集成、发布与回归

- [ ] 7.1 扩展 `LoomX.PluginPlayground` 端到端覆盖源码编译、Bridge SQLite CRUD、日志、通知、Pipeline、reload 和 Credential Protection 历史 Vault，并验证全部检查通过。 <!-- comet-task:44d21847-c7bb-4800-aeef-0c9bd92e1ce3 -->
- [ ] 7.2 运行完整 `dotnet test`，修复所有 Plugin Runtime、Credential Protection、UI、本地化和发布布局回归，并保存通过结果作为 Verify 输入。 <!-- comet-task:277f9bc1-0799-4bea-b20a-174aefa98e65 -->
- [ ] 7.3 执行 Windows 发布与打包，检查宿主只携带一套 SQLite 实现、各插件负载目录无 SQLite 动态链、源码插件示例布局正确，并将可读时间命名的成品放入 `outputs/`。 <!-- comet-task:0019cb6e-914f-465e-8cf6-5b0152ca5bf0 -->
- [ ] 7.4 在真实发布包中执行无 SDK 源码插件创建→编译→诊断修复→测试→加载→reload 流程，并验证重启后缓存命中、插件数据仍位于 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\`。 <!-- comet-task:8ba86c5c-6fb5-48ae-afec-ed3561f0506c -->
- [ ] 7.5 使用现有 Credential Protection 端到端请求/响应用例复验 placeholder 完整性指令、跨 chunk/SSE 恢复、未知或损坏 token fail closed、禁用与卸载生命周期均未改变。 <!-- comet-task:4a03cea1-9a66-4c87-808e-befb55568744 -->

## Workflow follow-up

- Build、Verify 和审查完成后归档该 OpenSpec change。
- 归档后确认三个新 capability 已进入主 specs，且未意外修改既有 Pipeline、UI Contribution 或 Credential Protection 安全语义。
