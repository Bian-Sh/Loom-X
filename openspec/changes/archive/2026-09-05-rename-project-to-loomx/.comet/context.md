# Comet Design Handoff

- Change: rename-project-to-loomx
- Phase: design
- Mode: compact
- Context hash: bb954d4dd3ea3b66e43e0860fb94031aeffc51187af9fe086457152c081e08ad

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/rename-project-to-loomx/proposal.md

- Source: openspec/changes/rename-project-to-loomx/proposal.md
- Lines: 1-32
- SHA256: 91fbbd4fcaeb87f7470011f026742fd6cf2be4a610b1ab6a065ac4cab45576c2

```md
## Why

项目当前产品名、源码标识、发布入口和本地数据目录仍使用 OllamaHub，且项目已经迁移为单一桌面应用，历史上的 `Desktop` 后缀不再表达实际结构。现在统一更名为 Loom-x/LoomX，可以消除产品、代码和发布包之间的不一致，同时让新安装使用明确的 LoomX 数据目录。

## What Changes

- 将产品显示名统一为 `Loom-x`，将 C# namespace、项目、程序集和解决方案统一为 `LoomX`。
- 将桌面项目、测试项目、源码目录、Avalonia 资源 URI、运行时互斥标识、环境变量和启动参数改为 LoomX 标识。
- 将发布入口从旧名称改为 `LoomX.exe`，同步更新发布脚本、README、诊断信息和设置页链接。
- 将配置数据库从 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.db`。
- 将活动数据库迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.Activity.db`，日志迁移到 `%LOCALAPPDATA%\\LoomX\\logs`。
- 新增幂等的 SQLite 数据迁移流程，保留旧目录作为回滚备份；迁移失败时禁止静默创建空库。
- 保持 `/api/tags`、`/v1/chat/completions` 等 HTTP 路径以及请求/响应结构不变。
- **BREAKING**：不再提供 `OllamaHub.*` namespace、旧程序集或旧 exe 兼容层。

## Capabilities

### New Capabilities

- `project-identity`: 定义 Loom-x 产品名、LoomX 技术标识、运行时标识和唯一发布入口。
- `app-data-migration`: 定义 OllamaHub 本地数据库向 LoomX 新路径的安全、可重试迁移行为。

### Modified Capabilities

无。当前 `openspec/specs/` 中没有覆盖项目身份或应用数据迁移的现有主规格。

## Impact

- 影响桌面项目、测试项目、解决方案文件、C# namespace、Avalonia XAML、程序集资源 URI、启动策略、日志和发布脚本。
- 影响 `%LOCALAPPDATA%` 下配置库、活动库、日志和初始化锁的路径解析。
- 需要新增 SQLite 迁移组件和迁移测试，并更新所有静态源码路径断言。
- HTTP 路由、Provider/Model 配置模型、DPAPI 密钥内容和现有客户端调用方式保持不变。

```

## openspec/changes/rename-project-to-loomx/design.md

- Source: openspec/changes/rename-project-to-loomx/design.md
- Lines: 1-75
- SHA256: da5110f053a5643ff257756f0b284420d60e09318511e1ef941ba26857440e47

```md
## Context

当前仓库已经是单一 Avalonia 桌面应用，旧项目名 `OllamaHub.Desktop` 来源于历史上的独立 CLI/网关工程。源码、程序集、XAML 资源 URI、运行时互斥标识、发布脚本和用户界面仍然混用 OllamaHub 与 Desktop 标识；运行时配置则固定存储在 `%LOCALAPPDATA%\\OllamaHub`。

本变更需要同时处理名称重构和持久化数据迁移。约束包括：C# 标识不能包含连字符；HTTP 路由和协议结构必须继续兼容；SQLite 数据可能处于 WAL 模式；DPAPI 密文必须在同一 Windows 用户下继续可解密；旧数据目录不能被删除或覆盖。

## Goals / Non-Goals

**Goals:**

- 统一产品显示名为 `Loom-x`，技术标识为 `LoomX`。
- 将单一桌面项目、测试项目、解决方案和发布入口改为 `LoomX`/`LoomX.Tests`/`LoomX.slnx`/`LoomX.exe`。
- 将所有正常运行时数据迁移到 `%LOCALAPPDATA%\\LoomX`，并安全迁移旧配置库和活动库。
- 保持现有 HTTP 路由、请求/响应结构、Provider/Model 配置语义和 DPAPI 密钥可用。
- 为迁移、名称契约、构建和发布补齐可重复的自动化验证。

**Non-Goals:**

- 不修改 `/api/tags`、`/v1/chat/completions` 等协议路径。
- 不保留旧 namespace、程序集或 exe 兼容层。
- 不删除、移动或重写既有 `.codegraph`、`graphify-out`、`outputs` 等流程产物。
- 不把旧日志内容复制到新目录；旧目录保留，LoomX 从新目录开始写日志。

## Decisions

### 产品名与技术标识分离

显示文本使用 `Loom-x`，C# namespace、程序集、项目文件和机器可读标识使用 `LoomX`/`loomx`。这是因为连字符不能出现在 C# 标识符中，同时保留品牌视觉形式。物理目录和项目名直接去掉历史 `Desktop` 后缀，不引入新的分层程序集。

备选方案是保留 `LoomX.Desktop` 或同时提供旧 namespace 包装层。前者继续暴露已经失真的架构信息，后者会增加重复维护和资源 URI 复杂度，因此不采用。

### 使用物理重命名而不是别名映射

通过 `git mv` 重命名解决方案、项目目录、项目文件、测试目录和网关宿主源文件，并同步修改 namespace、资源 URI、程序集属性、静态源码路径和脚本。这样 Git 历史保持可追踪，构建产物和源码结构一次收敛。

备选方案是只修改显示名或保留旧文件名。该方案无法满足发布入口、程序集和数据目录统一的目标，因此不采用。

### 采用 SQLite 备份 API 完成数据库迁移

新增独立迁移组件，在 `LoomXHost` 创建数据库连接前执行。迁移组件获得新目录锁后，针对配置库和活动库分别执行：源库存在且目标库不存在时，通过 SQLite 备份能力写入临时目标，执行完整性/必要表检查，再原子移动为正式文件。迁移组件接受显式源路径和目标路径的内部测试参数，但生产路径始终由 `AppDataPaths` 固定解析。

备选方案是直接复制 `.db` 文件或先读出业务实体再重建。直接复制可能遗漏 WAL/SHM 内容，实体重建又可能丢失未知字段和 DPAPI 密文，因此不采用。

### 新旧库冲突时以新库为准

如果新库已经存在，即使旧库仍存在，也不覆盖新库、不重复迁移。旧目录始终保留。这样可以避免用户已经运行 LoomX 后被旧版本数据回写；回滚时仍可使用未修改的旧目录。

### 协议兼容与品牌更新并行

HTTP 路径和协议结构不变；根健康响应的 `name` 更新为 `Loom-x`，模型列表的 `owned_by` 更新为 `loomx`。这两个字段属于身份信息，不参与路由或配置解析。客户端依赖的 Endpoint、请求格式和响应字段保持不变。

### 验证优先于发布切换

先完成名称契约、迁移单元测试和静态路径断言，再执行完整解决方案测试、构建和发布。发布脚本严格校验唯一应用入口为 `LoomX.exe`，发布目录使用带时间戳的 `outputs` 子目录。桌面发布包通过窗口级 CUA 检查启动、单实例、网关和迁移结果。

## Risks / Trade-offs

- [SQLite 源库处于 WAL 模式] → 使用 SQLite 备份 API 和临时目标，不直接复制主文件，并在成功提交前执行完整性检查。
- [迁移过程中进程中断] → 只写临时目标并在最后原子移动；下次启动可清理临时文件并重试，旧库不被修改。
- [新旧库同时存在导致用户困惑] → 明确新库优先规则，记录安全摘要日志，并在诊断信息中展示当前 LoomX 数据目录。
- [全量 namespace/程序集改名造成构建联动] → 以 `git mv` 配合全仓活动源码搜索，逐步更新项目引用、XAML URI 和测试路径。
- [旧版本回滚后读取新版本数据] → 不尝试让旧版本读取 LoomX 目录；保留未修改的 OllamaHub 目录作为旧版本回滚数据源。

## Migration Plan

1. 创建本 change 的 OpenSpec 产物和实现计划，确认工作区隔离、测试方式和审查方式。
2. 同步远程并确认工作区无无关改动后，使用 `git mv` 完成文件和目录重命名。
3. 更新 namespace、程序集/资源标识、运行时标识、UI、协议品牌值、文档和发布脚本。
4. 实现并接入应用数据迁移组件，补齐配置库、活动库、WAL、冲突和失败安全测试。
5. 运行测试、构建、发布和窗口级验证；确认新安装和旧库升级两条路径。
6. 发布 LoomX 后保留旧版本发布包及 `%LOCALAPPDATA%\\OllamaHub` 目录。若迁移或启动失败，停止 LoomX，使用旧版本读取旧目录，修复后重新尝试迁移。

## Open Questions

无。名称、数据路径、迁移失败策略、协议兼容边界和发布入口均已确认。

```

## openspec/changes/rename-project-to-loomx/tasks.md

- Source: openspec/changes/rename-project-to-loomx/tasks.md
- Lines: 1-34
- SHA256: aa668dbe2df2bb8cf4a321399bd578c447a1ef9cb3cd95ef5a8265e91c693d20

```md
## 1. 结构与技术标识改名

- [ ] 1.1 使用 `git mv` 将 `OllamaHub.slnx`、`OllamaHub.Desktop/`、`OllamaHub.Tests/`、项目文件和 `OllamaHubHost.cs` 重命名为 LoomX 对应名称，并保留既有流程产物
- [ ] 1.2 更新解决方案项目路径、项目引用、程序集属性和 `InternalsVisibleTo`，使 `LoomX.slnx` 仅引用 `LoomX` 与 `LoomX.Tests`
- [ ] 1.3 将活动源码和测试中的 `OllamaHub.*` namespace、`using`、类型名、Avalonia `x:Class`、`using:` 声明和 `avares://` URI 改为 `LoomX.*`
- [ ] 1.4 更新静态源码路径断言、测试临时目录名称和所有构建/测试命令引用，确保测试不依赖旧目录

## 2. 运行时身份与用户界面

- [ ] 2.1 将窗口标题、导航品牌、设置页、诊断摘要、日志模板和启动提示统一为 `Loom-x`
- [ ] 2.2 将单实例互斥锁、Shell 引导互斥锁、环境变量、启动参数和临时快捷方式名称统一为 LoomX 标识
- [ ] 2.3 更新网关根响应的产品名、模型列表 `owned_by`、设置页项目主页/问题链接和 `app.manifest` 程序集身份
- [ ] 2.4 保持既有 HTTP 路由、请求/响应结构和 Provider/Model 配置语义不变，并补齐相关契约断言

## 3. 应用数据迁移

- [ ] 3.1 更新 `AppDataPaths`，定义 `%LOCALAPPDATA%\\LoomX`、`LoomX.db`、`LoomX.Activity.db`、新日志目录和初始化锁路径，并保留旧路径只读常量
- [ ] 3.2 实现幂等应用数据迁移组件，使用迁移锁、SQLite 备份 API、临时文件、完整性检查和原子提交
- [ ] 3.3 在 `LoomXHost` 创建数据库连接前接入迁移，处理新库优先、旧库保留、无旧库初始化和失败阻止启动规则
- [ ] 3.4 为配置库、活动库、WAL、重复启动、目标已存在、源库损坏、完整性失败和 DPAPI 密文保留新增测试入口与测试用例

## 4. 文档与发布脚本

- [ ] 4.1 更新 README、`AGENTS.md`、升级说明和相关设计引用中的产品名、路径、命令、日志名和发布入口
- [ ] 4.2 更新 `scripts/publish-desktop.ps1`，发布 LoomX 项目并严格校验唯一应用入口为 `LoomX.exe`
- [ ] 4.3 在活动源码、项目文件、脚本、用户文档和测试范围内搜索旧名称，确认仅保留迁移兼容代码、迁移测试和升级说明

## 5. 验证与交付

- [ ] 5.1 运行 `dotnet test LoomX.slnx`，修复所有编译或测试失败
- [ ] 5.2 运行 `dotnet build LoomX.slnx`，确认桌面应用和测试程序集完整构建
- [ ] 5.3 运行发布脚本输出到带时间戳的 `outputs` 目录，确认发布包包含 `LoomX.exe` 且不包含旧应用入口
- [ ] 5.4 使用发布包验证新安装、旧库迁移、单实例、网关健康检查、配置读取、活动读取和 Loom-x UI 品牌
- [ ] 5.5 汇总验证结果，更新 Comet/OpenSpec 状态并准备提交、推送和后续归档

```

## openspec/changes/rename-project-to-loomx/specs/app-data-migration/spec.md

- Source: openspec/changes/rename-project-to-loomx/specs/app-data-migration/spec.md
- Lines: 1-71
- SHA256: e270a3d80be4c6f6c31cd6e5bc02def5292f47d82af77176a5ea33215a415791

```md
## ADDED Requirements

### Requirement: LoomX runtime data paths

正常运行时 SHALL 只从 `AppDataPaths` 解析并使用以下路径：根目录 `%LOCALAPPDATA%\\LoomX`、配置库 `LoomX.db`、活动库 `LoomX.Activity.db`、日志目录 `logs` 和配置库初始化锁 `LoomX.db.init.lock`。正常运行时不得使用应用目录或当前工作目录创建数据库。

#### Scenario: New installation

- **WHEN** LoomX 在没有旧数据目录的用户环境中首次启动
- **THEN** 应用创建 `%LOCALAPPDATA%\\LoomX` 及其新数据库和日志目录，不创建新的 `%LOCALAPPDATA%\\OllamaHub` 数据库

### Requirement: Legacy database migration

当新配置库不存在且旧配置库 `%LOCALAPPDATA%\\OllamaHub\\OllamaHub.db` 存在时，系统 SHALL 将配置库迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.db`。当旧活动库存在时，系统 SHALL 将其迁移到 `%LOCALAPPDATA%\\LoomX\\LoomX.Activity.db`。迁移 SHALL 在 LoomX 创建数据库连接前完成。

#### Scenario: First launch with legacy data

- **WHEN** 用户首次启动 LoomX 且旧配置库和活动库存在
- **THEN** LoomX 在新路径下保留配置、活动记录和 DPAPI 密文，并使用新路径继续运行

#### Scenario: Legacy configuration only

- **WHEN** 旧配置库存在但旧活动库不存在
- **THEN** LoomX 迁移配置库并按正常初始化流程创建新的空活动库

### Requirement: Safe SQLite copy and integrity validation

迁移 SHALL 使用 SQLite `VACUUM INTO` 在源库上创建一致性快照，支持 WAL/SHM 状态； SHALL 先写入临时目标并完成 SQLite 完整性及必要表检查，再原子提交正式目标文件。

#### Scenario: WAL database migration

- **WHEN** 旧数据库存在尚未合并到主文件的 WAL 数据
- **THEN** 迁移后的新数据库包含已提交数据，且完整性检查通过后才成为正式数据库

#### Scenario: Interrupted migration

- **WHEN** 迁移在临时文件阶段中断
- **THEN** 新路径不出现不完整的正式数据库，旧数据库保持可读，后续启动可以清理临时文件并重试

#### Scenario: Activity database retry after configuration success

- **WHEN** 配置库迁移已成功但活动库迁移失败
- **THEN** LoomX 保留已验证的配置库、阻止应用启动，并在下次启动只重试活动库迁移

### Requirement: Idempotent conflict handling

迁移 SHALL 是幂等的。当新数据库已经存在时，系统 SHALL 以新数据库为准，不覆盖、不重复导入，也不得使用旧库回写新库。

#### Scenario: Relaunch after migration

- **WHEN** LoomX 已完成迁移并再次启动，且旧目录仍然存在
- **THEN** LoomX 直接使用新数据库，不再次复制或改变新数据库内容

#### Scenario: New database already exists

- **WHEN** 新数据库存在而旧数据库也存在
- **THEN** 新数据库保持不变，旧目录继续作为备份保留

### Requirement: Failure safety and legacy retention

源库损坏、备份失败、完整性检查失败或正式文件提交失败时，系统 SHALL 删除未完成的临时目标、记录安全摘要错误并阻止继续启动；不得静默创建空配置库。迁移成功或失败后均 SHALL 保留旧 `%LOCALAPPDATA%\\OllamaHub` 目录，不删除或覆盖源库。

#### Scenario: Corrupt legacy database

- **WHEN** LoomX 检测到旧配置库无法通过 SQLite 完整性检查
- **THEN** LoomX 不创建可被误认为有效的空配置库，启动失败并保留旧文件供修复

#### Scenario: Successful migration keeps source

- **WHEN** 配置库和活动库迁移成功
- **THEN** 新数据库可正常读取，旧目录和源数据库仍然存在且未被覆盖

```

## openspec/changes/rename-project-to-loomx/specs/project-identity/spec.md

- Source: openspec/changes/rename-project-to-loomx/specs/project-identity/spec.md
- Lines: 1-49
- SHA256: 5886eca88ef6d1cf76b58d31d9e926f696d855d10b4da5da9f3fdd5b836b9583

```md
## ADDED Requirements

### Requirement: LoomX technical identity

项目 SHALL 使用 `LoomX` 作为 C# 根命名空间、桌面项目和程序集名称，使用 `LoomX.Tests` 作为测试项目和程序集名称，并使用 `LoomX.slnx` 作为解决方案名称。桌面发布包 SHALL 只提供 `LoomX.exe` 作为应用入口。

#### Scenario: Solution and project build

- **WHEN** 使用 `dotnet build LoomX.slnx` 构建解决方案
- **THEN** 构建系统解析 LoomX 和 LoomX.Tests 项目，且不依赖旧 `OllamaHub.Desktop` 或 `OllamaHub.Tests` 项目路径

#### Scenario: Published application entry

- **WHEN** 使用发布脚本发布 Windows 桌面应用
- **THEN** 发布目录包含唯一的应用入口 `LoomX.exe`，且不生成旧名称的应用入口

#### Scenario: Avalonia resource resolution

- **WHEN** LoomX 启动并加载 XAML 资源
- **THEN** `x:Class`、`using` 和 `avares://` URI 均解析到 `LoomX` 程序集和 namespace

### Requirement: Loom-x product branding

面向用户的窗口标题、导航品牌、诊断摘要、README 和发布说明 SHALL 使用 `Loom-x`。健康检查根响应的产品名 SHALL 为 `Loom-x`，OpenAI 模型列表的 `owned_by` SHALL 为 `loomx`。

#### Scenario: Desktop branding

- **WHEN** 用户打开 LoomX 桌面应用或查看设置页
- **THEN** 窗口标题、品牌文本和诊断摘要显示 `Loom-x`，不显示旧产品名

#### Scenario: Gateway identity

- **WHEN** 客户端请求健康检查根路径或 OpenAI 模型列表
- **THEN** 根响应的 `name` 为 `Loom-x`，模型条目的 `owned_by` 为 `loomx`

### Requirement: Protocol route compatibility

LoomX SHALL 保持现有 HTTP 路由、请求/响应结构和 Provider/Model 配置语义不变；名称改动不得改变 `/api/tags`、`/v1/chat/completions` 和其他现有兼容入口的路径。

#### Scenario: Existing Ollama route

- **WHEN** 客户端请求 `/api/tags`
- **THEN** LoomX 使用原有路由和模型列表结构返回结果

#### Scenario: Existing OpenAI route

- **WHEN** 客户端向 `/v1/chat/completions` 发送原有格式的请求
- **THEN** LoomX 按原有配置和响应约定处理请求


```
