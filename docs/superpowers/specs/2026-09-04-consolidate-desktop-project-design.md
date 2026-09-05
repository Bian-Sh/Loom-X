---
comet_change: consolidate-desktop-project
role: technical-design
canonical_spec: openspec
---

# OllamaHub 源码整合到桌面工程技术设计

## 概述

桌面端当前已经通过 `GatewayProcessService` 在 Avalonia 进程内调用 `OllamaHubHost.CreateAsync` 并启动 ASP.NET Core 网关。因此本次变更不改变网关宿主方式，而是消除已失去用途的独立工程和 CLI 入口。

本设计以 OpenSpec 的 `desktop-only-distribution` 规格为唯一行为契约：桌面端是唯一受支持的应用与发布入口，旧 `OllamaHub.exe` 和 `SetApiKey` CLI 命令不再存在。

## 目标边界

- 将旧工程中除 CLI 和重复程序集属性之外的 25 个 C# 源文件迁入 `OllamaHub.Desktop`。
- 保留所有现有 `OllamaHub.*` 命名空间、桌面进程内网关调用方式、HTTP 路由和唯一的 LocalAppData 数据库路径。
- 移除解决方案和测试工程对 `OllamaHub.csproj` 的引用，删除整个旧项目目录。
- 不引入兼容项目、编译链接或运行时转发。

不在本次范围内：改变 Avalonia UI、重构网关协议、迁移数据库内容、修改历史设计记录，或为 CLI 提供替代命令。

## 目标文件布局

有效源文件按既有职责移动到桌面工程的同名目录，物理归属改变但类型全名不变：

```text
OllamaHub.Desktop/
  Activity/
  Configuration/
  Contracts/
  Hosting/
  Logging/
  Services/
  AppDataPaths.cs
  OllamaHubHost.cs
  Program.cs                 # 保留现有 Avalonia 唯一入口
  Properties/AssemblyInfo.cs # 保留现有 InternalsVisibleTo
```

不迁移的旧文件：`Program.cs`、`Interop/WindowsConsoleManager.cs`、`Properties/AssemblyInfo.cs`。前两者只服务旧 CLI，最后一项在桌面工程中已有等价程序集属性，合并会造成重复特性。

## 构建与依赖决策

### 保留桌面项目 SDK

`OllamaHub.Desktop.csproj` 保持 `Microsoft.NET.Sdk`、`WinExe` 和 `Microsoft.AspNetCore.App` FrameworkReference。该引用已经提供桌面内托管网关所需的 ASP.NET Core 共享框架，不能为迁移而改成 Web SDK。

原核心项目独有的 `Serilog.AspNetCore` 和 `Serilog.Sinks.File` 改为桌面项目的直接 PackageReference。`Microsoft.EntityFrameworkCore.Sqlite` 已由桌面项目直接引用，无需重复添加。

### 清理工程图

迁移前：

```text
OllamaHub.Tests -> OllamaHub
                 -> OllamaHub.Desktop -> OllamaHub
```

迁移后：

```text
OllamaHub.Tests -> OllamaHub.Desktop
```

`OllamaHub.slnx` 只保留桌面工程和测试工程。测试项目因 `InternalsVisibleTo("OllamaHub.Tests")` 继续访问内部核心类型，不需要为可见性添加新的兼容代码。

## 迁移步骤

1. 逐目录迁移核心源文件到桌面工程，保留文件内容和命名空间。
2. 修改桌面工程包引用，移除其旧 ProjectReference。
3. 修改测试工程，使其只引用桌面工程；更新 `AppStartupAndProviderRefreshContractTests` 中两处旧路径，使其读取桌面工程中的 `OllamaHubHost.cs` 和 `Configuration/DatabaseConfigurationProvider.cs`。
4. 从解决方案移除旧项目，删除旧项目目录，更新 README，删除单独运行网关和 `SetApiKey` 说明。
5. 搜索活动源码、项目文件和 README，确认没有残留的旧工程或 CLI 启动说明；不修改历史文档中的历史描述。

## 运行时行为与失败处理

迁移不改变 `GatewayProcessService` 的启动、健康检查、停止或异常状态逻辑。`OllamaHubHost.CreateAsync` 迁移后仍位于同一程序集，依赖注入服务、日志和数据库路径的解析不改变。

编译失败应优先揭示缺失的直接依赖、错误的相对路径或遗漏文件；不通过临时复制或双项目兼容来掩盖问题。若迁移导致桌面端无法启动，使用 Git 回退本次变更恢复旧工程结构，不触碰用户的 `%LOCALAPPDATA%\OllamaHub\OllamaHub.db`。

## 测试与验收策略

- `dotnet test OllamaHub.slnx`：覆盖配置、网关和桌面启动契约；尤其验证静态契约测试读取迁移后的源码路径。
- `dotnet build OllamaHub.slnx`：确认仅由桌面与测试工程组成的解决方案能够完成构建。
- `dotnet publish OllamaHub.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o outputs\\yyyy-MM-dd_HHmm`：确认发布目录包含 `OllamaHub.Desktop.exe` 而不包含 `OllamaHub.exe`。
- 仓库搜索：活动源码、项目文件和 README 中不再出现旧 `OllamaHub.csproj`、旧项目路径或 CLI 用法；LocalAppData 数据库路径相关文本不视为旧工程残留。

## 风险与缓解

| 风险 | 缓解措施 |
| --- | --- |
| 遗漏某个核心文件 | 用旧项目文件清单逐项迁移，在删除旧目录后构建。 |
| 迁移后缺少日志运行依赖 | 将原核心的两个 Serilog 包改为桌面直接依赖并执行还原。 |
| 静态测试仍定位旧路径 | 更新两处硬编码路径并运行全量测试。 |
| 说明文档误导用户调用 CLI | 仅更新当前 README；保留历史设计文档不改写。 |

## Spec Patch

无。OpenSpec 规格已明确桌面端唯一入口和旧 CLI 移除，迁移实现不需要补充行为场景。
