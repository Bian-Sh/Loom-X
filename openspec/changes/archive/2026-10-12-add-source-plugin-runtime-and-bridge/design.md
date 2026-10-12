---
comet_change: add-source-plugin-runtime-and-bridge
role: technical-design
canonical_spec: openspec
archived-with: 2026-10-12-add-source-plugin-runtime-and-bridge
status: final
---

# Design

## Context

参见 [proposal.md](proposal.md) 的动机。本 change 建立在现有插件运行时之上：插件包位于 `plugins/<id>/`，由 `plugin.manifest.json` 描述；预编译插件通过独立 collectible `AssemblyLoadContext` 加载，只与 `LoomX.Plugin.Abstractions` 交换类型；插件初始化上下文当前包含插件 id 与 `DataDirectory`；Credential Protection 在插件项目内直接引用 `Microsoft.Data.Sqlite`，并在插件数据目录中维护 `credential-tokens.db`。

本设计同时受到以下约束：

- 现有预编译插件、Pipeline 顺序、失败策略、启停语义和声明式 UI 协议必须保持兼容。
- Credential Protection 的既有 Vault 是长期 placeholder 的唯一恢复来源，不能改名、重建或静默创建第二份数据库。
- 插件包相对路径与发布布局不能出现平铺副本；源码文件也必须留在各自 `plugins/<id>/` 内。
- Bridge 是宿主能力边界，但本阶段源码插件仍在宿主进程和当前用户权限下运行；Bridge 不是恶意代码沙箱。
- 公开契约从一开始应能由进程内对象或未来 IPC Proxy 实现。

## Goals / Non-Goals

**Goals:**

- 在不要求插件携带 `.csproj` 或用户安装 SDK 的情况下加载 Manifest 驱动的 C# 源码插件。
- 让插件仅依赖稳定契约即可调用 SQLite、结构化日志和 Toast 通知，并能独立调试业务逻辑。
- 由宿主统一承担 SQLite 提供程序、原生库、连接、参数绑定、事务、路径约束和错误映射。
- 保留 Credential Protection 现有数据库与安全行为，并从其插件负载中移除 SQLite 动态依赖。
- 为 AI 提供稳定知识与结构化开发工具闭环。

**Non-Goals:**

- 不实现独立 Plugin Worker、AppContainer、Restricted Token 或其他 OS 沙箱。
- 不把 `AssemblyLoadContext` 当作安全边界。
- 不支持任意 NuGet 恢复、插件自带编译器参数、`.csx` scripting 或目录 glob。
- 不引入默认 KV abstraction、统一数据库文件名或统一表结构。
- 不允许插件获取 `IServiceProvider`、`ILoggerFactory`、`HttpClient`、`DbConnection`、`DbTransaction`、窗口或 ViewModel。
- 不改变现有 Pipeline 和声明式 UI 的功能语义，也不迁移或销毁现有插件数据。

## Decisions

### 1. Manifest 使用单一 Runtime 联合结构，保留旧程序集字段兼容

新增可选的 `runtime` 对象，并使用现有 snake_case JSON 命名：

```json
{
  "id": "example.my-plugin",
  "version": "0.1.0",
  "runtime": {
    "kind": "csharp-source",
    "entry_type": "Example.MyPlugin",
    "sources": [
      "src/Models.cs",
      "src/RequestExtension.cs",
      "src/MyPlugin.cs"
    ]
  },
  "host_capabilities": [
    "storage.sqlite/v1",
    "logging/v1",
    "notifications/v1"
  ],
  "capabilities": [],
  "extensions": []
}
```

`runtime.kind` 首版支持：

- `assembly`：包含 `assembly` 与 `entry_type`。
- `csharp-source`：包含 `entry_type` 与显式 `sources`。

旧 Manifest 的顶层 `assembly` 与 `plugin_type` 在 `runtime` 缺失时继续按 `assembly` Runtime 解析；两种写法同时存在时拒绝，避免双重来源。`host_capabilities` 表示插件向宿主申请的能力，与现有表示插件自身行为的 `capabilities` 分离。

选择显式 sources 而不是目录 glob，是为了让 Manifest 成为唯一结构入口、让缓存输入可审计，并避免目录中临时文件被意外编译。替代方案是生成临时 `.csproj` 调用 `dotnet build`，但它依赖本机 SDK、项目恢复和外部构建语义，不符合目标。

### 2. 公共契约与宿主实现分层，SQLite 实现单独成宿主 DLL

程序集职责如下：

- `LoomX.Plugin.Abstractions.dll`：插件 SDK；包含 Manifest 模型、Plugin API、Bridge 接口、值 DTO、稳定错误与诊断。插件开发和编译只引用此契约，不引用 SQLite 实现。
- `LoomX.PluginHost.dll`：发现、验证、能力协商、源码编译、缓存、加载、重新加载与 Bridge 生命周期编排。
- `LoomX.Plugin.Storage.Sqlite.dll`：宿主侧 `storage.sqlite/v1` 实现；它统一承载插件 Bridge 的 SQLite 提供程序依赖；宿主既有配置数据库依赖不在本次去重范围内。
- `LoomX`：组合根；把统一 `ILogger` 管线和现有 `ToastService` 适配到 Logging/Notifications Bridge。

SQLite 实现单独 DLL 的目的不是让插件引用它，而是把提供程序与原生依赖固定在宿主发布边界，使插件目录保持纯净，也便于用测试实现替换。插件调试依赖 Abstractions 和宿主提供的测试 Bridge，不需要复制生产 SQLite DLL。

Logging 和 Notifications 适配器暂不拆独立项目：它们体量小且依赖宿主组合根，提前拆分只会增加程序集数量。

### 3. 初始化上下文增加 Bridge，同时保持已有插件二进制入口

`PluginInitializationContext` 增加 `Bridge` 属性，并保留现有 `PluginInitializationContext(string, string)` 二参数构造与 `PluginId`、`DataDirectory` 属性，避免改变旧 DLL 调用的构造签名。宿主为每个插件创建与不可变 plugin id 绑定的 Bridge；所有能力授权、数据库根目录、日志作用域和通知来源均来自该绑定身份，而不是插件调用参数。

新插件文档优先使用 Bridge。`DataDirectory` 本 change 不删除，因为现有插件仍用它保存 JSON 配置和统计文件；删除会把 SQLite 边界调整扩大成全插件文件系统迁移。它不提供安全沙箱保证，后续若要治理任意文件访问，应新增 `storage.files/v1` 并配合进程隔离，而不是在本 change 中伪装成已隔离。

### 4. Bridge 以精确能力名协商，未声明能力使用拒绝代理

Manifest 使用 `<capability>/v<major>` 字符串，例如 `storage.sqlite/v1`。宿主在执行插件入口前完成精确主版本匹配：

1. 解析并去重 `host_capabilities`。
2. 验证宿主是否支持全部必需能力。
3. 为已声明能力创建实现，为未声明能力创建返回 `CapabilityNotDeclared` 的拒绝代理。
4. 任一必需能力不兼容时，不编译或不实例化插件。

v1 内只允许向后兼容的新增枚举值保护、可选字段或新方法版本接口；破坏性变更发布新的 major 能力名。拒绝代理让根 Bridge 形状稳定，同时避免“属性存在即默认授权”。

### 5. Bridge DTO 使用封闭值类型并兼容未来 IPC

根接口方向：

```csharp
public interface IPluginBridge
{
    IPluginStorageBridge Storage { get; }
    IPluginLogBridge Log { get; }
    IPluginNotificationBridge Notifications { get; }
}
```

公开 API 只使用 string、enum、`byte[]`、只读 record DTO、`CancellationToken`、`ValueTask` 与受控 Bridge 接口。禁止 `object`、`Stream`、delegate、`DbConnection`、`DbTransaction` 和宿主私有类型。Bridge 接口在进程内是普通对象，未来 IPC 实现可把同一调用映射为请求、响应和远端句柄。

SQL 与日志值分别使用封闭 union DTO，防止类型系统退化为 `Dictionary<string, object?>`。Blob 和结果集由宿主设置大小、行数与总字节限制；超过限制映射为稳定的 `ResourceLimit` 错误，避免进程内与 IPC 行为分叉。

### 6. SQLite Bridge 保留 SQL 语义，但控制连接和文件边界

核心契约：

```csharp
public interface IPluginStorageBridge
{
    ValueTask<IPluginSqliteDatabase> OpenSqliteAsync(
        string relativePath,
        CancellationToken cancellationToken = default);
}

public interface IPluginSqliteDatabase : IAsyncDisposable
{
    ValueTask<int> ExecuteAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<PluginSqlRow?> QuerySingleAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<PluginSqlRow>> QueryAsync(PluginSqlCommand command, CancellationToken cancellationToken = default);
    ValueTask<IPluginSqliteTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
```

`IPluginSqliteTransaction` 提供同形的 Execute/Query 与 Commit/Rollback；事务对象完成或释放后不可复用，v1 不支持嵌套事务。事务与数据库共享串行操作门，活动事务期间非事务命令不能混入该事务；裸 BEGIN/COMMIT/ROLLBACK/SAVEPOINT 由语句策略拒绝，事务控制只能通过句柄完成。`PluginSqlCommand` 包含一条 SQL 语句和命名参数；多语句批处理拆成多次调用，事务保证原子性。

`PluginSqlValue` 的种类固定为 Null、Integer、Real、Text、Blob。`PluginSqlRow` 保留列顺序，同时允许按列名读取；重复列名必须通过索引区分。`QuerySingleAsync` 返回第一行或 null，`QueryAsync` 返回受宿主限制的缓冲结果集，不暴露 Reader 或 Stream。

选择 SQL Bridge 而不是 KV，是因为插件需要自行掌握表结构、索引、事务和迁移，且 Credential Protection 已有必须原样复用的关系模型。选择 Bridge 而不是暴露 `SqliteConnection`，是为了避免插件绑定提供程序版本、原生加载和进程内实现。

### 7. 数据库路径解析和 SQLite 文件操作均由宿主约束

`OpenSqliteAsync(relativePath)` 将目标解析到：

```text
%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\<relativePath>
```

解析顺序：拒绝空值、根路径、UNC、设备路径与盘符相对路径；规范化分隔符；计算绝对路径；验证目标位于插件根目录；逐段检查插件数据根、已存在父目录及数据库文件本身的 reparse point/符号链接；创建缺失父目录；创建后再次验证最终路径。所有文件系统操作使用宿主绑定的 plugin id，插件不能提交其他 id。

仅约束打开入口还不够，因为 SQLite 的 `ATTACH`、`VACUUM INTO`、扩展加载等语句可能再次触达文件系统。宿主实现必须关闭扩展加载，并通过 SQLite authorizer 与受限语句策略拒绝可打开外部文件或改变提供程序边界的操作；v1 不支持 ATTACH。普通 DDL、DML、查询、索引、事务和数据库内 PRAGMA 保持可用，Credential Protection 所需的 WAL、synchronous 与 busy_timeout 由独立命令设置。

该约束是 Bridge 服务保证，不是完整进程沙箱。可信插件若直接调用 `System.IO` 仍拥有当前进程权限；真正阻止绕过需要未来 Worker/OS sandbox。

### 8. SQLite 生命周期和错误由宿主规范化

数据库句柄拥有宿主连接资源，并默认使用 read-write-create、禁用池化或采用宿主可控池策略。插件显式释放句柄；Runtime 卸载插件时兜底关闭该插件全部数据库和未完成事务。事务释放但未提交时回滚。

提供程序异常映射为 `PluginSqlException` 与 `PluginSqlError`：ConstraintViolation、Busy、Locked、Corrupt、ReadOnly、PermissionDenied、InvalidPath、InvalidSql、Cancelled、ResourceLimit、Unknown。错误携带稳定代码、安全消息和可选的 SQLite 主错误码，但不携带 SQL 参数值、数据库绝对路径或提供程序异常对象。

取消令牌传入所有异步操作；取消映射为 Cancelled。对无法立即中断的本地调用，宿主在返回前保证连接仍处于可判定状态，必要时废弃该连接而不是把未知事务状态交还插件。

### 9. Logging 与 Notifications 使用最小值 DTO

`logging/v1` 接收级别、消息模板和 `PluginLogField` 列表；字段值限制为 Null、Boolean、Integer、Real、Text。宿主自动增加 PluginId 和能力作用域，再映射到 `ILogger`/Serilog。v1 消息模板只接受 `{Name}` 占位与 `{{`/`}}` 字面花括号，拒绝 format/alignment（如 `{Count:N2}` 或 `{Count,8}`）。宿主加可见 `[pluginId] ` 来源前缀；512 字符是交给 Serilog 前按字段值预展开的总预算，包含来源前缀，不保证 Serilog quoting/escaping 后的最终输出仍不超过 512 字符。保留来源字段不可由插件覆盖。宿主限制字段数量、键长度和值长度，并对敏感键名和已知凭据形态拒绝或脱敏。插件仍负有不提交敏感数据的契约责任，过滤器不能被描述为绝对防泄漏安全边界。

`notifications/v1` 接收级别与短安全摘要，由宿主映射到 `ToastService` 的 Success/Info/Warning/Error。宿主执行长度、频率和敏感内容检查；插件不能控制窗口、持续时间、位置、导航或渲染类型。

### 10. 源码编译使用 Roslyn 内嵌服务和原子内容寻址缓存

源码编译器位于 PluginHost 内部，随宿主发布 Roslyn C# 编译组件。流程：

1. 解析 Manifest 并校验 runtime、sources、host capabilities 与路径。
2. 按 sources 声明顺序读取 UTF-8 源码，诊断使用 Manifest 相对路径。
3. 生成缓存键：规范化完整 Manifest、每个源码相对路径及字节、Plugin API 版本、编译器版本、语言版本、全部生效编译选项、实际引用程序集摘要，以及进入产物的本地化负载内容。
4. 检查 `%LOCALAPPDATA%\LoomX\plugin-cache\source\<plugin-id>\<hash>\` 的完整性元数据。
5. 未命中时，以固定语言版本、nullable、deterministic 和 portable PDB 选项编译到同根临时候选目录。
6. 编译成功后写入元数据并以原子目录切换发布；失败时删除本次候选，不触碰已发布缓存。
7. 缓存内部镜像 `plugins/<id>/plugin.manifest.json` 与 `i18n/<culture>.json`，程序集位于对应插件目录，并嵌入默认 en-US；保持 PluginTextCatalog 原查找顺序，不添加特判。生成程序集身份包含内容摘要，防止 reload 后文案缓存串代际。`i18n` 更新参与缓存失效。通过现有 Plugin API、入口类型和 Manifest 一致性验证后，在新的 collectible ALC 中加载。

引用集合来自发布包随附的固定 net10.0 编译引用集合、`LoomX.Plugin.Abstractions` 及明确允许的共享契约。构建时收集并发布，运行时校验清单与摘要，不搜索本机 SDK，不把宿主已加载程序集或 TPA 全集当作插件白名单，不解析插件目录任意 DLL，不发起 NuGet 恢复。引用集缺失或摘要不符时返回安全诊断，不在线补齐。缓存元数据记录输入摘要和产物摘要，避免只凭目录名信任残缺或篡改产物。

首次编译失败时插件不加载；更新或 reload 失败时保留当前实例。成功 reload 先构造并验证新实例，再原子替换 Runtime 注册，最后释放旧实例；旧 ALC 无法及时回收只记诊断，不回滚已经成功的切换。

### 11. 诊断模型统一服务 UI、工具和 AI

新增公共 `PluginDiagnostic` 值模型，包含 PluginId、Stage、Severity、Code、Message、RelativePath、Line、Column。Stage 至少覆盖 Manifest、SourceRead、Compile、Cache、Capability、Load、Initialize、Bridge、Test、Reload。

所有外部诊断必须使用相对路径和安全消息。编译诊断保留稳定 ID、相对路径与行列，消息由受控模板生成，不直接回显可能含源码字面量的 Roslyn 原始消息或任意异常消息。详细异常只通过宿主 `ILogger<T>` 记录安全摘要，不把源码正文、SQL 参数、Vault 数据、Prompt 或请求/响应正文放入诊断。

### 12. Credential Protection 只替换数据库访问层

Credential Protection 仍是预编译第一方插件，本 change 不把它转换为源码插件。迁移步骤：

- Manifest 增加 `storage.sqlite/v1`，需要用户反馈时增加 `logging/v1`、`notifications/v1`。
- 初始化时保存 Bridge；`SensitiveRuleStore` 等现有 JSON 数据保持原路径和行为。
- `CredentialTokenStore` 改为通过 `OpenSqliteAsync("credential-tokens.db")` 初始化同一张 `CredentialTokens` 表。
- 保留同步 CredentialEngine/JSON/SSE 业务链，以插件内部单一适配点消费异步 Bridge，不为数据库依赖去重重写全链路 async。第一版宿主实现不捕获 UI SynchronizationContext；统一验证同步适配的取消、异常、资源释放与无死锁行为。唯一约束冲突捕获 `ConstraintViolation`；失败事务释放后在新操作中按 OriginalHash 精确查回既有 token。
- DPAPI 前缀、哈希算法、字段内容、表名、列名、索引和 fail closed 分支不变。
- 从插件 csproj 移除 `Microsoft.Data.Sqlite`；发布测试断言插件目录不存在 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*` 和 `e_sqlite3`，同时宿主发布目录具备唯一实现。

不执行数据库迁移意味着回滚到旧版本仍能读取同一 Vault。若新 Bridge 版本出现故障，回滚宿主与插件程序集即可；不得删除或恢复数据库快照覆盖用户新产生的 token。

### 13. 插件开发 Skill 通过宿主工具操作形成闭环

提供稳定工具名：

- `loomx.plugin.create`
- `loomx.plugin.validate`
- `loomx.plugin.compile`
- `loomx.plugin.test`
- `loomx.plugin.reload`
- `loomx.plugin.diagnostics`

这些工具共享同一应用服务并注册到既有 ToolRegistry，不重新实现 Manifest 或编译规则。create 不覆盖既有插件目录；compile 写缓存、test 执行可信代码和 reload 改变 Runtime 的风险等级如实标记，沿用工具授权与可信代码披露。集成 AI 在进程内调用；未来 CLI 适配器可以映射为 `loomx plugin <verb>`，但本 change 不要求为同一逻辑维护第二套实现。

Skill 随 Plugin API 版本发布，包含 Manifest Schema、目录模板、Pipeline、声明式 UI、Bridge、权限、示例和安全规则。AI 只需读取 Skill 和插件目录：validate/compile/test 返回 `PluginDiagnostic`，reload 使用 Runtime 的原子替换路径。测试使用隔离临时数据根、测试 Bridge 与假 Provider，默认不连接真实 Provider、不读取生产 Vault、不修改宿主配置。该隔离是受控测试流程的数据隔离，可信任意 C# 仍能直接访问进程资源，不得称为 OS 沙箱。validate/diagnostics 不执行插件代码；compile 只编译；test/reload 会执行可信代码。

### 14. reload 按请求代际切换，不立即释放在途实例

当前 ProviderExecutionPipeline 在启动时固定持有 request/response Pipeline，Runtime 的 Pipeline Entry 又存于可变 List。仅换 Runtime 注册会让旧引用继续工作，执行中直接改 List 会产生并发风险。采用候选实例、不可变注册快照与请求级代际租约：

- 候选编译、入口、初始化、Extension/UI 校验全部成功后才发布；同插件 reload 串行，与启停操作协调。
- ProviderExecutionPipeline 每次完整 Provider 调用取得成对 request/response 快照，不永久绑定启动时的旧 Pipeline；保留原构造用法供测试与兼容。
- 租约贯穿请求、响应和流式正文释放。新请求使用新代际，旧请求/SSE 继续旧快照，直到结束。
- 旧租约排空后解除 UI 订阅、回滚未完成事务、释放实例和 Bridge，并申请 ALC 卸载；未排空时不强制关闭数据库。
- 候选失败只释放本次候选资源。普通禁用不释放 Credential Protection 历史解析所需的 Bridge。
- 原子性只覆盖 Runtime 注册和句柄归属，不保证任意插件初始化副作用回滚。初始化持久化操作由可信插件自行保证幂等/事务性；失败 reload 不得恢复 Vault 快照覆盖新增 token。

选择请求级租约而不是暂停全局请求或直接替换 List，是为了同时保持新请求可用与 SSE 连续性；代价是长流会延长旧代际存活。

### 15. SQLite 策略与同步适配的验证边界

一命令一语句通过实际 prepare 语义确认，不按分号切割或正则关键字承担安全边界。authorizer 在 prepare 层拒绝文件逃逸，并用受限策略处理可能改变文件落点的 PRAGMA。活动事务由宿主管理，命令取消后连接状态必须可判定，否则关闭。

保持同步凭据业务链可最小化解析回归风险，但本地 SQLite 操作仍可能占用调用线程；Async 契约用于稳定调用和未来传输，不承诺底层已经是非阻塞 I/O。源码插件可直接 await Bridge；未来 Worker 的阻塞成本另行评估，不把本轮描述为已完成进程外隔离。

测试必须覆盖旧二参数构造、固定引用集、i18n 缓存失效、真实旧格式 Vault、跨事务混入拒绝、并发 reload 和完整 SSE 生命周期。敏感哨兵需覆盖 SQL 参数、源码字面量、异常、日志与 Toast。无 SDK 验证采用发布包与不调用外部 SDK 的子进程，仅改 PATH 不足以证明无 SDK 依赖。

## Risks / Trade-offs

- **[源码插件可执行任意受信任进程权限代码]** → 安装和启用时明确披露；不宣称 ALC 或 Bridge 是沙箱；未来以相同 DTO 契约迁移到 Worker。
- **[Bridge 路径限制可被进程内插件直接 System.IO 绕过]** → 把限制准确描述为 Bridge 服务边界；不以不完整的 API 黑名单冒充安全隔离。
- **[公共契约增长后难以二进制兼容]** → 按能力 major 版本演进，使用封闭 DTO、契约兼容测试和旧插件加载测试。
- **[SQL Bridge 过于通用，错误使用会造成锁竞争或大结果集]** → 一语句一命令、显式事务、取消、资源限制、busy/locked 分类和测试工具诊断。
- **[SQL 文件逃逸不只发生在连接路径]** → 禁用扩展加载，使用 SQLite authorizer 和语句策略阻止 ATTACH、VACUUM INTO 等文件型操作。
- **[宿主统一 SQLite 后升级影响所有插件]** → 实现独立程序集、固定版本、集中兼容测试；错误契约与提供程序异常解耦。
- **[编译缓存残缺或被篡改]** → 候选目录、完整性元数据、产物摘要和原子发布；无效缓存重新编译。
- **[Roslyn 增加宿主体积]** → 只在宿主携带一次，换取所有插件无需 SDK 和重复编译器依赖；发布体积纳入验收。
- **[日志和通知过滤无法识别所有秘密]** → 插件契约禁止提交敏感内容，宿主过滤仅作为纵深防御，并对字段和长度做最小化。
- **[Credential Store 从同步改异步引入竞态]** → 保留单插件异步互斥和“冲突事务结束后重新精确查询”的既有算法，增加并发回归测试。

## Migration Plan

1. 在 Abstractions 增加兼容的 Manifest Runtime、host capabilities、Bridge、SQL/日志/通知 DTO、错误和诊断模型，并以契约测试锁定 IPC 友好形状。
2. 在 PluginHost 增加能力协商、每插件 Bridge 绑定、源码路径校验、Roslyn 编译、缓存与 reload；现有 assembly Manifest 继续通过原路径加载。
3. 增加宿主 SQLite 实现程序集及组合根适配器，完成路径、authorizer、事务、取消、资源限制和错误映射测试。
4. 迁移 Credential Protection 的 token store，使用既有数据库运行兼容性测试；发布布局确认 SQLite 依赖只存在宿主侧。
5. 增加源码插件示例、开发 Skill 与 `loomx.plugin.*` 工具，并覆盖无 SDK 编译、诊断修复、隔离测试和失败 reload。
6. 完成端到端发布验证后默认启用新 Runtime 类型；旧预编译插件无需迁移。

回滚时恢复旧宿主和 Credential Protection 程序集，保留 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\` 数据与源码编译缓存。缓存可被旧版本忽略，但不得在回滚流程中自动删除；Credential Vault 无格式变化。
