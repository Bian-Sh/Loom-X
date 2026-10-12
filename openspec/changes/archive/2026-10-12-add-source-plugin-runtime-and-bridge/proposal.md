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
