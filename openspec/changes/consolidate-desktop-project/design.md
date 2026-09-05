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
