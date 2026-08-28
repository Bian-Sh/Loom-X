# Avalonia 桌面 UI 接入设计

## 1. 背景与目标

OllamaHub 当前是基于 ASP.NET Core 的本地协议网关，具备配置加载、模型路由和多协议转发能力，但缺少跨平台桌面入口。目标是在不重写现有网关核心的前提下，引入 C# + XAML 的 Avalonia 桌面 UI，形成 Windows、Linux、macOS 可运行的控制中心。

第一阶段只实现最小闭环：

- 启动 Avalonia 桌面应用并显示主窗口。
- 使用 Fluent 风格控件与主题资源，预留 Acrylic/Mica 能力，不把平台特效写死在业务页面中。
- 提供六个固定页面的导航壳：概览、网关、Provider、活动、控制台、设置。
- 支持启动、停止、查看本地网关状态。
- 概览页展示监听地址、网关状态和已配置 Provider/模型数量。
- 复用现有配置和服务逻辑，避免在 UI 中复制配置解析或协议转发代码。

## 2. 非目标

本阶段不实现：

- 不迁移或重写现有 ASP.NET Core API 映射。
- 不一次性实现 Provider、活动、控制台、设置页面的全部编辑能力。
- 不引入 WebView，也不把 `.design` HTML 直接嵌入桌面窗口。
- 不实现系统托盘、自动启动、安装包和自动更新。
- 不把 Windows 专属 Mica/Acrylic API 作为跨平台运行前提。

## 3. 方案选择

### 方案 A：单进程 Avalonia + Generic Host（推荐）

将当前网关启动逻辑抽取为可复用的宿主服务，由 Avalonia 应用负责生命周期；桌面窗口与网关共享依赖注入容器、配置提供器和日志服务。

优点是单进程、生命周期简单、可直接复用现有服务。代价是需要把顶层语句中的网关启动过程整理成可组合服务，UI 崩溃隔离能力弱于双进程方案。

### 方案 B：Avalonia UI 启动独立网关子进程

桌面应用负责启动和监控现有网关可执行文件，通过 HTTP API 获取状态。优点是改动网关较少、隔离性较好；代价是跨平台发布、子进程路径、退出回收和日志关联更复杂。

### 方案 C：Avalonia 只做前端，依赖外部已启动网关

桌面应用不负责网关生命周期，仅连接用户指定地址。实现简单，但不满足控制中心的核心体验，也无法保证首次启动时有可用后端。

本阶段采用方案 A；若后续需要插件隔离或独立升级，再评估方案 B。

## 4. 目标结构

```text
OllamaHub.slnx
├─ OllamaHub              现有网关核心与 HTTP API
├─ OllamaHub.Desktop      Avalonia 应用、窗口、页面和 ViewModel
└─ OllamaHub.Tests        网关与应用服务测试
```

`OllamaHub.Desktop` 通过项目引用复用 `OllamaHub` 中的配置、日志和服务；网关核心不得引用 Avalonia，保持服务层可测试和可在无 UI 环境运行。

桌面端内部按以下边界组织：

- `App.axaml` / `App.axaml.cs`：主题、资源和应用生命周期。
- `MainWindow.axaml` / `MainWindow.axaml.cs`：窗口布局和导航容器，不承载业务请求。
- `Views`：六个页面的视图，首阶段除概览外使用明确的占位状态。
- `ViewModels`：导航状态、网关状态和概览数据。
- `Services`：网关生命周期协调、概览查询和 UI 友好的错误转换。

## 5. 网关生命周期契约

定义桌面端使用的最小抽象：

```csharp
public interface IGatewayLifecycleService
{
    GatewayStatus Status { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
```

约束如下：

- 启动操作幂等；运行中重复启动不创建第二个监听器。
- 停止操作幂等；应用退出时尽力停止并释放宿主。
- 启动失败返回可展示的错误，不让 UI 线程抛出未处理异常。
- 状态至少包含 `Stopped`、`Starting`、`Running`、`Stopping`、`Failed`。
- 监听地址来自现有 `OllamaHubConfigLoader`，不在 UI 单独维护一套地址配置。

## 6. 概览页契约

概览 ViewModel 只依赖抽象服务，提供：

- `GatewayStatus`：当前状态和错误信息。
- `Endpoint`：当前有效监听地址。
- `ProviderCount`：已配置 Provider 数量。
- `ModelCount`：已配置模型数量。
- `StartCommand` / `StopCommand`：生命周期操作。
- `RefreshCommand`：重新读取状态与配置摘要。

首阶段页面加载时自动刷新一次；启动和停止成功后再次刷新。计数读取现有配置模型集合，不新增数据库或缓存。

## 7. 视觉与跨平台策略

- 使用 Avalonia FluentTheme 和项目统一的浅色/深色资源。
- 页面颜色、间距、圆角和字体大小集中为资源键，避免在页面中散落常量。
- Acrylic/Mica 作为可选窗口背景策略：Windows 支持时启用，否则回退到普通背景色。
- Linux/macOS 不依赖 Windows API；平台特效通过条件能力检测或独立适配器实现。
- 导航使用左侧栏，页面内容区域保持与 `.design` 六页信息架构一致，但不要求首阶段像素级复刻 HTML 原型。

## 8. 验证标准

- `dotnet build OllamaHub.slnx` 成功。
- `dotnet test` 保持现有测试通过。
- 桌面项目可在当前开发环境构建，并能启动主窗口。
- 启动/停止网关不会创建重复监听器，失败状态可回显。
- 六个导航项可切换，概览页显示真实配置摘要，其余页面显示明确占位内容。
- 网关核心项目不产生 Avalonia 依赖。

## 9. 后续演进

完成第一阶段后，按页面优先级逐步接入 Provider 配置中心、控制台实时日志、活动诊断、网关路由管理、设置持久化；每个页面独立建立 OpenSpec 变更和测试，不在本阶段提前引入复杂 MVVM 框架或全局状态管理。