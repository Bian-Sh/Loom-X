---
change: consolidate-desktop-project
design-doc: docs/superpowers/specs/2026-09-04-consolidate-desktop-project-design.md
base-ref: b31fb3b813d257dfad0a1cee875801e9cba5fe5f
---

# 整合 OllamaHub 源码到桌面工程实施计划

> 计划根据 OpenSpec 任务和已确认的技术设计创建。当前环境没有 `writing-plans` 技能，因此以等价的人工计划作为流程降级记录。

## 1. 迁移有效核心源码

**目标文件：**
- `OllamaHub/Activity/*.cs` → `OllamaHub.Desktop/Activity/`
- `OllamaHub/Configuration/*.cs` → `OllamaHub.Desktop/Configuration/`
- `OllamaHub/Contracts/*.cs` → `OllamaHub.Desktop/Contracts/`
- `OllamaHub/Hosting/GatewayEndpointRouting.cs` → `OllamaHub.Desktop/Hosting/`
- `OllamaHub/Logging/*.cs` → `OllamaHub.Desktop/Logging/`
- `OllamaHub/Services/*.cs` → `OllamaHub.Desktop/Services/`
- `OllamaHub/AppDataPaths.cs`、`OllamaHub/OllamaHubHost.cs` → `OllamaHub.Desktop/`

**步骤：**
1. 逐个移动上述 25 个文件，保持每个文件内容和 `OllamaHub.*` 命名空间不变。
2. 不移动 `OllamaHub/Program.cs`、`OllamaHub/Interop/WindowsConsoleManager.cs` 和 `OllamaHub/Properties/AssemblyInfo.cs`。
3. 确认桌面工程已经有唯一的 `Program.cs` 和 `Properties/AssemblyInfo.cs`，避免重复入口点和程序集属性。

**验证：**
- 搜索桌面目录，确认 25 个迁移文件存在。
- 搜索旧目录，确认仅剩待删除的 CLI 专用文件和项目文件。

## 2. 合并项目依赖与解决方案

**目标文件：**
- `OllamaHub.Desktop/OllamaHub.Desktop.csproj`
- `OllamaHub.Tests/OllamaHub.Tests.csproj`
- `OllamaHub.slnx`

**步骤：**
1. 从桌面项目移除 `..\\OllamaHub\\OllamaHub.csproj` ProjectReference。
2. 向桌面项目添加 `Serilog.AspNetCore` 10.0.0 和 `Serilog.Sinks.File` 7.0.0 的直接 PackageReference；保留 SDK 和现有 FrameworkReference。
3. 从测试项目移除旧核心 ProjectReference，仅保留桌面项目引用。
4. 从 `.slnx` 删除旧核心项目行。

**验证：**
- `dotnet restore OllamaHub.slnx` 成功。
- 项目文件和解决方案中不存在 `OllamaHub/OllamaHub.csproj`。

## 3. 更新测试与当前说明

**目标文件：**
- `OllamaHub.Tests/Views/AppStartupAndProviderRefreshContractTests.cs`
- `README.md`

**步骤：**
1. 将静态契约测试的 `OllamaHubHost.cs` 和 `DatabaseConfigurationProvider.cs` 路径修改为 `OllamaHub.Desktop` 下的新位置。
2. 从 README 删除“开发运行网关”和 `SetApiKey` CLI 命令段落；保留桌面启动、HTTP 接口、数据库路径和发布说明。
3. 删除整个旧 `OllamaHub` 目录，以移除旧项目、CLI 入口和控制台互操作代码。

**验证：**
- 活动源码、项目文件和 README 的搜索不再返回旧项目引用、`SetApiKey` CLI 用法或 `WindowsConsoleManager`。
- 历史 `docs/superpowers` 文档不纳入本次修改。

## 4. 完整验证与发布

**步骤：**
1. 运行 `dotnet test OllamaHub.slnx`。
2. 运行 `dotnet build OllamaHub.slnx`。
3. 使用 `dotnet publish OllamaHub.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false` 发布到 `outputs` 下按实际执行时间命名的目录。
4. 检查发布目录：存在 `OllamaHub.Desktop.exe`，不存在 `OllamaHub.exe`。
5. 在 `tasks.md` 勾选已验证任务，并在 Taskboard 记录结果和遗留风险。

**失败处理：**
- 先根据编译或测试错误定位遗漏的源文件、包引用或硬编码路径；不增加临时兼容工程。
- 若发布验证失败，修复桌面项目的直接依赖或发布配置后重新运行完整验证。
