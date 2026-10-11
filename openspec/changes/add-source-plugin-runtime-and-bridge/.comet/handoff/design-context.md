# Comet Design Handoff

- Change: add-source-plugin-runtime-and-bridge
- Phase: design
- Mode: compact
- Context hash: 88f4b849dc800dda2dcb5015d9e7674bf6d7f54c9172013368c01eb2f5799c55

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
- Lines: 1-246
- SHA256: 4da4ea58b160cfeedd970f2e71d70eae486335bb2f0ba54d19216208eb3eb106

[TRUNCATED]

```md
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
- `LoomX.Plugin.Storage.Sqlite.dll`：宿主侧 `storage.sqlite/v1` 实现；它是唯一直接引用 `Microsoft.Data.Sqlite` 与 `SQLitePCLRaw` 的插件基础设施程序集。
- `LoomX`：组合根；把统一 `ILogger` 管线和现有 `ToastService` 适配到 Logging/Notifications Bridge。


```

Full source: openspec/changes/add-source-plugin-runtime-and-bridge/design.md

## openspec/changes/add-source-plugin-runtime-and-bridge/tasks.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/tasks.md
- Lines: 1-69
- SHA256: 3b98480920cbf9417abc2fc3ea40d7517012370935ef987f1dd1765d6236c1ed

```md
# Tasks

## 1. 公共契约与 Manifest

- [ ] 1.1 在 `LoomX.Plugin.Abstractions` 增加 Runtime 联合模型、`host_capabilities`、`PluginDiagnostic` 及稳定错误码，并通过契约序列化测试验证字段可确定性往返且不含宿主私有类型。
- [ ] 1.2 定义 `IPluginBridge`、Storage/Logging/Notifications 接口及封闭值 DTO，增加反射契约测试，验证公共签名不出现 `object`、`Stream`、delegate、`DbConnection`、`DbTransaction`、`IServiceProvider` 或宿主程序集类型。
- [ ] 1.3 扩展 Manifest 解析与验证以支持 `assembly`、`csharp-source` 和旧顶层程序集字段，测试有效声明、双重 Runtime、缺失入口、空/重复 sources、非法能力版本及旧插件兼容。
- [ ] 1.4 扩展 `PluginInitializationContext` 以提供 Bridge 且保留现有属性和兼容构造方式，使用既有第一方插件和测试插件验证二进制入口及初始化行为未回归。
- [ ] 1.5 实现 host capability 精确协商与未声明能力拒绝代理，测试不兼容能力在执行插件代码前被拒绝、未声明调用返回 `CapabilityNotDeclared`、不同插件授权互不影响。

## 2. 宿主 SQLite Bridge

- [ ] 2.1 新建宿主侧 `LoomX.Plugin.Storage.Sqlite` 项目并把 `Microsoft.Data.Sqlite`/`SQLitePCLRaw` 依赖集中于该项目，构建测试验证 Plugin Abstractions 和普通插件项目不传递这些依赖。
- [ ] 2.2 实现与 plugin id 绑定的数据库路径解析和目录创建，覆盖相对嵌套路径、绝对路径、UNC、设备路径、盘符相对路径、`..`、空路径及大小写边界测试。
- [ ] 2.3 增加 Windows reparse point/符号链接逃逸检查并在测试临时目录验证现有父目录链接、创建前后竞态校验和跨插件路径均返回 `InvalidPath`。
- [ ] 2.4 实现 SQLite 打开、单语句参数化 Execute、QuerySingle、Query 和 Null/Integer/Real/Text/Blob 映射，使用 CRUD、DDL、索引、Blob、空结果、重复列名和资源上限测试验证行为。
- [ ] 2.5 实现显式事务、提交、回滚、释放回滚、完成后拒绝复用和取消传播，使用原子写入、失败回滚、并发 busy/locked 与取消测试验证生命周期。
- [ ] 2.6 实现 `PluginSqlException` 错误映射，逐项测试 ConstraintViolation、Busy、Locked、Corrupt、ReadOnly、PermissionDenied、InvalidPath、InvalidSql、Cancelled、ResourceLimit 和 Unknown，确认消息不含参数值或数据库绝对路径。
- [ ] 2.7 关闭 SQLite 扩展加载并增加 authorizer/语句限制，测试 ATTACH、外部路径型 VACUUM 和提供程序替换被拒绝，而普通 PRAGMA、DDL、DML 与查询保持可用。
- [ ] 2.8 把数据库句柄和事务登记到插件 Bridge 生命周期，在插件卸载时关闭资源并回滚未完成事务；通过文件可重新打开、句柄不可复用和其他插件不受影响的测试验证。

## 3. Logging 与 Notifications Bridge

- [ ] 3.1 实现 Logging Bridge 到 `ILogger`/Serilog 的级别、模板、字段和 PluginId 作用域映射，测试结构化字段输出以及敏感键名、凭据形态、超长字段和高频调用的拒绝或净化。
- [ ] 3.2 实现 Notifications Bridge 到现有 `ToastService` 的 Success/Info/Warning/Error 映射，测试自动归属、长度/频率限制、敏感内容拦截以及插件无法控制窗口或导航。
- [ ] 3.3 在宿主组合根为每个插件创建授权后的 Bridge，并用多插件集成测试验证数据库根、日志作用域、通知来源和故障均按插件隔离。

## 4. C# 源码插件运行时

- [ ] 4.1 在 PluginHost 引入随应用发布的 Roslyn 编译能力和固定编译选项，使用没有外部 `dotnet` SDK 参与的测试进程验证最小源码插件可生成 DLL 与 portable PDB。
- [ ] 4.2 实现 Manifest 显式 sources 收集和插件包路径验证，测试未声明 `.cs` 不参与、缺失文件被诊断、目录穿越和 reparse point 逃逸在读取前被拒绝。
- [ ] 4.3 实现受控引用集合与禁止任意 NuGet/插件目录 DLL 解析的规则，测试框架 API 与 Abstractions 可用、未允许第三方引用产生结构化编译错误且无网络恢复行为。
- [ ] 4.4 实现包含规范化 Manifest、源码路径及内容、Plugin API、编译器和选项的缓存键，使用单项输入变化测试验证缓存命中、失效和不同插件隔离。
- [ ] 4.5 实现候选目录、产物摘要、完整性元数据和原子缓存发布，测试残缺/篡改缓存触发重编译、失败编译不覆盖最近可用缓存且临时候选可清理。
- [ ] 4.6 把 Roslyn、缓存、能力、加载与初始化错误统一映射为 `PluginDiagnostic`，测试文件、行、列、错误码与阶段完整，并验证诊断不含源码正文、外部绝对路径或敏感运行数据。
- [ ] 4.7 将成功编译产物接入现有 collectible ALC、Manifest 一致性、Extension/Pipeline 和 UI Contribution 流程，使用同功能的源码/预编译插件对照测试验证行为一致。
- [ ] 4.8 实现“先验证新实例、再原子替换”的 reload，测试成功切换、首次失败拒绝、更新失败保留当前实例和旧 ALC 回收失败仅产生安全诊断。
- [ ] 4.9 在安装/启用源码插件入口增加可信代码边界披露及本地化文案，UI 测试验证不会把 ALC 或 Bridge 表述为安全沙箱。

## 5. Credential Protection 迁移

- [ ] 5.1 为 Credential Protection Manifest 声明所需 Bridge 能力，并把 `CredentialTokenStore` 改为通过 `storage.sqlite/v1` 打开 `credential-tokens.db`；使用新建 Vault 测试验证表名、列名、索引和初始化 SQL 不变。
- [ ] 5.2 将 token 创建、精确查询和批量恢复改为异步 Bridge 调用与插件内异步互斥，测试同一明文稳定复用 token、跨请求恢复、重复 token 去重和取消行为。
- [ ] 5.3 保留“唯一约束冲突后释放失败事务并重新精确查询”的算法，使用并发冲突测试验证不会签出第二个 token 或产生嵌套 placeholder。
- [ ] 5.4 使用由旧实现创建的真实 `credential-tokens.db` 作为兼容夹具，验证升级后无需迁移即可恢复历史 placeholder，DPAPI 前缀、密文和 OriginalHash 保持原样且不创建第二个 Vault。
- [ ] 5.5 覆盖 Bridge Busy/Locked/Corrupt、DPAPI 解密失败与未知 token 场景，验证 Credential Protection 保持原有确定性解析和 fail closed，不把明文发送给 Provider。
- [ ] 5.6 从 Credential Protection csproj 移除 `Microsoft.Data.Sqlite`，更新宿主/测试负载复制并扩展发布契约测试，验证插件目录不存在 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*`、`e_sqlite3`，宿主侧实现仍可运行。

## 6. 插件开发 Skill 与工具闭环

- [ ] 6.1 实现共享插件开发应用服务和 `loomx.plugin.create/validate/compile/test/reload/diagnostics` 工具注册，使用工具契约测试验证稳定名称、机器可读结果和统一 `PluginDiagnostic`。
- [ ] 6.2 实现 create 模板生成器，验证生成目录包含合法 Manifest、显式 sources、入口源码、测试占位和 i18n 负载，且不强制生成 `.csproj`。
- [ ] 6.3 让 validate、compile 和 diagnostics 复用生产 Manifest、能力协商、路径和 Roslyn 服务，使用多错误输入测试验证一次返回可安全收集的问题且无规则分叉。
- [ ] 6.4 实现隔离 test 工具和临时 Bridge，测试默认不连接真实 Provider、不读取生产 Vault、不修改 LoomX 配置，并能验证 Pipeline、声明式 UI 和 SQLite 插件测试。
- [ ] 6.5 让 reload 工具调用生产 Runtime 的原子替换路径，使用正在运行的测试宿主验证成功生效和失败保留旧实例。
- [ ] 6.6 编写版本化插件开发 Skill，覆盖 Manifest Schema、源码模板、Pipeline、声明式 UI、SQLite/Logging/Notifications Bridge、权限边界、安全规则和 AI 修复循环；按 Skill 中步骤生成并加载示例插件验证文档可执行。

## 7. 集成、发布与回归

- [ ] 7.1 扩展 `LoomX.PluginPlayground` 端到端覆盖源码编译、Bridge SQLite CRUD、日志、通知、Pipeline、reload 和 Credential Protection 历史 Vault，并验证全部检查通过。
- [ ] 7.2 运行完整 `dotnet test`，修复所有 Plugin Runtime、Credential Protection、UI、本地化和发布布局回归，并保存通过结果作为 Verify 输入。
- [ ] 7.3 执行 Windows 发布与打包，检查宿主只携带一套 SQLite 实现、各插件负载目录无 SQLite 动态链、源码插件示例布局正确，并将可读时间命名的成品放入 `outputs/`。
- [ ] 7.4 在真实发布包中执行无 SDK 源码插件创建→编译→诊断修复→测试→加载→reload 流程，并验证重启后缓存命中、插件数据仍位于 `%LOCALAPPDATA%\LoomX\plugins\<plugin-id>\`。
- [ ] 7.5 使用现有 Credential Protection 端到端请求/响应用例复验 placeholder 完整性指令、跨 chunk/SSE 恢复、未知或损坏 token fail closed、禁用与卸载生命周期均未改变。

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
- Lines: 1-99
- SHA256: 0ce1cb21e6d770b56f2bd00dbec7d6b1a07d107d721ebcf406acac21600d7dba

[TRUNCATED]

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

```

Full source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-development-skill/spec.md

## openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-host-bridge/spec.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-host-bridge/spec.md
- Lines: 1-182
- SHA256: 54dddc327a1e3de9fc4dc43b62aca9e5cc79457df23743a3c4e809f0248ce4a0

[TRUNCATED]

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

```

Full source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/plugin-host-bridge/spec.md

## openspec/changes/add-source-plugin-runtime-and-bridge/specs/source-plugin-runtime/spec.md

- Source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/source-plugin-runtime/spec.md
- Lines: 1-111
- SHA256: 1bf93b711440f1987d8b5e04d2d0334840f8f2d3c6f841836eaba8252e32d97b

[TRUNCATED]

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

```

Full source: openspec/changes/add-source-plugin-runtime-and-bridge/specs/source-plugin-runtime/spec.md
