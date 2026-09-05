# Comet Design Handoff

- Change: consolidate-desktop-project
- Phase: design
- Mode: compact
- Context hash: 4e537d84f5bd8e11ef1d36f2c0871f6bd560856e16a07e7acad37bce952a8220

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/consolidate-desktop-project/proposal.md

- Source: openspec/changes/consolidate-desktop-project/proposal.md
- Lines: 1-26
- SHA256: 22ee407f9704ff53580f9bbdfe18cae7f1e496fc978ed40a1f0641000c38f153

```md
## Why

当前核心网关代码作为独立 `OllamaHub` 工程和 CLI 入口存在，但产品仅需桌面端。双工程结构让依赖、构建和发布维护产生重复，也会继续暴露已不再支持的命令行启动方式。

## What Changes

- 将原 `OllamaHub` 项目的有效网关、配置、代理和契约源码纳入 `OllamaHub.Desktop`，保持桌面端同进程托管网关的现有运行方式。
- **BREAKING** 从解决方案、测试引用和发布物中移除独立 `OllamaHub` 工程、`OllamaHub.exe` CLI 入口及其参数启动命令。
- 删除旧项目目录，并让桌面端直接承载原核心代码所需的依赖和构建配置。
- 更新测试工程，使其仅引用桌面工程；保留对网关与配置行为的覆盖。

## Capabilities

### New Capabilities

- `desktop-only-distribution`: 定义桌面端为唯一受支持的应用入口与发布目标，且不再生成独立 CLI 可执行文件。

### Modified Capabilities

- 无。

## Impact

- 受影响的代码：`OllamaHub`、`OllamaHub.Desktop`、`OllamaHub.Tests` 和 `OllamaHub.slnx`。
- 受影响的构建与发布：桌面项目新增原核心项目的直接包依赖，发布仅产出桌面端。
- 受影响的接口：移除 `OllamaHub.exe` 与命令行参数启动兼容性；桌面 UI 和本地网关 HTTP 行为保持不变。

```

## openspec/changes/consolidate-desktop-project/design.md

- Source: openspec/changes/consolidate-desktop-project/design.md
- Lines: 1-62
- SHA256: 61652eef67ca84862c832f73527ecd52ace0194bbaf9d63a884db85d2daa8a76

```md
## Context

当前解决方案同时包含 `OllamaHub`、`OllamaHub.Desktop` 和 `OllamaHub.Tests`。桌面端通过 `GatewayProcessService` 在本进程中启动 `OllamaHubHost`，因此原核心项目已不需要作为独立可执行程序运行。详见 [proposal.md](proposal.md) 的动机与范围。

原核心项目使用 `Microsoft.NET.Sdk.Web` 并直接引用 Serilog 包；桌面工程使用 `Microsoft.NET.Sdk`，已有 `Microsoft.AspNetCore.App` FrameworkReference。测试工程目前同时引用核心和桌面工程。根目录 `README.md` 仍包含旧 CLI 的 `SetApiKey` 示例。

## Goals / Non-Goals

**Goals:**

- 在不改变 `OllamaHub.*` 命名空间和桌面进程内网关行为的前提下，将有效核心源码物理迁入 `OllamaHub.Desktop`。
- 让解决方案、测试依赖和发布产物只面向桌面端。
- 移除 CLI 专用源文件、命令说明和旧工程目录，并用构建、测试和发布验证迁移完整性。

**Non-Goals:**

- 不重构网关 HTTP 协议、配置数据库位置或业务服务边界。
- 不重命名现有 `OllamaHub.*` 命名空间。
- 不修改历史 `docs/superpowers` 设计记录。
- 不为旧 CLI 提供兼容层、重定向或参数迁移。

## Decisions

### 按现有逻辑目录迁移核心源码并保持命名空间

将原 `Activity`、`Configuration`、`Contracts`、`Hosting`、`Logging`、`Services`、`AppDataPaths.cs` 与 `OllamaHubHost.cs` 迁入 `OllamaHub.Desktop` 的对应目录，保留 `OllamaHub.*` 命名空间。

保留命名空间能避免桌面代码和测试中大范围的 using 与类型重写；物理路径则清楚表达这些实现已是桌面工程的内部代码。替代方案是保留独立类库，或用链接文件共享源码；两者都会继续保留不需要的工程边界，因此不采用。

### 维持桌面工程 SDK，显式补齐核心运行依赖

桌面项目继续使用 `Microsoft.NET.Sdk` 和现有 `Microsoft.AspNetCore.App` FrameworkReference；将原核心项目独有的 `Serilog.AspNetCore` 与 `Serilog.Sinks.File` 加入桌面项目的直接 PackageReference。

这可保持 Avalonia 工程类型与发布配置不变，同时确保核心托管和日志代码的编译依赖完整。替代方案是将桌面工程改为 `Microsoft.NET.Sdk.Web`，但它会不必要地改变桌面项目的 SDK 语义和发布面。

### 明确删除 CLI 专用入口

不迁移旧项目的 `Program.cs`、`Interop/WindowsConsoleManager.cs` 和重复的程序集属性文件。保留桌面项目现有的 `Program.cs` 作为唯一进程入口，并更新根目录 README 移除旧 CLI 示例。

这避免同一程序集出现多个入口点或程序集属性，并确保不再向用户暴露旧命令。替代方案是保留 CLI 代码但不在解决方案中引用，仍会留下误用和维护风险，因此不采用。

### 直接引用桌面工程进行测试

测试工程移除对旧核心项目的 ProjectReference，只引用桌面工程；同步更新以旧文件路径定位源码的契约测试。

合并后测试将继续访问同样的公共和内部核心类型，但不会重新引入已删除工程。替代方案是保留测试对一个空壳工程的引用，没有实际价值且违背仅保留桌面端的目标。

## Risks / Trade-offs

- [源码迁移遗漏文件或重复编译] → 按旧项目清单逐项迁移，并删除整个旧项目目录后执行完整构建。
- [桌面工程缺失原核心的直接包依赖] → 显式添加 Serilog 包并运行 `dotnet test`、`dotnet build` 与发布验证。
- [测试含有硬编码路径或工程引用] → 搜索所有旧路径和项目名，并更新相关契约测试。
- [文档仍引导用户调用 CLI] → 更新当前 README；历史设计文档作为历史记录不作追溯修改。

## Migration Plan

1. 迁移有效核心源码至桌面目录，排除 CLI 专用文件和重复程序集属性。
2. 调整桌面、测试和解决方案项目引用，删除旧工程目录与当前 README 中的 CLI 示例。
3. 搜索仓库确认不存在旧项目引用或 CLI 启动说明。
4. 运行完整测试、构建和桌面端发布；将发布包放入以可读时间命名的 `outputs` 子目录。

变更可以通过 Git 回退恢复；发布前无需对用户配置数据库执行数据迁移。

```

## openspec/changes/consolidate-desktop-project/tasks.md

- Source: openspec/changes/consolidate-desktop-project/tasks.md
- Lines: 1-15
- SHA256: 606dfcf17b260dd4832d3417c6ebfeff8574705c12e557871225b2bddaf5e7e0

```md
## 1. 核心源码并入桌面工程

- [ ] 1.1 将原核心网关、配置、代理、日志和契约源码迁入 `OllamaHub.Desktop` 对应目录，排除 CLI 专用文件，并验证桌面工程包含全部有效源码。
- [ ] 1.2 保持桌面工程 SDK 与 FrameworkReference 配置，补齐原核心项目专有的包依赖，并验证项目还原成功。

## 2. 移除旧工程边界

- [ ] 2.1 更新解决方案和测试工程引用，使测试仅引用桌面工程，并更新所有依赖旧源码路径的测试；验证仓库搜索不再返回旧项目引用。
- [ ] 2.2 删除旧 `OllamaHub` 工程目录与 CLI 专用入口，更新当前 README 移除 `SetApiKey` 使用说明；验证发布物与文档不再提供旧 CLI 入口。

## 3. 构建与发布验证

- [ ] 3.1 运行 `dotnet test OllamaHub.slnx`，验证核心与桌面行为测试通过。
- [ ] 3.2 运行 `dotnet build OllamaHub.slnx`，验证解决方案仅由桌面和测试工程构成且构建成功。
- [ ] 3.3 发布桌面工程到 `outputs` 下的可读时间命名目录，验证产物存在且未生成独立 `OllamaHub.exe`。

```

## openspec/changes/consolidate-desktop-project/specs/desktop-only-distribution/spec.md

- Source: openspec/changes/consolidate-desktop-project/specs/desktop-only-distribution/spec.md
- Lines: 1-19
- SHA256: 5cc4f6206d834604878e98dd9d4b88875f032758fa65b1e56c8c15cc3a368009

```md
## Purpose

定义 OllamaHub 仅以桌面端应用交付和运行，避免向用户或自动化流程继续暴露已废弃的独立命令行入口。

## ADDED Requirements

### Requirement: 桌面端是唯一的受支持入口
系统 SHALL 仅将 `OllamaHub.Desktop` 作为 OllamaHub 的应用工程和发布目标。桌面应用 SHALL 在同一进程中托管本地网关，并继续提供桌面 UI 所依赖的本地网关行为。

#### Scenario: 构建并发布桌面端
- **WHEN** 维护者构建解决方案并发布桌面项目
- **THEN** 解决方案仅包含桌面工程和测试工程，发布目录仅提供 `OllamaHub.Desktop` 应用入口，且不生成独立的 `OllamaHub.exe`

### Requirement: 不再提供 CLI 参数命令
系统 SHALL 不再提供旧 `OllamaHub.exe` CLI 入口、`SetApiKey` 命令或其他通过该旧可执行文件传入参数的启动命令。

#### Scenario: 检查受支持的启动方式
- **WHEN** 用户查阅项目的当前使用说明或发布目录
- **THEN** 用户只能获得桌面应用启动方式，且找不到旧 CLI 参数命令的可执行入口或使用说明

```
