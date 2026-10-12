# Comet Design Handoff

- Change: add-source-plugin-runtime-and-bridge
- Phase: design
- Mode: full
- Context hash: d8db719913a358d7ee7b8f590deb939b2c764e0cb19004db35c4d570585c60c9

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/add-source-plugin-runtime-and-bridge/proposal.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/proposal.md
- Lines: 1-36
- SHA256: fdb2d991879337af9330d0a0d204b54379f4e04e1e113ed983e2aa373f658223

```md
# Proposal

## Why

LoomX 当前插件以预编译程序集为交付单位，插件开发者既要理解宿主实现与依赖装配，又要自行处理 SQLite 等宿主已经具备的基础设施，导致插件包重复携带原生动态库、调试和分发成本高，也不利于 AI 直接生成、验证和迭代插件。需要把稳定的宿主能力收敛为版本化 Bridge，并支持由 Manifest 描述的 C# 源码插件，使插件只依赖公共契约即可开发和运行。

## What Changes

- 扩展插件 Manifest，允许声明 `csharp-source` Runtime、入口类型、显式源码文件列表以及所需 Bridge 能力与版本；插件无需携带 `.csproj`。
- 宿主内置 Roslyn 编译流程，将可信的 C# 源码插件编译为 DLL 与 portable PDB，并使用内容寻址缓存复用结果；编译失败保留最近一次可用缓存并输出可供 UI 和 AI 修复的结构化诊断。
- 新增独立、版本化且适合未来 IPC 的 `LoomX.Plugin.Abstractions` Bridge 契约。首版公开 `storage.sqlite/v1`、`logging/v1` 与 `notifications/v1`，插件不直接获取宿主实现对象、依赖注入容器或具体数据库类型。
- `storage.sqlite/v1` 公开参数化 SQLite CRUD、查询和事务能力。插件自行决定是否落盘、数据库相对路径、文件名、表结构、索引和迁移；宿主将数据库路径限制在 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\`，并统一提供 SQLite 实现与异常映射。
- Credential Protection 改用 SQLite Bridge 访问既有 `credential-tokens.db`，保持文件名、表结构、DPAPI 密文、精确查表、唯一约束冲突处理和 fail closed 行为不变，同时从插件负载中移除 SQLite 托管与原生动态库。
- 提供面向用户与 AI 的插件开发 Skill 和验证工具闭环，覆盖创建、校验、编译、测试、诊断与重新加载，并仅描述稳定 Manifest、Pipeline、声明式 UI 和 Bridge 契约。
- 明确源码插件第一阶段属于可信代码扩展：不把 `AssemblyLoadContext` 宣称为安全沙箱，不实现独立 Worker/AppContainer，不支持任意 NuGet 恢复，也不把 `.csx` scripting 作为正式插件模型。

## Capabilities

### New Capabilities

- `source-plugin-runtime`: 定义 Manifest 驱动的 C# 源码收集、内置编译、内容寻址缓存、结构化诊断、加载与重新加载行为。
- `plugin-host-bridge`: 定义插件与宿主之间版本化、实现无关、可演进到 IPC 的能力边界，以及 SQLite、日志和通知 Bridge 的行为与隔离要求。
- `plugin-development-skill`: 定义面向用户和 AI 的插件开发知识包、模板及创建到重新加载的工具闭环。

### Modified Capabilities

无。现有 Pipeline 顺序、失败策略、声明式 UI 行为和 Credential Protection 的用户可见安全语义保持不变；本 change 仅迁移其宿主依赖实现。

## Impact

- 公共契约：`LoomX.Plugin.Abstractions` 的插件初始化、Manifest、Bridge DTO、诊断和能力协商契约。
- 插件宿主：`LoomX.PluginHost` 的 Manifest 验证、源码编译、缓存、加载、能力检查、路径隔离与 Bridge 生命周期。
- 宿主应用：SQLite Bridge 实现、日志映射、Toast 通知映射、插件诊断与开发工具入口；SQLite 实现可位于独立宿主程序集，例如 `LoomX.Plugin.Storage.Sqlite.dll`。
- 第一方插件：`plugins/LoomX.CredentialProtection` 移除 `Microsoft.Data.Sqlite` 直接依赖并通过 Bridge 访问原数据库。
- 构建、发布与测试：源码插件负载布局、缓存键稳定性、编译诊断、路径逃逸、事务与错误映射、旧 Vault 兼容性、插件目录中 SQLite 动态库缺失等契约测试。
- 开发者体验：新增插件开发 Skill、示例源码插件和结构化 CLI/工具接口。

```

## openspec/changes/add-source-plugin-runtime-and-bridge/design.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/design.md
- Lines: 1-273
- SHA256: bd3aa437acd0225cfccf5a694bfed0c58cc34deaccc537b69cb4b9382532dbdd

```md
---
comet_change: add-source-plugin-runtime-and-bridge
role: technical-design
canonical_spec: openspec
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

```

## openspec/changes/add-source-plugin-runtime-and-bridge/tasks.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/tasks.md
- Lines: 1-72
- SHA256: 85195c241b77e3edefb7ab21edfb02a8adfea928b6ae64a276c1670079917127

```md
# Tasks

## 1. 公共契约与 Manifest

- [x] 1.1 在 `LoomX.Plugin.Abstractions` 增加 Runtime 联合模型、`host_capabilities`、`PluginDiagnostic` 及稳定错误码，并通过契约序列化测试验证字段可确定性往返且不含宿主私有类型。 <!-- comet-task:72235196-cb85-4513-8f30-f0e12ce1e207 -->
- [x] 1.2 定义 `IPluginBridge`、Storage/Logging/Notifications 接口及封闭值 DTO，增加反射契约测试，验证公共签名不出现 `object`、`Stream`、delegate、`DbConnection`、`DbTransaction`、`IServiceProvider` 或宿主程序集类型。 <!-- comet-task:a3f4b72b-c955-45b4-9d4a-7b7fc318dfd5 -->
- [x] 1.3 扩展 Manifest 解析与验证以支持 `assembly`、`csharp-source` 和旧顶层程序集字段，测试有效声明、双重 Runtime、缺失入口、空/重复 sources、非法能力版本及旧插件兼容。 <!-- comet-task:728d1069-edb4-40b7-a62a-7ba66415a0aa -->
- [x] 1.4 扩展 `PluginInitializationContext` 以提供 Bridge 且保留现有属性和兼容构造方式，使用既有第一方插件和测试插件验证二进制入口及初始化行为未回归。 <!-- comet-task:d8c17566-c4d5-4982-9b28-5b80b738a707 -->
- [x] 1.5 实现 host capability 精确协商与未声明能力拒绝代理，测试不兼容能力在执行插件代码前被拒绝、未声明调用返回 `CapabilityNotDeclared`、不同插件授权互不影响。 <!-- comet-task:3c8df5e2-e9ab-4207-9668-c442a28d7f6b -->

## 2. 宿主 SQLite Bridge

- [x] 2.1 新建宿主侧 `LoomX.Plugin.Storage.Sqlite` 项目并把 `Microsoft.Data.Sqlite`/`SQLitePCLRaw` 依赖集中于该项目，构建测试验证 Plugin Abstractions 和普通插件项目不传递这些依赖。 <!-- comet-task:b28eb15e-361c-45ea-80bc-8745f78f06e7 -->
- [x] 2.2 实现与 plugin id 绑定的数据库路径解析和目录创建，覆盖相对嵌套路径、绝对路径、UNC、设备路径、盘符相对路径、`..`、空路径及大小写边界测试。 <!-- comet-task:172a5347-d48a-44a6-a8c4-acd4cb6407cd -->
- [x] 2.3 增加 Windows reparse point/符号链接逃逸检查并在测试临时目录验证现有父目录链接、创建前后竞态校验和跨插件路径均返回 `InvalidPath`。 <!-- comet-task:40bda651-a7a0-4193-9a6b-707d23986624 -->
- [x] 2.4 实现 SQLite 打开、单语句参数化 Execute、QuerySingle、Query 和 Null/Integer/Real/Text/Blob 映射，使用 CRUD、DDL、索引、Blob、空结果、重复列名和资源上限测试验证行为。 <!-- comet-task:e2aca947-f284-42f6-b367-7a145da607fb -->
- [x] 2.5 实现显式事务、提交、回滚、释放回滚、完成后拒绝复用和取消传播，使用原子写入、失败回滚、并发 busy/locked 与取消测试验证生命周期。 <!-- comet-task:3423f7ba-5076-4d87-87e3-3e2214f81f73 -->
- [x] 2.6 实现 `PluginSqlException` 错误映射，逐项测试 ConstraintViolation、Busy、Locked、Corrupt、ReadOnly、PermissionDenied、InvalidPath、InvalidSql、Cancelled、ResourceLimit 和 Unknown，确认消息不含参数值或数据库绝对路径。 <!-- comet-task:fb8c5646-1a63-41fd-ad62-55d3f2bba90a -->
- [x] 2.7 关闭 SQLite 扩展加载并增加 authorizer/语句限制，测试 ATTACH、外部路径型 VACUUM 和提供程序替换被拒绝，而普通 PRAGMA、DDL、DML 与查询保持可用。 <!-- comet-task:cd65eb11-68d9-4122-bfac-a7ffb98f1341 -->
- [x] 2.8 把数据库句柄和事务登记到插件 Bridge 生命周期，在插件卸载时关闭资源并回滚未完成事务；通过文件可重新打开、句柄不可复用和其他插件不受影响的测试验证。 <!-- comet-task:fe042dc6-143e-4987-a09c-5227006fe633 -->

## 3. Logging 与 Notifications Bridge

- [x] 3.1 实现 Logging Bridge 到 `ILogger`/Serilog 的级别、模板、字段和 PluginId 作用域映射，测试结构化字段输出以及敏感键名、凭据形态、超长字段和高频调用的拒绝或净化。 <!-- comet-task:2d120113-1d43-464b-8fff-24f9f8b880cc -->
- [x] 3.2 实现 Notifications Bridge 到现有 `ToastService` 的 Success/Info/Warning/Error 映射，测试自动归属、长度/频率限制、敏感内容拦截以及插件无法控制窗口或导航。 <!-- comet-task:0f824d00-7a70-4edd-a25b-5e7c1b524e0b -->
- [x] 3.3 在宿主组合根为每个插件创建授权后的 Bridge，并用多插件集成测试验证数据库根、日志作用域、通知来源和故障均按插件隔离。 <!-- comet-task:254f980b-7683-4dc5-b0d2-039d3ce6d497 -->

## 4. C# 源码插件运行时

- [x] 4.1 在 PluginHost 引入随应用发布的 Roslyn 编译能力和固定编译选项，使用没有外部 `dotnet` SDK 参与的测试进程验证最小源码插件可生成 DLL 与 portable PDB。 <!-- comet-task:b8fb2332-bcc6-4f6c-bb6c-1b7ece04c426 -->
- [x] 4.2 实现 Manifest 显式 sources 收集和插件包路径验证，测试未声明 `.cs` 不参与、缺失文件被诊断、目录穿越和 reparse point 逃逸在读取前被拒绝。 <!-- comet-task:55e9dd95-5f68-494b-8ddb-7455b9e96b91 -->
- [x] 4.3 实现受控引用集合与禁止任意 NuGet/插件目录 DLL 解析的规则，测试框架 API 与 Abstractions 可用、未允许第三方引用产生结构化编译错误且无网络恢复行为。 <!-- comet-task:f95170f5-995f-48bb-a3d1-701c1e993205 -->
- [x] 4.4 实现包含规范化 Manifest、源码路径及内容、Plugin API、编译器和选项的缓存键，使用单项输入变化测试验证缓存命中、失效和不同插件隔离。 <!-- comet-task:bb87d631-934a-4a8d-af9a-e32a390d30fd -->
- [x] 4.5 实现候选目录、产物摘要、完整性元数据和原子缓存发布，测试残缺/篡改缓存触发重编译、失败编译不覆盖最近可用缓存且临时候选可清理。 <!-- comet-task:5bd5d58d-a15d-49c3-bbb1-2bf4de8727a6 -->
- [x] 4.6 把 Roslyn、缓存、能力、加载与初始化错误统一映射为 `PluginDiagnostic`，测试文件、行、列、错误码与阶段完整，并验证诊断不含源码正文、外部绝对路径或敏感运行数据。 <!-- comet-task:0ba5923d-d44d-4722-b34a-70b3097a9f1a -->
- [x] 4.7 将成功编译产物接入现有 collectible ALC、Manifest 一致性、Extension/Pipeline 和 UI Contribution 流程，使用同功能的源码/预编译插件对照测试验证行为一致。 <!-- comet-task:80c29900-6775-41c0-971e-aaa986b0effe -->
- [x] 4.8 实现“先验证新实例、再原子替换”的 reload，测试成功切换、首次失败拒绝、更新失败保留当前实例和旧 ALC 回收失败仅产生安全诊断。 <!-- comet-task:1f5a8d6d-aa48-4d4f-bf1c-ef9bcb3a1948 -->
- [x] 4.9 在安装/启用源码插件入口增加可信代码边界披露及本地化文案，UI 测试验证不会把 ALC 或 Bridge 表述为安全沙箱。 <!-- comet-task:f7d28e73-a9a8-4bd8-97f4-db9621bb7705 -->

- [x] 4.10 随发布包提供固定编译引用集，并将引用和本地化负载纳入缓存输入，验证无 SDK 编译、引用缺失诊断、四语言缓存布局及文案更新生效。 <!-- comet-task:8fa61a79-c51c-4b2a-904e-a417c7a5a922 -->
- [x] 4.11 为 Provider 调用绑定 request/response 代际租约，覆盖新请求切换、旧请求/SSE 完成前资源不释放、启停竞争及组合根旧引用回归。 <!-- comet-task:d8c634f9-404e-406f-bd8c-6a4f79d97345 -->

## 5. Credential Protection 迁移

- [x] 5.1 为 Credential Protection Manifest 声明所需 Bridge 能力，并把 `CredentialTokenStore` 改为通过 `storage.sqlite/v1` 打开 `credential-tokens.db`；使用新建 Vault 测试验证表名、列名、索引和初始化 SQL 不变。 <!-- comet-task:220c7e73-88a7-4abf-a655-44c164eb1484 -->
- [x] 5.2 将 token 创建、精确查询和批量恢复接入异步 Bridge，并保留同步解析链与单一同步适配点；测试同一明文稳定复用 token、跨请求恢复、重复 token 去重、取消、释放以及 UI SynchronizationContext 下无死锁。 <!-- comet-task:fb7a6c1c-25d2-4edd-9b80-ba6ff74c9ac2 -->
- [x] 5.3 保留“唯一约束冲突后释放失败事务并重新精确查询”的算法，使用并发冲突测试验证不会签出第二个 token 或产生嵌套 placeholder。 <!-- comet-task:7a9cc787-0334-4a30-ad7f-55b0db5a029a -->
- [x] 5.4 使用由旧实现创建的真实 `credential-tokens.db` 作为兼容夹具，验证升级后无需迁移即可恢复历史 placeholder，DPAPI 前缀、密文和 OriginalHash 保持原样且不创建第二个 Vault。 <!-- comet-task:60e5ec3a-0df3-4b54-b76b-15ba477d2ce2 -->
- [x] 5.5 覆盖 Bridge Busy/Locked/Corrupt、DPAPI 解密失败与未知 token 场景，验证 Credential Protection 保持原有确定性解析和 fail closed，不把明文发送给 Provider。 <!-- comet-task:5107c058-d1e3-4d5b-9163-2a356ad46433 -->
- [x] 5.6 从 Credential Protection csproj 移除 `Microsoft.Data.Sqlite`，更新宿主/测试负载复制并扩展发布契约测试，验证插件目录不存在 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*`、`e_sqlite3`，宿主侧实现仍可运行。 <!-- comet-task:554527df-f334-4923-ad69-45133e24c64d -->

## 6. 插件开发 Skill 与工具闭环

- [x] 6.1 实现共享插件开发应用服务和 `loomx.plugin.create/validate/compile/test/reload/diagnostics` 工具注册，使用工具契约测试验证稳定名称、机器可读结果和统一 `PluginDiagnostic`。 <!-- comet-task:9efd77a2-d371-419d-94b2-d92ac7aee6d9 -->
- [x] 6.2 实现 create 模板生成器，验证生成目录包含合法 Manifest、显式 sources、入口源码、测试占位和 i18n 负载，且不强制生成 `.csproj`。 <!-- comet-task:cbe387c5-15a1-472c-8835-1a9d43f1acd2 -->
- [x] 6.3 让 validate、compile 和 diagnostics 复用生产 Manifest、能力协商、路径和 Roslyn 服务，使用多错误输入测试验证一次返回可安全收集的问题且无规则分叉。 <!-- comet-task:f1c02aae-561b-434f-ae2c-4093feef867e -->
- [x] 6.4 实现隔离 test 工具和临时 Bridge，测试默认不连接真实 Provider、不读取生产 Vault、不修改 LoomX 配置，并能验证 Pipeline、声明式 UI 和 SQLite 插件测试。 <!-- comet-task:70fd1107-f8c9-48d6-bca4-aaa931855cac -->
- [x] 6.5 让 reload 工具调用生产 Runtime 的原子替换路径，使用正在运行的测试宿主验证成功生效和失败保留旧实例。 <!-- comet-task:2d75515d-0de5-475e-a04c-485bf438e7bf -->
- [x] 6.6 编写版本化插件开发 Skill，覆盖 Manifest Schema、源码模板、Pipeline、声明式 UI、SQLite/Logging/Notifications Bridge、权限边界、安全规则和 AI 修复循环；按 Skill 中步骤生成并加载示例插件验证文档可执行。 <!-- comet-task:70f4eb88-0074-4135-8522-eb08501e8967 -->

## 7. 集成、发布与回归

- [x] 7.1 扩展 `LoomX.PluginPlayground` 端到端覆盖源码编译、Bridge SQLite CRUD、日志、通知、Pipeline、reload 和 Credential Protection 历史 Vault，并验证全部检查通过。 <!-- comet-task:44d21847-c7bb-4800-aeef-0c9bd92e1ce3 -->
- [x] 7.2 运行完整 `dotnet test`，修复所有 Plugin Runtime、Credential Protection、UI、本地化和发布布局回归，并保存通过结果作为 Verify 输入。 <!-- comet-task:277f9bc1-0799-4bea-b20a-174aefa98e65 -->
- [x] 7.3 执行 Windows 发布与打包，检查宿主只携带一套 SQLite 实现、各插件负载目录无 SQLite 动态链、源码插件示例布局正确，并将可读时间命名的成品放入 `outputs/`。 <!-- comet-task:0019cb6e-914f-465e-8cf6-5b0152ca5bf0 -->
- [x] 7.4 在真实发布包中执行无 SDK 源码插件创建→编译→诊断修复→测试→加载→reload 流程，并验证重启后缓存命中、插件数据仍位于 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\`。 <!-- comet-task:8ba86c5c-6fb5-48ae-afec-ed3561f0506c -->
- [x] 7.5 使用现有 Credential Protection 端到端请求/响应用例复验 placeholder 完整性指令、跨 chunk/SSE 恢复、未知或损坏 token fail closed、禁用与卸载生命周期均未改变。 <!-- comet-task:4a03cea1-9a66-4c87-808e-befb55568744 -->

## Workflow follow-up

- Build、Verify 和审查完成后归档该 OpenSpec change。
- 归档后确认三个新 capability 已进入主 specs，且未意外修改既有 Pipeline、UI Contribution 或 Credential Protection 安全语义。

```

## openspec/changes/add-source-plugin-runtime-and-bridge/.openspec.yaml

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/.openspec.yaml
- Lines: 1-2
- SHA256: 1dfdca412094f24473d8b842866201ec26f200b29b17064b03f1e4982c35a7b9

```md
schema: spec-driven
created: 2026-10-12

```

## openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-development-skill/spec.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-development-skill/spec.md
- Lines: 1-109
- SHA256: b8377c76c2475d7c74638120bd4158f9f478a95319023fa9f081830bbe28b48f

```md
# Spec Delta

## Purpose

定义 LoomX 面向用户和 AI 的插件开发知识包与工具闭环，使开发者无需掌握宿主内部实现或手工搭建编译环境，也能创建、诊断、测试和重新加载插件。

## ADDED Requirements

### Requirement: 提供版本化插件开发 Skill
LoomX SHALL 提供可由 AI 和用户读取的插件开发 Skill，并标明适用的 Plugin API 与 Manifest 版本。Skill MUST 只描述稳定公共契约和工具，不得要求插件依赖宿主内部实现。

#### Scenario: AI 开始开发插件
- **WHEN** AI 读取与当前 LoomX 版本匹配的插件开发 Skill
- **THEN** Skill 提供完成插件所需的公共入口、约束、目录布局和验证步骤

#### Scenario: Skill 与宿主版本不兼容
- **WHEN** Skill 声明的 Plugin API 主版本与宿主不兼容
- **THEN** 开发工具在编译或安装前报告版本不兼容，不继续猜测契约

### Requirement: Skill 覆盖完整稳定契约
Skill SHALL 说明 Manifest Schema、源码插件目录与模板、Pipeline API、声明式 UI API、Bridge 能力与版本、权限边界、日志与通知安全规则，并包含至少一个可运行示例。

#### Scenario: 创建使用 SQLite 的源码插件
- **WHEN** 用户要求 AI 创建需要持久化数据的源码插件
- **THEN** Skill 引导其在 Manifest 声明 `storage.sqlite/v1`、自定数据库相对路径并使用参数化 SQL Bridge

#### Scenario: 创建带 UI 的插件
- **WHEN** 用户要求插件在卡片或详情区域展示内容
- **THEN** Skill 引导其使用声明式 UI Contribution，不生成 Avalonia Control、XAML 或宿主 ViewModel 依赖

#### Scenario: 需要日志和通知
- **WHEN** 插件需要运行诊断和用户即时反馈
- **THEN** Skill 引导其分别使用 `logging/v1` 与 `notifications/v1` 并遵守敏感信息限制

### Requirement: 提供源码插件模板
开发工具 SHALL 能创建包含合法 Manifest、显式 sources、入口类型、基础源码、测试占位与本地化负载的最小源码插件模板。生成结果不得要求 `.csproj` 才能由 LoomX 编译。

#### Scenario: 创建最小插件
- **WHEN** 用户提供合法插件 id 并执行创建操作
- **THEN** 工具生成可通过 Manifest 校验和源码编译的最小目录结构

#### Scenario: 创建参数非法
- **WHEN** 插件 id、目标目录或入口类型不符合规范
- **THEN** 工具拒绝写入并返回可定位到输入字段的结构化诊断

### Requirement: 提供插件验证操作
开发工具 SHALL 提供 validate 操作，用于在执行插件代码前检查 Manifest、源码路径、能力版本、入口声明、负载布局和禁止依赖，并以机器可读格式返回全部可安全报告的问题。

#### Scenario: 验证有效插件
- **WHEN** 插件目录满足当前契约
- **THEN** validate 返回成功以及实际使用的 Plugin API 和能力版本

#### Scenario: 一次报告多个独立问题
- **WHEN** Manifest 同时存在缺失 source 和不支持能力版本的问题
- **THEN** validate 在安全可行范围内返回两个结构化诊断，而非只返回第一个问题

### Requirement: 提供插件编译与诊断操作
开发工具 SHALL 提供 compile 和 diagnostics 操作，复用宿主实际源码编译与诊断规则。操作 MUST 在没有本机 .NET SDK 的环境中可用，并输出供用户或 AI 自动修复的结构化结果。

#### Scenario: 编译成功
- **WHEN** 源码插件通过验证并编译成功
- **THEN** compile 返回成功、缓存身份和产物状态，不要求用户手工定位编译器

#### Scenario: 编译失败后自动修复
- **WHEN** compile 返回包含文件、行、列、错误码和安全消息的诊断
- **THEN** AI 可修改对应声明源码并再次调用 compile 验证修复

### Requirement: 提供隔离的插件测试操作
开发工具 SHALL 提供 test 操作，在受控测试宿主中验证入口创建、Extension 注册、Bridge 契约、声明式 UI 与插件自带测试，不得默认向真实 Provider 发送请求或修改宿主核心配置。

#### Scenario: 测试 Pipeline Extension
- **WHEN** 插件包含 Request 或 Response Extension 测试
- **THEN** test 在测试 Pipeline 和隔离插件数据目录中执行并返回结构化结果

#### Scenario: 测试需要 SQLite
- **WHEN** 插件测试调用 `storage.sqlite/v1`
- **THEN** test 提供与生产契约一致但数据目录隔离的 SQLite Bridge

### Requirement: 提供安全重新加载操作
开发工具 SHALL 提供 reload 操作，用于对已安装插件执行验证、编译和原子替换。重新加载失败 MUST 保持当前可用实例运行，并返回失败诊断。

#### Scenario: 重新加载成功
- **WHEN** 修改后的插件验证与编译均成功
- **THEN** reload 在运行时边界替换插件实例并报告新版本已生效

#### Scenario: 重新加载失败
- **WHEN** 修改后的插件编译或初始化失败
- **THEN** reload 不替换当前实例且返回结构化诊断

### Requirement: 支持 AI 闭环开发流程
插件开发接口 SHALL 允许 AI 按“读取 Manifest 与 sources、修改源码、验证、编译、读取诊断、修复、测试、安装或重新加载”的顺序反复执行，所有操作结果 MUST 可机器读取且具有稳定错误码。

#### Scenario: 从自然语言需求迭代到可加载插件
- **WHEN** AI 根据用户需求生成插件并在首次编译遇到错误
- **THEN** AI 可仅依据 Skill、插件文件和结构化工具结果完成修复与复验，无需读取宿主内部源码

#### Scenario: 工具输出包含敏感运行数据
- **WHEN** 编译、测试或重新加载过程中遇到包含凭据或请求正文的异常
- **THEN** 工具结果仅返回安全摘要和稳定错误码，不回显敏感原值
### Requirement: 开发诊断与测试隔离准确披露
开发工具 SHALL 对可能带源码字面量或敏感运行数据的编译与测试诊断使用稳定代码和受控安全消息。受控测试的数据隔离 MUST NOT 被描述为对可信任意 C# 的操作系统安全沙箱。

#### Scenario: 编译错误含敏感字面量
- **WHEN** 编译器原始错误消息可能包含源码中的敏感字面量
- **THEN** 工具只返回错误码、相对路径、行列和受控消息，不回显该原值

#### Scenario: 执行可信插件测试
- **WHEN** test 使用临时数据根、测试 Bridge 与假 Provider 执行插件
- **THEN** Skill 和工具准确说明此隔离不阻止任意可信 C# 直接访问当前进程权限内的资源

```

## openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-host-bridge/spec.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-host-bridge/spec.md
- Lines: 1-196
- SHA256: 4bf68ae56351cd3a057cb3c3df146203ff6be3249b34eb6b7e0e56d3054acde3

```md
# Spec Delta

## Purpose

定义插件调用 LoomX 宿主能力时所依赖的稳定、版本化和实现无关边界，使存储、日志与通知可由宿主统一治理，并为未来进程外插件执行保留兼容的契约形状。

## ADDED Requirements

### Requirement: 插件仅通过公共 Bridge 使用宿主能力
宿主 SHALL 在插件初始化上下文中提供版本化 Plugin Bridge。插件与宿主交换的能力类型 MUST 来自公共契约程序集，不得向插件暴露依赖注入容器、宿主私有对象、UI 实例或具体基础设施连接对象。

#### Scenario: 插件初始化获得 Bridge
- **WHEN** 通过兼容性检查的插件被初始化
- **THEN** 插件获得与自身 id 绑定的 Storage、Logging 和 Notifications Bridge

#### Scenario: 插件请求宿主实现对象
- **WHEN** 插件尝试把 `IServiceProvider`、宿主 ViewModel、窗口对象或具体数据库连接声明为初始化依赖
- **THEN** Runtime 拒绝该入口契约且不向插件传递这些对象

### Requirement: Manifest 声明并协商 Bridge 能力版本
插件 Manifest SHALL 声明所需 Bridge 能力及契约版本。Runtime MUST 在编译或实例化插件前完成兼容性检查；缺失或不兼容的必需能力 MUST 导致插件被拒绝，不得猜测、静默降级或部分执行未知语义。

#### Scenario: 所需能力均受支持
- **WHEN** 插件要求 `storage.sqlite/v1`、`logging/v1` 和 `notifications/v1` 且宿主均支持
- **THEN** Runtime 允许插件继续加载并提供对应 Bridge

#### Scenario: 必需能力版本不兼容
- **WHEN** 插件要求宿主不支持的 `storage.sqlite/v2`
- **THEN** Runtime 在执行任何插件代码前拒绝加载并返回能力不兼容诊断

#### Scenario: 未声明能力不得调用
- **WHEN** 插件未在 Manifest 声明 `notifications/v1` 却调用 Notifications Bridge
- **THEN** 宿主拒绝该调用并返回稳定的能力未授权错误

### Requirement: Bridge 契约可跨进程传输
Bridge 的公开输入、输出与错误 SHALL 使用字符串、枚举、字节数组、只读值 DTO、取消令牌和异步结果表达。契约 MUST NOT 包含任意 `object` 值、流、委托、数据库连接、数据库事务或宿主私有类型。

#### Scenario: 进程内实现可替换为 Proxy
- **WHEN** 宿主未来将同一插件从进程内执行迁移到进程外 Worker
- **THEN** 插件业务代码继续使用同一 Bridge 契约，调用参数和结果可被确定性序列化

#### Scenario: 不可传输类型被阻止进入公共契约
- **WHEN** Bridge API 变更引入 `object`、`Stream`、delegate 或具体数据库类型
- **THEN** 契约兼容性检查失败且该变更不得发布

### Requirement: 插件自行命名和组织 SQLite 数据库
`storage.sqlite/v1` SHALL 允许插件按需打开其自有数据根目录内的数据库相对路径。插件自行决定是否落盘、文件名、相对子目录、表结构、索引、SQL、事务和迁移，宿主不得强制统一的 `plugin-storage.db` 或 KV collection。

#### Scenario: 插件创建自定义数据库
- **WHEN** 插件打开 `credential-tokens.db`、`settings.db` 或 `cache/models.db`
- **THEN** 宿主在 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\<relative-path>` 打开对应 SQLite 数据库

#### Scenario: 插件选择不落盘
- **WHEN** 插件不调用 Storage Bridge
- **THEN** 宿主不为该插件自动创建默认数据库文件

#### Scenario: 不同插件使用相同文件名
- **WHEN** 两个插件都打开 `settings.db`
- **THEN** 两个数据库分别位于各自插件数据根目录且互不可见

### Requirement: SQLite 路径严格隔离
Storage Bridge MUST 拒绝绝对路径、UNC 路径、设备路径、盘符相对路径、目录穿越、空路径以及规范化后或经 reparse point、符号链接逃逸插件数据根目录的路径。

#### Scenario: 合法嵌套路径
- **WHEN** 插件打开 `cache/models.db` 且规范化结果位于本插件数据根目录内
- **THEN** 宿主创建所需插件内子目录并打开该数据库

#### Scenario: 跨插件路径被拒绝
- **WHEN** 插件打开 `../other-plugin/data.db` 或其他规范化后逃逸的路径
- **THEN** 宿主返回 `InvalidPath` 且不创建、不读取目标文件

#### Scenario: 链接逃逸被拒绝
- **WHEN** 插件目录内路径经 reparse point 或符号链接指向根目录之外
- **THEN** 宿主返回 `InvalidPath` 且不跟随该链接打开数据库

### Requirement: SQLite Bridge 提供参数化 CRUD 与查询
`storage.sqlite/v1` SHALL 提供参数化命令执行、单行查询、多行查询和显式事务。SQL 参数与行值 MUST 使用 `Null`、`Integer`、`Real`、`Text`、`Blob` 的封闭值联合表达，不得使用任意对象字典。

#### Scenario: 执行参数化写入
- **WHEN** 插件提交包含命名参数的 INSERT、UPDATE 或 DELETE 命令
- **THEN** 宿主绑定显式类型参数并返回受影响行数

#### Scenario: 查询多行结果
- **WHEN** 插件执行 SELECT 并请求多行结果
- **THEN** 宿主以有序行和显式列值类型返回结果，不泄露具体 SQLite Reader

#### Scenario: 查询单行无结果
- **WHEN** 插件执行单行查询且结果集为空
- **THEN** Bridge 返回明确的无结果状态而非伪造空行

### Requirement: SQLite Bridge 管理事务生命周期
Storage Bridge SHALL 提供与数据库句柄绑定的显式事务。提交、回滚、释放和取消 MUST 具有确定性结果；事务完成后再次执行命令 MUST 被拒绝。

#### Scenario: 事务提交
- **WHEN** 插件在事务内成功执行多条命令并提交
- **THEN** 所有修改以同一事务提交且事务进入已完成状态

#### Scenario: 事务中途失败并回滚
- **WHEN** 插件在事务内遇到失败后请求回滚或释放未提交事务
- **THEN** 宿主回滚未提交修改且后续不得复用该事务

#### Scenario: 操作被取消
- **WHEN** 调用方取消正在等待的数据库操作
- **THEN** Bridge 尽快终止等待并返回 `Cancelled`，不得把取消误报为未知数据库故障

### Requirement: SQLite 错误映射为稳定契约错误
宿主 MUST 将具体 SQLite 异常映射为 Bridge 稳定错误分类，至少覆盖 `ConstraintViolation`、`Busy`、`Locked`、`Corrupt`、`ReadOnly`、`PermissionDenied`、`InvalidPath`、`InvalidSql`、`Cancelled` 和 `Unknown`，不得向插件暴露具体提供程序异常类型。

#### Scenario: 唯一约束冲突
- **WHEN** 插件写入违反唯一约束的数据
- **THEN** Bridge 返回 `ConstraintViolation`，插件可按自身业务规则重新查询或重试

#### Scenario: 数据库锁定
- **WHEN** SQLite 报告 busy 或 locked
- **THEN** Bridge 分别返回 `Busy` 或 `Locked` 并保留可诊断的安全摘要

#### Scenario: 未知提供程序错误
- **WHEN** 具体错误无法映射到已知分类
- **THEN** Bridge 返回 `Unknown`，诊断不得包含 SQL 参数中的敏感值

### Requirement: SQLite 实现由宿主提供
插件使用 `storage.sqlite/v1` 时 MUST NOT 需要携带 SQLite 托管程序集或原生动态库。宿主 SHALL 提供唯一受控的 SQLite 实现并阻止插件通过 Bridge 注入或替换提供程序。

#### Scenario: 插件包仅包含业务程序集和负载
- **WHEN** 使用 SQLite Bridge 的插件被构建和发布
- **THEN** 插件目录不包含 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*` 或平台 `e_sqlite3` 文件仍可正常访问数据库

#### Scenario: 插件携带自有 SQLite 实现
- **WHEN** 插件包尝试以 Bridge 依赖方式覆盖宿主 SQLite 提供程序
- **THEN** Runtime 不使用该实现且返回安全诊断

### Requirement: Logging Bridge 提供结构化安全日志
`logging/v1` SHALL 接受日志级别、消息模板和受限结构化字段，由宿主映射到统一日志管线。插件 MUST NOT 记录 API Key、Authorization、自定义 Header 值、请求或响应正文、用户 Prompt、图片及工具调用参数，也不得逐 token 或逐流式 chunk 高频记录。

#### Scenario: 记录结构化业务事件
- **WHEN** 插件记录包含插件 id、事件名、状态码和耗时的完成事件
- **THEN** 宿主为该插件加上安全作用域并写入统一日志管线

#### Scenario: 日志字段包含敏感值
- **WHEN** 日志调用包含被策略识别为凭据或正文的字段
- **THEN** 宿主拒绝或净化该字段并记录不含原值的安全诊断

### Requirement: Notifications Bridge 提供宿主管控的即时反馈
`notifications/v1` SHALL 允许插件提交安全摘要和 `Success`、`Info`、`Warning`、`Error` 级别，由宿主映射到统一 Toast 体验。插件不得接触窗口、ViewModel 或导航状态。

#### Scenario: 插件显示成功通知
- **WHEN** 插件提交不含敏感数据的 Success 摘要
- **THEN** 宿主通过现有 Toast 机制显示并按统一生命周期自动隐藏

#### Scenario: 通知包含敏感内容
- **WHEN** 插件提交 API Key、Authorization、请求正文或工具参数
- **THEN** 宿主拒绝显示原文并返回安全错误或净化后的反馈

### Requirement: Bridge 调用按插件隔离
宿主 SHALL 将每个 Bridge 绑定到不可变插件 id，并以该身份执行能力授权、数据路径、日志作用域和通知来源归属。插件不得通过调用参数冒充另一插件。

#### Scenario: 插件尝试指定其他插件 id
- **WHEN** 插件在数据库路径、日志字段或通知来源中尝试覆盖绑定身份
- **THEN** 宿主仍使用初始化时绑定的插件 id，并拒绝越权目标

#### Scenario: 单插件 Bridge 故障
- **WHEN** 一个插件的 Bridge 调用失败
- **THEN** 故障按该调用和既有 Extension 失败策略处理，不破坏其他插件的 Bridge 实例

### Requirement: Credential Protection 原 Vault 保持兼容
Credential Protection 迁移到 SQLite Bridge 后 MUST 继续使用既有 `credential-tokens.db`、`CredentialTokens` 表和 DPAPI 密文，不得创建第二个 Vault、重建数据或使既有 placeholder 失效。

#### Scenario: 升级后解析历史 placeholder
- **WHEN** 用户升级前已存在 token 映射且升级后请求恢复历史 placeholder
- **THEN** Credential Protection 通过 Bridge 精确读取原记录并恢复相同 DPAPI 保护值

#### Scenario: 初始化已有 Vault
- **WHEN** `credential-tokens.db` 已存在并包含既有表结构
- **THEN** 插件直接复用该数据库，不执行数据迁移或创建另一个数据库文件

#### Scenario: 唯一约束冲突保持原语义
- **WHEN** 并发创建映射触发 Token 或 OriginalHash 唯一约束冲突
- **THEN** 插件通过 `ConstraintViolation` 识别冲突、结束失败事务并重新精确查询既有映射

#### Scenario: Vault 访问失败时安全失败
- **WHEN** 数据库损坏、无法解密或 Bridge 返回不可恢复错误
- **THEN** Credential Protection 保持既有 fail closed 行为，不放行可能含明文凭据的数据
### Requirement: 事务归属与实例生命周期明确
数据库句柄 SHALL 区分非事务命令与活动事务命令，不得将非事务调用隐式加入其他调用的活动事务。句柄在其所属实例实际退休并释放后 MUST 拒绝复用；普通禁用 MUST NOT 提前销毁 Credential Protection 的历史解析资源。

#### Scenario: 非事务命令遇到活动事务
- **WHEN** 同一数据库句柄上存在未结束事务且其他调用提交非事务命令
- **THEN** Bridge 不让该命令混入活动事务，按受控串行或稳定错误处理

#### Scenario: 已退休句柄被复用
- **WHEN** 插件旧实例已排空在途调用并释放而代码继续使用其数据库或事务句柄
- **THEN** Bridge 拒绝调用且不访问或重建数据库

#### Scenario: 暂停主动保护后解析历史引用
- **WHEN** 用户普通禁用 Credential Protection 而历史 placeholder 仍需解析
- **THEN** 历史解析所需的 Bridge 保持可用，且不与实际实例退休混淆

```

## openspec/changes/add-source-plugin-runtime-and-bridge/specs/source-plugin-runtime/spec.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/source-plugin-runtime/spec.md
- Lines: 1-140
- SHA256: 165efc33000034a7fd660244bdfef50b30a9afd6968cd1b195c2f0bd7aaa3930

```md
# Spec Delta

## Purpose

定义 LoomX 如何发现、验证、编译、缓存、诊断和重新加载由 Manifest 描述的可信 C# 源码插件，使插件无需预编译项目或本机 .NET SDK 即可复用现有插件运行时契约。

## ADDED Requirements

### Requirement: Manifest 声明源码插件 Runtime
源码插件 Manifest SHALL 以版本化 Runtime 声明标识 `csharp-source`、插件入口类型和非空的显式源码相对路径列表。源码收集 MUST 仅以 Manifest 为准，不得依赖 `.csproj` 或隐式目录扫描。

#### Scenario: 有效源码插件声明
- **WHEN** Manifest 声明 `csharp-source`、有效入口类型和多个存在的 `.cs` 相对路径
- **THEN** Runtime 将声明的文件作为该插件本次编译的完整源码集合

#### Scenario: 缺少源码 Runtime 必备字段
- **WHEN** 源码插件缺少入口类型、sources 为空或包含重复路径
- **THEN** Runtime 在编译前拒绝该插件并返回对应 Manifest 字段的结构化诊断

#### Scenario: 不存在的隐式源码不参与编译
- **WHEN** 插件目录含有未列入 sources 的 `.cs` 文件
- **THEN** Runtime 不读取也不编译该文件

### Requirement: 源码路径限制在插件包内
Runtime MUST 将每个源码路径按插件包根目录规范化，并拒绝绝对路径、UNC 路径、设备路径、盘符相对路径、目录穿越以及经 reparse point 或符号链接逃逸插件根目录的路径。

#### Scenario: 合法子目录源码被读取
- **WHEN** sources 包含 `src/Models.cs` 且规范化结果仍位于插件包根目录内
- **THEN** Runtime 读取该文件并保留 Manifest 中的相对路径用于诊断和缓存

#### Scenario: 目录穿越被拒绝
- **WHEN** sources 包含 `../shared/Secret.cs` 或规范化后位于其他插件目录的路径
- **THEN** Runtime 拒绝加载该插件且不读取目标文件

#### Scenario: 链接导致的目录逃逸被拒绝
- **WHEN** 声明路径表面位于插件目录内但最终解析到插件目录之外
- **THEN** Runtime 拒绝该源码路径并返回安全诊断

### Requirement: 宿主内置确定性源码编译
Runtime SHALL 使用宿主随附的编译能力将声明源码编译为可加载程序集，用户环境 MUST NOT 需要安装 .NET SDK。编译引用 MUST 限于宿主允许的框架程序集、Plugin API 及明确允许的共享契约。

#### Scenario: 未安装 SDK 时编译成功
- **WHEN** 用户机器未安装 .NET SDK 且源码只使用允许的引用
- **THEN** Runtime 仍能完成编译并生成可加载程序集及调试符号

#### Scenario: 源码请求未允许的依赖
- **WHEN** 源码引用未由宿主提供的第三方程序集
- **THEN** Runtime 返回编译诊断且不执行 NuGet 或其他网络依赖恢复

#### Scenario: 预编译插件保持兼容
- **WHEN** Manifest 继续声明现有预编译程序集 Runtime
- **THEN** Runtime 按既有程序集加载流程处理，不要求该插件改为源码形式

### Requirement: 内容寻址编译缓存
Runtime SHALL 以 Manifest、源码相对路径及内容、Plugin API 版本、编译器版本和生效编译选项共同生成缓存身份，并在身份相同且缓存完整时复用编译产物。

#### Scenario: 未变化源码命中缓存
- **WHEN** 同一插件的缓存身份与既有完整产物一致
- **THEN** Runtime 直接复用对应程序集和调试符号而不重复编译

#### Scenario: 任一编译输入变化导致缓存失效
- **WHEN** 源码内容、路径、Manifest、Plugin API 版本、编译器版本或编译选项任一发生变化
- **THEN** Runtime 生成不同缓存身份并重新编译

#### Scenario: 不完整缓存不被加载
- **WHEN** 缓存目录缺失程序集、调试符号或完整性元数据
- **THEN** Runtime 将该缓存视为无效并重新编译，不加载部分产物

### Requirement: 编译失败不破坏可用版本
源码编译 SHALL 先写入隔离的候选缓存，只有完整编译成功后才能原子发布。失败候选 MUST NOT 覆盖最近一次可用缓存或当前已加载的插件实例。

#### Scenario: 首次编译失败
- **WHEN** 插件不存在可用缓存且源码编译失败
- **THEN** Runtime 拒绝加载该插件并保留其他插件正常运行

#### Scenario: 更新源码编译失败
- **WHEN** 已加载插件的新源码版本编译失败
- **THEN** Runtime 保持当前实例运行并保留最近一次可用缓存，同时报告本次更新失败

#### Scenario: 更新源码编译成功
- **WHEN** 新源码版本编译成功并通过加载前验证
- **THEN** Runtime 发布新缓存并在重新加载边界用新实例替换旧实例

### Requirement: 提供结构化安全编译诊断
编译与加载诊断 SHALL 至少包含插件 id、阶段、严重级别、错误码、安全消息，以及适用时的源码相对路径、行和列。诊断 MUST NOT 包含插件数据、凭据、请求正文或规范化后的外部绝对路径。

#### Scenario: 语法错误可定位
- **WHEN** 声明源码存在语法错误
- **THEN** Runtime 返回含相对文件路径、行、列和编译器错误码的诊断

#### Scenario: 非源码错误仍可分类
- **WHEN** 失败发生在 Manifest 验证、缓存校验或入口类型加载阶段
- **THEN** Runtime 返回对应阶段和稳定错误码，不伪造源码位置

### Requirement: 源码插件遵守既有插件契约
编译后的源码插件 MUST 通过与预编译插件相同的共享契约、Manifest 一致性检查、Extension 注册、Pipeline 顺序、失败策略、启停和 UI Contribution 验证流程，不得获得额外宿主实现访问权。

#### Scenario: 入口类型不实现插件契约
- **WHEN** 编译成功但入口类型未实现当前 Plugin API 要求的插件接口
- **THEN** Runtime 拒绝实例化该插件并返回契约不匹配诊断

#### Scenario: 源码插件注册 Extension
- **WHEN** 有效源码插件创建 Request 和 Response Extension
- **THEN** 两个 Extension 按现有 Manifest 声明和 Pipeline 配置进入对应 Pipeline

### Requirement: 源码插件被视为可信代码而非安全沙箱
产品和开发工具 MUST 明确披露源码插件会在 LoomX 本地进程权限边界内执行，`AssemblyLoadContext` 仅用于加载与卸载，不构成恶意代码隔离。

#### Scenario: 安装或启用源码插件
- **WHEN** 用户首次安装或启用一个源码插件
- **THEN** 产品向用户展示可信代码边界说明，不宣称该插件已被沙箱隔离
### Requirement: 编译引用与本地化负载参与缓存完整性
源码插件发布与缓存 SHALL 包含宿主固定提供的编译引用集合及实际进入产物的本地化负载身份。缺少必要引用时 MUST 返回安全诊断，不得搜索本机 SDK 或在线恢复。缓存负载 MUST 遵守既有插件目录布局与文案查找协议。

#### Scenario: 引用或文案更新使缓存失效
- **WHEN** 实际编译引用摘要或插件 i18n 内容发生变化
- **THEN** Runtime 不复用旧缓存，并使新实例使用更新后的引用与文案

#### Scenario: 发布包缺少编译引用
- **WHEN** 随附引用集合缺失或完整性校验失败
- **THEN** Runtime 返回安全的缓存或编译诊断，不搜索外部 SDK 或发起依赖恢复

#### Scenario: 从缓存加载插件本地化
- **WHEN** 源码程序集从内容寻址缓存加载
- **THEN** 缓存仍提供 plugins/<id>/plugin.manifest.json 与 i18n/<culture>.json 的一致负载布局，且无需增加 PluginTextCatalog 查找特判

### Requirement: 重新加载保持在途请求代际连续性
Runtime SHALL 在候选实例验证成功后切换不可变注册快照。完整 Provider 调用的 request/response 及流式处理 MUST 使用同一已取得代际，旧代际的 Bridge 资源 MUST 在在途调用结束后才释放。

#### Scenario: 新请求使用新代际
- **WHEN** 候选插件已成功切换且新的 Provider 调用开始
- **THEN** 新调用使用新代际的成对 request/response Pipeline，不继续使用组合根启动时固定持有的旧引用

#### Scenario: 旧 SSE 调用仍在途
- **WHEN** reload 成功而旧调用的 SSE 正文尚未结束或释放
- **THEN** 旧调用继续使用原代际，所需 Bridge 不被提前释放

#### Scenario: 候选初始化失败
- **WHEN** 新候选编译成功但初始化或注册验证失败
- **THEN** Runtime 不切换当前实例，只释放本次候选资源并返回安全诊断

```
