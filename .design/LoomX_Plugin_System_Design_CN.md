# LoomX Plugin System 需求与架构设计

> 状态：设计阶段  
> 定位：LoomX Router 的可插拔扩展系统  
> 目的：定义 Plugin Runtime、Router Pipeline Extension、Plugin Configuration、Settings UI、Pipeline 高级编排、动态加载/卸载以及跨平台可行性，为后续实现提供单一设计依据。

---

## 1. 核心定位

LoomX 首先是 Router。

Plugin System 的第一职责不是扩展 Agent，而是扩展 Router 对请求、响应以及中间数据的处理能力。Provider 故障转移、数据脱敏、Tool Result 后处理、协议转换、统计、日志等都属于 Router 数据流能力；其中一部分能力由内置代码实现，另一部分可以通过 Plugin 扩展。

LoomX 内置 AI Agent（“小助手”）是 Router 的一个消费者。它应当像外部 Agent Client 一样通过 LoomX Router 使用这些能力，而不获得一套绕过 Router Plugin Pipeline 的特殊通道。

~~~text
External Agent / App ──┐
                       ├── LoomX Endpoint ── Router ── Provider
LoomX Agent ───────────┘                         │
                                                └── Plugin Pipelines
~~~

## 2. 设计原则

### 2.1 Router-first

Plugin 的运行时扩展点必须围绕 Router 数据生命周期设计，而不是围绕 Agent 生命周期设计。

### 2.2 开放插件，不预知插件

LoomX 不应要求预先知道所有插件，更不应在核心代码中维护“插件 A 与插件 B 的语义关系”。

插件只需要声明自己提供哪些 Extension，以及希望进入哪个 Pipeline。

### 2.3 Pipeline 负责顺序

同一个 Pipeline 内的 Extension Entry 是一个有序集合。执行顺序由 Router Pipeline 配置决定。

第一版不把 before、after、requires、conflicts 等插件间语义依赖作为基础机制。这样可以避免插件生态被全局依赖图绑死。

如果未来确实出现不可绕过的安全约束，应由 Router 的 Pipeline Contract 定义，而不是让插件互相理解。

### 2.4 普通设置与高级编排分离

Plugin SettingsProvider 解决：

> “这个插件自己的数据和行为怎么配置？”

Pipeline Advanced Settings 解决：

> “Router 在某个扩展点执行哪些处理，以及它们的顺序是什么？”

二者必须是两个不同的 UI 层次。

### 2.5 UI 是 Plugin 能力的一部分，但不是 Runtime Contract 的全部

Plugin 可以没有 UI，也可以提供 UI。

Router Runtime 不应依赖 Plugin UI 才能运行。

### 2.6 跨平台优先

目标桌面平台为 Windows、Linux、macOS。

Plugin Contract 应保持平台无关；平台特定能力由插件显式声明和隔离。

---

## 3. Plugin 的组成

一个完整 Plugin 可以由以下部分组成：

~~~text
Plugin Package
├── Manifest
├── Runtime Assembly
├── Dependencies
├── Configuration Schema / Data
└── UI Assembly / XAML View（可选）
~~~

逻辑上分为：

~~~text
Plugin
├── Runtime
│   └── Router Extensions
├── Configuration
│   └── Plugin-owned data
└── UI
    └── SettingsProvider / Views
~~~

### 3.1 Runtime

负责真正参与 Router Pipeline 的代码，例如：

- Request Processor
- Response Processor
- Tool Result Processor
- Provider Selection Extension
- Observability Extension
- Persistence Extension
- 其他 LoomX 明确定义的 Extension Point

### 3.2 Configuration

Plugin 可以拥有自己的配置数据，例如：

- Enabled
- 规则列表
- 阈值
- 策略
- 本地索引
- Recall 数据
- UI 状态（如确有必要）

Plugin 配置必须有明确的持久化边界，不应直接修改 LoomX 核心配置文件的任意字段。

### 3.3 UI

Plugin 可以注册 SettingsProvider，为用户提供：

- CRUD
- 参数配置
- 诊断
- 测试
- 统计
- Plugin-specific navigation

Plugin 不应自行创建独立 Settings Window，也不应接管 LoomX Settings Shell。

---

## 4. Plugin Contract

建议建立非常小的独立 Contract Assembly：

~~~text
LoomX.Plugin.Abstractions
~~~

它不能依赖 LoomX UI、Router 内部实现或具体 Avalonia 页面。

概念接口：

~~~csharp
public interface ILoomXPlugin
{
    PluginManifest Manifest { get; }

    Task StartAsync(IPluginContext context, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
~~~

Extension 使用独立接口表达：

~~~csharp
public interface IRouterExtension
{
    string ExtensionId { get; }

    string PipelineId { get; }
}
~~~

具体 Processor 再使用强类型 Contract，例如：

~~~csharp
public interface IRequestProcessor : IRouterExtension
{
    Task<RequestContext> ProcessAsync(
        RequestContext context,
        CancellationToken cancellationToken);
}

public interface IResponseProcessor : IRouterExtension
{
    Task<ResponseContext> ProcessAsync(
        ResponseContext context,
        CancellationToken cancellationToken);
}

public interface IToolResultProcessor : IRouterExtension
{
    Task<ToolResult> ProcessAsync(
        ToolResult result,
        ToolResultContext context,
        CancellationToken cancellationToken);
}
~~~

以上接口仅作为设计方向；最终类型应以 LoomX 当前 Router/Harness 实际数据结构为准，不复制一套平行的 Agent Runtime 类型体系。

---

## 5. Extension Point 与 Pipeline

Plugin 不直接“拥有一个 Pipeline”。

Router 定义稳定的 Extension Point / Pipeline Contract，例如：

~~~text
Request
Routing
Provider Execution
Response
Tool Result
Persistence
Observability
~~~

以上仅为候选分类，不应在实现阶段未经源码验证直接固定名称。

一个 Plugin 可以注册多个 Extension：

~~~text
Credential Protection Plugin
├── Request Extension
├── Tool Result Extension
├── Response Extension
└── Persistence Extension
~~~

同一个 Extension 可以只属于一个 Pipeline。

Pipeline 内部保存的是 Extension Entry，而不是“插件排序号”。

概念数据：

~~~text
PipelineEntry
├── EntryId
├── PluginId
├── ExtensionId
├── Enabled
└── Order
~~~

### 5.1 稳定 ID

Pipeline 配置必须使用稳定的 PluginId + ExtensionId 或等价的稳定 Extension Entry ID。

不能使用：

- DLL 路径
- CLR Type.FullName
- 安装顺序
- 内存对象 ID

这样插件升级、程序集重载后，Pipeline 顺序仍然可以保持。

### 5.2 默认加入方式

新 Plugin 注册到某 Pipeline 时，默认追加到该 Pipeline 尾部。

LoomX 不要求插件作者预知其他插件。

用户可以在高级 Pipeline 设置中调整顺序。

---

## 6. Pipeline 执行顺序

同 Pipeline 的 Entry 按持久化 Order 执行：

~~~text
Pipeline
  ↓
Entry 1
  ↓
Entry 2
  ↓
Entry 3
  ↓
Router 下一阶段
~~~

执行器只需要知道：

1. 当前 Pipeline 是什么；
2. 当前有哪些启用 Entry；
3. Entry 的顺序；
4. Entry 对应的 Runtime Extension。

### 6.1 不采用全局 Priority

不建议设计 PluginA.Priority = 10、PluginB.Priority = 20。

因为 Priority 很容易变成隐式的全局耦合。

应使用 Pipeline-local order。

~~~text
ToolResultPipeline:
  A → B → C

RequestPipeline:
  B → A → D
~~~

不同 Pipeline 可以拥有完全不同的顺序。

### 6.2 不要求插件间依赖

Plugin A 与 Plugin B 默认互不知道彼此。

Pipeline Manager 只负责有序执行。

### 6.3 安全约束

如果某些处理存在不可绕过的安全边界，应由 LoomX Core / Pipeline Contract 保证。

例如敏感数据保护如果被定义为“数据离开安全边界前必须完成”，则不能依赖用户是否恰好把某个普通 Plugin 拖到它后面。

这类规则必须属于 Router Security Contract，而不是普通 Plugin-to-Plugin dependency。

---

## 7. Plugin Settings UI

Plugin UI 参考 Unity SettingsProvider 的理念：

> Host 提供统一 Settings Shell，Plugin 提供自己的 Settings Provider / View。

LoomX 不要求 Plugin 自己创建窗口。

### 7.1 Settings 总体结构

建议：

~~~text
Settings
├── General
├── Providers
├── Endpoints
├── Routing
├── Plugins
└── Advanced
    └── Pipelines
~~~

其中 Plugins 是普通用户主要使用的 Plugin 配置入口。

### 7.2 Plugins 页面布局

顶部为 Settings 一级 Tab，Plugins 选中后：

~~~text
┌─────────────────────────────────────────────────────────┐
│ General  Providers  Endpoints  Routing  Plugins  ...   │
├──────────────────────┬──────────────────────────────────┤
│ Plugin List          │ Plugin Settings                  │
│                      │                                  │
│ Credential           │ Credential Protection             │
│ Protection           │                                  │
│                      │ Enabled                     ●     │
│ Tool Result          │                                  │
│ Compression          │ Sensitive Rules                  │
│                      │                                  │
│ Provider Failover    │ [Rule CRUD]                      │
│                      │                                  │
└──────────────────────┴──────────────────────────────────┘
~~~

左侧是 Plugin 导航，右侧是当前 Plugin 的 SettingsProvider 内容。

### 7.3 SettingsProvider 的职责

普通 SettingsProvider 负责 Plugin 自己的数据 CRUD，例如：

- 敏感词规则新增
- 敏感词规则编辑
- 敏感词规则删除
- 开关
- 阈值
- 策略
- 本地存储配置
- 测试输入
- Plugin-specific diagnostics

它不负责 Pipeline 全局排序。

---

## 8. Pipeline 高级设置

Pipeline 编排属于 Advanced Settings。

建议入口：

~~~text
Settings
└── Advanced
    └── Pipelines
~~~

页面：

~~~text
Pipelines
├── Request
├── Routing
├── Response
├── Tool Result
└── ...
~~~

选择某 Pipeline 后显示：

~~~text
Tool Result

Execution Order

≡ Credential Protection
≡ Recall
≡ Compression
≡ Other Processor

[Move Up] [Move Down] [Reset]
~~~

### 8.1 关键 UI 信息

每个 Entry 至少显示：

- Extension 名称
- Plugin 名称
- Enabled 状态
- 当前顺序
- 简短职责
- 是否内置
- 是否可用
- 错误状态（如加载失败）

高级用户可以：

- 拖拽排序
- 上移/下移
- 启用/禁用 Entry
- 恢复默认顺序

### 8.2 不要求显示插件间关系图

Pipeline UI 的目标是：

> 管理一个 Pipeline 的有序入口。

不是：

> 可视化所有 Plugin 之间的关系图。

避免把简单的线性执行问题过度图形化。

---

## 9. Plugin 生命周期

建议生命周期：

~~~text
Discovered
    ↓
Validated
    ↓
Loaded
    ↓
Starting
    ↓
Running
    ↓
Stopping
    ↓
Stopped
    ↓
Unloaded
~~~

异常状态：

~~~text
Failed
~~~

Plugin Manager 负责：

- Discovery
- Manifest validation
- Dependency resolution
- Load
- Start
- Stop
- Unload
- Reload
- Enable / Disable
- Status
- Error isolation

---

## 10. AssemblyLoadContext

桌面端动态 Plugin 的基础实现建议采用：

~~~text
AssemblyLoadContext
+
AssemblyDependencyResolver
+
独立 Plugin Contract Assembly
~~~

每个可动态卸载 Plugin 使用独立、可回收的 AssemblyLoadContext。

概念：

~~~text
LoomX
│
├── Shared Contract
│   └── LoomX.Plugin.Abstractions
│
├── Plugin A
│   └── ALC-A
│
├── Plugin B
│   └── ALC-B
│
└── Plugin C
    └── ALC-C
~~~

### 10.1 Shared Assembly

Contract Assembly 必须由 Host 与 Plugin 共享同一加载实例，否则接口类型可能因 Load Context 不同而不相等。

Avalonia 核心程序集如果参与 Plugin UI，也必须明确设计共享策略，避免 Plugin ALC 内重复加载 Host 正在使用的 Avalonia 核心类型。

这是 Playground 必须优先验证的技术点。

---

## 11. Hot Reload / Hot Swap

“插件热加载”与 .NET Hot Reload 不是同一个概念。

LoomX 需要的是：

~~~text
Stop
  ↓
Release Resources
  ↓
Unload ALC
  ↓
Collect
  ↓
Load New Assembly
  ↓
Create Plugin
  ↓
Start
  ↓
Register Extensions
  ↓
Refresh UI
~~~

### 11.1 Plugin 必须释放

插件不能遗留：

- Event Handler
- Delegate
- Timer
- Task
- Thread
- Static Reference
- Stream
- Socket
- File Watcher
- UI Binding
- Host Service Reference

否则旧 ALC 可能无法回收。

### 11.2 UI Reload

Plugin Reload 时：

1. Host 先卸载当前 Plugin View；
2. 解除 View / ViewModel / Event Binding；
3. Stop Runtime；
4. Unload ALC；
5. Load 新 Plugin；
6. 重新注册 Extension；
7. 重新创建 SettingsProvider View。

### 11.3 更新失败

新版本 Plugin 加载或启动失败时：

- 不得破坏 Router；
- 保留旧版本可恢复能力；
- Pipeline Entry 不应因为一次失败而丢失；
- UI 应显示明确错误；
- 必要时回滚到上一可用版本。

---

## 12. UI 与 Runtime 生命周期解耦

Plugin UI 不应决定 Runtime 是否存在。

允许：

~~~text
Runtime Plugin
无 UI
~~~

也允许：

~~~text
Runtime Plugin
+
Settings UI
~~~

Plugin 被禁用时：

- Runtime Extension 不参与 Pipeline；
- SettingsProvider 是否仍可访问由 Host 决定；
- 推荐保留设置页面，以便用户重新启用。

Plugin 被卸载时：

- Runtime Extension 注销；
- UI 从 Host 导航树移除；
- 当前打开的 View 必须释放；
- Plugin-owned resources 必须停止。

---

## 13. 配置模型

Plugin 配置应与 LoomX Core 配置分离。

概念：

~~~text
LoomX Configuration
├── Core
├── Providers
├── Endpoints
├── Routing
└── Plugins
    ├── Plugin A
    ├── Plugin B
    └── ...
~~~

Plugin 配置至少需要：

- PluginId
- SchemaVersion
- Enabled
- Plugin-specific data

### 13.1 Schema Version

Plugin 自己负责配置迁移：

~~~text
v1
 ↓ migration
v2
 ↓ migration
v3
~~~

Host 不应理解 Plugin 内部业务字段。

---

## 14. 两个第一方 Plugin

首批用于验证和最终落地的 Plugin：

### 14.1 Credential Protection / Sensitive Data Protection

定位：

> Router 数据处理插件，用于在数据进入不应暴露真实凭据的边界前进行检测、替换和保护。

可能包含：

- Credential Detection
- Sensitive Rule
- Mask / Placeholder
- Restore / Local Resolution
- Echo Re-scrubbing
- Persistence Sanitization

UI：

~~~text
Credential Protection
├── Enabled
├── Rules CRUD
├── Detection
├── Masking
└── Diagnostics
~~~

敏感规则应是 Plugin-owned data。

### 14.2 Tool Result Compression

定位：

> Router 对 Tool Result 数据进行压缩、去重、截断、结构化处理以及可选 Recall 的能力。

建议拆分：

~~~text
Tool Result Compression
├── Processor
├── Recall Store
└── SettingsProvider
~~~

不要把 Recall Store 强制定义成 Compression 内部不可分离的实现；未来可以存在只 Recall、不 Compression 的场景。

UI 可以包含：

- Enabled
- Compression Threshold
- Strategy
- Recall
- Diagnostics
- Token Saving Statistics

---

## 15. Router 数据安全边界

Plugin Pipeline 中最重要的约束之一是：

> 未经必要安全处理的数据，不得进入后续会持久化、展示或发送给外部模型的组件。

例如：

~~~text
Raw Tool Result
    ↓
Sensitive Data Protection
    ↓
Safe Full Result
    ↓
Recall Store（可选）
    ↓
Compression
    ↓
Safe Compact Result
    ↓
Endpoint / Agent
~~~

这里的具体 Pipeline 名称和实际挂载点必须根据 LoomX 当前 Router 源码最终确定。

关键原则：

**任何 Recall / Log / Persistence 组件都不能在脱敏之前保存原始敏感数据。**

---

## 16. Plugin 权限与能力声明

Plugin Manifest 应允许声明能力，而不是直接获得 Host 全部权限。

例如：

~~~json
{
  "id": "loomx.tool-result-compression",
  "name": "Tool Result Compression",
  "version": "1.0.0",
  "runtime": {
    "minLoomX": "x.y.z",
    "hotReload": true
  },
  "extensions": [
    {
      "id": "compression",
      "type": "tool-result-processor",
      "pipeline": "tool-result"
    }
  ],
  "capabilities": [
    "tool.result.process",
    "result.recall"
  ]
}
~~~

Credential Protection 可以声明：

~~~json
{
  "id": "loomx.credential-protection",
  "extensions": [
    {
      "id": "request-protection",
      "type": "request-processor",
      "pipeline": "request"
    },
    {
      "id": "tool-result-protection",
      "type": "tool-result-processor",
      "pipeline": "tool-result"
    }
  ],
  "capabilities": [
    "credential.detect",
    "credential.mask",
    "credential.restore"
  ]
}
~~~

以上 Manifest 只是设计示例，不是最终 Schema。

---

## 17. Host 与 Plugin 的依赖边界

Plugin 不应直接依赖：

- LoomX Main Window
- LoomX ViewModel
- Router 内部私有类
- AgentLoop
- AgentSession 内部实现
- Host-specific service locator
- Host UI 状态

推荐：

~~~text
LoomX.Plugin.Abstractions
        ↑
        │
Plugin
        │
        ├── Router Contract
        └── Optional UI Contract
~~~

这样 Plugin API 才能长期稳定。

---

## 18. Avalonia UI 可行性

LoomX 当前桌面 UI 使用 Avalonia/XAML，因此 Plugin UI 目标为：

- Windows
- Linux
- macOS

Plugin View 使用 Avalonia Control / XAML。

但 UI Contract 与 Runtime Contract 应保持逻辑分离：

~~~text
LoomX.Plugin.Abstractions
        │
        ├── Runtime Contracts
        │
        └──（不要强制引用 Avalonia）
~~~

UI 扩展可以另设：

~~~text
LoomX.Plugin.UI
~~~

用于：

- SettingsProvider
- Plugin Page
- Navigation metadata
- View factory

最终 API 是否采用两个 Assembly，应通过 Playground 验证。

---

## 19. Plugin Package 与安装

建议 Plugin 安装目录：

~~~text
plugins/
└── <plugin-id>/
    └── <version>/
        ├── plugin.json
        ├── Plugin.dll
        ├── Dependencies/
        └── UI/
~~~

不应依赖绝对路径。

Manifest 至少描述：

- ID
- Name
- Version
- Min LoomX Version
- Runtime Assembly
- Extensions
- Capabilities
- UI Provider
- Supported Platforms
- Hot Reload Capability

未来可增加：

- Publisher
- Signature
- Checksum
- Update Channel
- License
- Repository

第一阶段不必实现完整插件市场。

---

## 20. 错误隔离

Plugin 运行异常不应直接导致 Router 崩溃。

至少要做到：

~~~text
Plugin Exception
      ↓
Extension Entry Failed
      ↓
Record Diagnostic
      ↓
Apply Host-defined failure policy
      ↓
Router continues / request fails safely
~~~

具体 failure policy 必须按 Extension Point 定义。

例如：

- Observability 插件失败：通常不能影响主请求；
- 数据安全插件失败：不能静默绕过安全边界；
- Provider Selection 插件失败：应回退到 Router 默认策略或安全失败；
- Tool Result Processor 失败：需要明确“发送原始结果”是否允许，不能默认放行敏感数据。

---

## 21. Pipeline Advanced Settings 的原则

高级 Pipeline 设置只操作：

~~~text
Pipeline Entry
├── Enabled
└── Order
~~~

不负责修改：

- Plugin 内部配置
- Plugin Rule CRUD
- Plugin API Key
- Plugin-specific thresholds

例如：

~~~text
Plugins
└── Credential Protection
    └── 编辑敏感规则

Advanced
└── Pipelines
    └── 调整 Credential Protection 在 Pipeline 中的位置
~~~

两个页面修改的是不同的数据。

---

## 22. Plugin Playground / Spike 验证项目

在进入 LoomX 生产代码之前，必须先完成一个独立小型验证项目。

建议名称：

~~~text
LoomX.PluginPlayground
~~~

技术栈：

- .NET
- Avalonia
- XAML
- AssemblyLoadContext
- Contract Assembly

目标不是做完整框架，而是验证架构是否优雅。

### Phase 1：Runtime

验证：

- Plugin Discovery
- Manifest
- Load
- Start
- Register Extension
- Pipeline Execution
- Stop
- Unload
- Reload

### Phase 2：Dynamic UI

验证：

- Plugin SettingsProvider
- 动态 XAML View
- Host Navigation
- Plugin ViewModel
- Data Binding
- Theme
- View Dispose

### Phase 3：Router Simulation

模拟：

~~~text
Request Pipeline
Response Pipeline
Tool Result Pipeline
Persistence Pipeline
~~~

实现两个示例 Plugin：

~~~text
Sensitive Data
Tool Result Compression
~~~

验证：

- 多 Plugin 注册
- 同 Pipeline 排序
- 启用/禁用
- 新插件追加
- 拖拽排序
- Plugin Reload 后顺序保持
- Plugin UI 动态挂载/卸载
- 配置 CRUD
- Runtime 与 UI 生命周期解耦

---

## 23. Playground 必须重点验证的技术风险

### 23.1 Avalonia + AssemblyLoadContext

确认 Plugin ALC 中的 Avalonia 类型与 Host 类型不会发生类型身份冲突。

### 23.2 XAML 加载

确认 Plugin 内的 AXAML 能在动态 Assembly 中正常加载。

### 23.3 Binding

确认 Plugin ViewModel 不需要引用 Host 私有类型。

### 23.4 Theme

确认 Plugin UI 可以自然使用 LoomX Host Theme，而不复制一套主题。

### 23.5 Unload

打开 Plugin Settings Page 后执行 Reload，确认旧 View、Binding、事件和 ALC 都可以释放。

### 23.6 Plugin Dependency

验证不同 Plugin 使用不同版本的普通 NuGet 依赖时是否可以隔离。

### 23.7 Configuration Migration

修改 Plugin 配置 Schema 后 Reload，确认迁移不会污染 Host Configuration。

---

## 24. 第一阶段非目标

暂不实现：

- Plugin Marketplace
- 在线插件仓库
- 自动更新服务
- 第三方 Plugin 签名体系
- 跨进程沙箱
- 不可信代码安全隔离
- Plugin 间语义依赖图
- 全局 before/after/conflict DSL
- Plugin 自定义独立 Window Framework
- Plugin 专属主题系统
- Agent 专用 Plugin API

尤其不把 AssemblyLoadContext 当成安全沙箱。它主要解决程序集隔离与卸载问题，不解决恶意代码执行风险。

---

## 25. 最终验收标准

### Runtime

- [ ] Plugin 可以从目录发现。
- [ ] Manifest 可以验证。
- [ ] Plugin 可以动态 Load。
- [ ] Plugin 可以注册多个 Router Extension。
- [ ] Extension 可以进入指定 Pipeline。
- [ ] 同 Pipeline Entry 可以稳定排序。
- [ ] Plugin 可以 Enable/Disable。
- [ ] Plugin 可以 Stop/Unload/Reload。
- [ ] 一个 Plugin 失败不会无条件拖垮 Router。

### UI

- [ ] Plugin 可以没有 UI。
- [ ] Plugin 可以注册 SettingsProvider。
- [ ] Plugin Settings 可以动态出现在 Settings。
- [ ] Plugin 可以提供 XAML View。
- [ ] Plugin View 与 Host Settings Shell 解耦。
- [ ] Plugin Settings 支持 CRUD。
- [ ] Pipeline 排序位于 Advanced Settings。
- [ ] Plugin 卸载后对应 UI 正确移除。

### Pipeline

- [ ] Pipeline 是 Router 的概念。
- [ ] Plugin 不需要知道其他 Plugin。
- [ ] Pipeline Entry 使用稳定 ID。
- [ ] 新 Plugin 默认可以追加。
- [ ] 用户可以调整同 Pipeline Entry 顺序。
- [ ] 不依赖全局 Priority。
- [ ] 不要求 Plugin 间 before/after/requires 关系。
- [ ] 安全边界由 Router Contract 保证。

### Cross-platform

- [ ] Windows 可运行。
- [ ] Linux 可运行。
- [ ] macOS 可运行。
- [ ] Plugin Contract 不依赖平台 API。
- [ ] Avalonia XAML View 可以跨平台加载。

---

## 26. 与 LoomX 现有架构的集成原则

本设计不是要求 LoomX 创建第二套 Agent Runtime。

集成时应优先寻找现有 Router、Gateway、Provider、Request/Response、Tool Result 和 Harness 数据流中的真实生命周期，然后把 Plugin Extension 挂入现有流程。

尤其要避免：

~~~text
Existing Router
       +
New Plugin Router
       +
New Agent Pipeline
~~~

正确方向是：

~~~text
Existing LoomX Router
        │
        ├── Extension Point
        │      ↓
        │   Plugin Entry
        │
        └── Existing Agent
               ↓
          normal Router path
~~~

LoomX 内置 Agent 不应成为 Plugin System 的架构中心。

---

## 27. 设计结论

LoomX Plugin System 的核心抽象最终收敛为：

~~~text
Plugin
  = Runtime Capability
  + Optional Configuration
  + Optional Settings UI

Router
  = Pipeline Owner

Pipeline
  = Ordered Extension Entries

SettingsProvider
  = Plugin-owned CRUD / Configuration UI

Advanced Pipeline Settings
  = Router-owned execution-order management
~~~

最重要的边界：

> **Plugin 提供能力；Router 决定能力在哪个 Pipeline 执行；Pipeline 只管理同一扩展点内的顺序；SettingsProvider 管理插件自己的数据；高级设置管理 Router Pipeline。**

这套模型允许未知的第三方 Plugin 持续加入，而不会要求 LoomX 预先理解整个插件生态。

在正式修改 LoomX 之前，先通过 LoomX.PluginPlayground 验证 .NET AssemblyLoadContext + Avalonia/XAML 动态 UI + Pipeline Entry + SettingsProvider + Reload 的组合是否足够简单、稳定、优雅。验证通过后，再将经过验证的最小 Contract 迁入 LoomX。
