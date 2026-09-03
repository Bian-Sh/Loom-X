# 概览拓扑与 AppDataStore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让首页概览复用 AppDataStore 投影完整四层拓扑，并以事件驱动的 C#→JavaScript 桥接实时更新紧凑 HUD、拓扑和遥测动画。

**Architecture:** OverviewViewModel 从 AppDataStore 快照构造去重的 Endpoint、Combo、Provider、Model 节点及关系边；OverviewGraphHost 缓存页面未就绪时的最新快照，并用 InvokeScript 调用 `applyTopology`、`applyMetrics`、`receiveTelemetry`。HTML/Three.js 负责布局、标签、HUD、相机和动画，C# 保留指标聚合与事件顺序。

**Tech Stack:** .NET 8、Avalonia、System.Text.Json、WebView2/NativeWebView、Three.js、xUnit。

**Spec:** `docs/superpowers/specs/2026-09-03-overview-topology-app-data-design.md`

## Global Constraints

- 配置只能通过 `AppDataStore` 的 `CurrentConfig`、`Providers`、`GatewayEndpoints`、`ConfigurationChanged`、`RefreshAsync` 访问，不直接访问 SQLite。
- Web 数据不得包含 API Key、Authorization、Header、prompt、请求/响应正文等敏感字段。
- 遥测枚举通过 `JsonStringEnumConverter` 序列化为字符串；禁止轮询和复杂前端状态管理。
- 所有文档、代码注释和提交消息使用中文；保留现有未跟踪流程产物不做清理。

---

### Task 1: 完整拓扑数据投影与事件合同

**Files:**
- Modify: `OllamaHub.Desktop/ViewModels/MainWindowViewModel.cs`
- Test: `OllamaHub.Tests/Views/OverviewGraphContractTests.cs`

**Interfaces:**
- `OverviewViewModel.TopologyJson` 输出 `endpoints`、`combos`、`providers`、`models`、`edges`。
- 新增 `OverviewComboViewModel`、`OverviewProviderViewModel`（或等价内部投影）并保持稳定 ID。

- [ ] **Step 1: 编写失败契约测试**，构造带 4 个 Provider（含无 Model Provider）、多 Endpoint/Combo 的配置，断言 JSON 五个集合、Provider 完整保留、Combo 层级和边类型。
- [ ] **Step 2: 运行定向测试确认失败**：`dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj --filter FullyQualifiedName~OverviewGraphContractTests`。
- [ ] **Step 3: 实现投影**：从 `dataStore.Providers` 建立 Provider 集合；从 `CurrentConfig.GatewayEndpoints` 展开启用/停用 Combo 与 Route；按 `ProviderId|ModelId` 去重 Model；为 `endpoint-combo`、`combo-provider`、`provider-model` 以及路由关系生成边，所有字段仅使用安全显示名和稳定标识。
- [ ] **Step 4: 重新运行定向测试并确认通过**。
- [ ] **Step 5: 提交**：`git add OllamaHub.Desktop/ViewModels/MainWindowViewModel.cs OllamaHub.Tests/Views/OverviewGraphContractTests.cs; git commit -m "完善概览四层拓扑数据投影"`。

### Task 2: 事件驱动 C#→JS 桥接与字符串枚举

**Files:**
- Modify: `OllamaHub.Desktop/Views/OverviewGraphHost.cs`
- Test: `OllamaHub.Tests/Views/OverviewGraphContractTests.cs`

**Interfaces:**
- `OverviewGraphHost` 继续实现 `IOverviewGraphHost`，使用 `JsonSerializerOptions` 注册 `JsonStringEnumConverter`。
- 页面就绪前保留最新拓扑/指标，页面就绪后按顺序调用 `window.applyTopology`、`window.applyMetrics`、`window.receiveTelemetry`。

- [ ] **Step 1: 增加失败测试**，检查宿主源码注册 `JsonStringEnumConverter`，并验证遥测脚本包含字符串 `RequestStarted` 所需的序列化结果。
- [ ] **Step 2: 运行定向测试确认失败**。
- [ ] **Step 3: 修改 JsonOptions 和发送逻辑**，保持拓扑/指标合并去重，遥测事件不轮询、不发送敏感字段；页面未就绪只丢弃旧遥测，不影响快照补发。
- [ ] **Step 4: 运行定向测试确认通过**。
- [ ] **Step 5: 提交**：`git add OllamaHub.Desktop/Views/OverviewGraphHost.cs OllamaHub.Tests/Views/OverviewGraphContractTests.cs; git commit -m "统一概览 Web 事件桥接序列化"`。

### Task 3: XAML/HTML HUD、圆角和四层布局

**Files:**
- Modify: `OllamaHub.Desktop/Views/OverviewView.axaml`
- Modify: `OllamaHub.Desktop/Assets/Overview/index.html`
- Test: `OllamaHub.Tests/Views/OverviewGraphContractTests.cs`

- [ ] **Step 1: 增加失败契约断言**：XAML 不含旧标题/说明；HTML HUD 不含 `.meta` 或“活跃边”；根容器和宿主均有裁剪/圆角声明。
- [ ] **Step 2: 运行测试确认失败**。
- [ ] **Step 3: 修改 XAML 使 WebView 直接填充 Border，设置 `ClipToBounds`；压缩 HUD 仅保留标题/数值；删除活跃边图例文字；重写 Three.js `buildGraph` 按 Endpoint、Combo、Provider、Model 分列网格，保留无 Model Provider 节点并建立关系边。
- [ ] **Step 4: 运行定向契约测试确认通过**。
- [ ] **Step 5: 提交**：`git add OllamaHub.Desktop/Views/OverviewView.axaml OllamaHub.Desktop/Assets/Overview/index.html OllamaHub.Tests/Views/OverviewGraphContractTests.cs; git commit -m "调整概览 HUD 与四层拓扑布局"`。

### Task 4: 标签缩放与统一相机交互

**Files:**
- Modify: `OllamaHub.Desktop/Assets/Overview/index.html`
- Test: `OllamaHub.Tests/Views/OverviewGraphContractTests.cs`

- [ ] **Step 1: 增加失败断言**：页面包含标签缩放函数、统一相机状态函数、滚轮仅改变距离的实现。
- [ ] **Step 2: 运行测试确认失败**。
- [ ] **Step 3: 实现 `updateLabelScale`、`setCameraState`、`fitTopology`/重置逻辑；在动画循环按相机距离限制 Sprite 最小/最大尺寸；滚轮修改相机距离后始终 lookAt 固定目标，不改 yaw/pitch。
- [ ] **Step 4: 运行定向测试确认通过**。
- [ ] **Step 5: 提交**：`git add OllamaHub.Desktop/Assets/Overview/index.html OllamaHub.Tests/Views/OverviewGraphContractTests.cs; git commit -m "统一概览标签缩放与相机行为"`。

### Task 5: 全量验证、发布与真实 Provider 检查

**Files:**
- Modify: `openspec/changes/overview-topology-app-data/tasks.md`
- Create: `docs/superpowers/reports/2026-09-03-overview-topology-app-data-verify.md`

- [ ] **Step 1: 运行全量测试**：`dotnet test OllamaHub.slnx`。
- [ ] **Step 2: 发布桌面端到 `outputs/20260903-overview-topology-app-data/`**，确认发布目录包含 Overview 资源。
- [ ] **Step 3: 按 `docs/superpowers/reports/2026-08-31-provider-launch-data-discrepancy.md` 指定 Explorer/Shell 方式启动发布包，检查真实 Provider（含无 Model Provider）和 Web 拓扑，不使用旧实例。
- [ ] **Step 4: 将验证证据写入报告，勾选 tasks.md 全部任务，并提交中文验证记录。

