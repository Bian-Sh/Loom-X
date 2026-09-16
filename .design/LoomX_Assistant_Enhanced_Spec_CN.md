# LoomX 小助手能力增强规格

> 版本：2026-09-17
> 类型：在现有小助手基础上的能力增强，不是新建一套 Agent 架构。

## 1. 先确认当前项目事实

本规格针对**已经存在的小助手实现**，不是“增加一个新的 AI Assistant”。开始实现前必须以仓库当前代码为准。

当前仓库已经存在：

- `LoomX/Assistant/AssistantService.cs`：小助手会话门面、模型选择、权限模式、会话、取消、标题摘要、AgentLoop 驱动。
- `LoomX.Harness/AgentLoop.cs`：已有 streaming、tool calling、取消、超时、最大步骤、重试、工具审批等基础循环。
- `LoomX.Harness/ToolRegistry.cs`：已有统一 Tool 定义、JSON Schema、Handler、超时和 `ToolRiskLevel`。
- `LoomX/Assistant/LoomXTools.cs`：已经存在 `loomx.*` 与 `skill.*` 工具，覆盖 status、Endpoint、Combo、Provider、Model、测试和 Skill。
- `LoomX/Assistant/SecretBoundary.cs`：已有 Secret 边界。
- `LoomX/Assistant/DiagnosticSubagent.cs`：已有诊断 Worker。
- `LoomX/Assistant/Browser/`：已有浏览器相关能力。
- `LoomX/Assistant/SkillStore.cs`：已有 Skill 加载机制。
- `LoomX/Services/GatewayProcessService.cs`：已有 Router/Gateway 启停生命周期。
- `LoomX/Services/GatewayStateHub.cs`：已有共享网关运行状态观测点。
- `LoomX/Activity/ActivityStore.cs`、`ActivityMiddleware.cs`、`RequestTelemetryHub.cs`：已有活动数据与运行时遥测基础设施。
- `LoomX.Tests/Assistant/`：已经存在大量 Assistant、Tool、Browser、Diagnostic、Session、ViewModel 测试。

因此本任务是**增强现有小助手，使其真正能够理解、操作和诊断整个 LoomX 应用**。禁止重新设计第二套助手架构。

## 2. 目标

让现有小助手从“能够调用现有 LoomX Tools 的聊天 Agent”进一步成为 LoomX 的统一自然语言操作入口。

用户应该可以直接说：

- “看看 LoomX 现在的所有设置。”
- “把这个 Provider 关掉。”
- “Router 现在运行吗？”
- “帮我启动 Router。”
- “最近请求为什么失败？”
- “分析一下刚才这个请求。”
- “把 Base URL 改成这个。”
- “把 API Key 换掉。”

小助手不需要知道 SQLite、EF Core、ViewModel、文件路径等实现细节；它只使用业务语义明确的 Function。

## 3. 四类新增/增强能力

### 3.1 完整读取 App 设置

覆盖当前 Settings 页面以及以后新增的应用级设置。

当前设计原型的设置页包括：

- 界面语言
- 主题
- 代理
- 更新
- 数据与隐私
- 关于

同时要覆盖已经属于 LoomX 配置域的：

- Gateway / Router
- Endpoint
- Combo
- Provider
- Model
- 请求相关配置
- Assistant 自身配置（模型、权限模式、Reasoning 等）

实现要求：

1. 先梳理真实设置来源，不要根据 HTML 原型猜字段。
2. UI 与小助手读取同一业务数据源。
3. 不为每一个 Toggle/TextBox 单独创建 Agent Tool。
4. 优先提供聚合读取能力，例如 `get_app_settings`，必要时再提供领域级查询。
5. 返回给模型的数据必须是安全摘要，Secret 只返回配置状态或引用，不返回明文。

### 3.2 设置 App 配置

小助手可以修改用户有权修改的应用设置。

优先复用已有 Configuration Service / Repository / Preferences Store / Runtime Service。

修改流程统一为：

```text
读取当前值
  ↓
判断目标与差异
  ↓
验证参数
  ↓
创建必要备份
  ↓
写入现有数据源
  ↓
刷新运行时状态
  ↓
发布状态变化
  ↓
UI 自动反映新状态
```

不要：

- LLM 生成 SQL
- Tool 直接操作 SQLite 文件
- Agent 直接修改 ViewModel 字段作为持久化手段
- Agent 模拟点击设置页面
- 为 AI 建立第二套配置缓存

### 3.3 Router / Gateway 启停

已有 `GatewayProcessService` 和 `GatewayStateHub`，增强时应直接复用。

至少提供稳定的自然语言能力：

```text
查询当前状态
启动 Router
停止 Router
重启 Router
必要时重新加载配置
```

重点处理已有状态：

```text
Stopped
Starting
Running
Stopping
Failed
```

操作后应返回结构化状态，并让 UI 获得同一状态。

不要让 Agent 通过 Process、PowerShell、CMD 或其他 Shell 自己启动 LoomX Router。

### 3.4 Activity / Console 数据读取与分析

已有 ActivityStore、ActivityMiddleware、RequestTelemetryHub 和 Console/Log 页面，不要再建立第二套日志系统。

小助手需要获得：

- 最近请求
- 请求时间
- Endpoint
- Combo
- Provider
- Model
- 成功/失败
- HTTP 状态
- 延迟
- Token/统计（项目已有数据才暴露）
- 请求详情中的安全诊断信息
- 最近 Console/Log 错误
- 时间范围查询
- 关键词/级别过滤

查询必须有限制：

```text
limit
时间范围
分页或最近 N 条
必要字段投影
```

禁止把整个 Activity 数据库或整个日志文件直接塞进模型上下文。

## 4. Function 设计

不要机械增加几十个函数。先盘点现有 `LoomXTools.RegisterAll`，复用已经存在的工具。

优先补齐缺口：

```text
loomx.get_app_settings
loomx.update_app_settings

loomx.get_router_status
loomx.start_router
loomx.stop_router
loomx.restart_router
loomx.reload_router_config

loomx.get_recent_activity
loomx.get_activity_detail
loomx.get_activity_summary
loomx.get_console_logs
loomx.get_recent_errors
```

最终命名以项目现有命名规范为准。

如果现有 Tool 已经能完成某项能力，不新增重复 Tool；改进其 schema、输出或组合方式即可。

## 5. 风险与二次确认

仓库当前 `ToolRiskLevel` 已有：

```text
Read
Write
External
Destructive
Secret
```

同时 `AgentLoop` 已有 `ToolApprovalGate` / `ToolApprovalRequested`，`AssistantService` 已有 `PermissionMode` 和 UI 审批桥。

本次增强应把它们真正用于应用级风险控制，而不是重新实现审批系统。

建议规则：

| 操作 | 风险 | 默认行为 |
| --- | --- | --- |
| 读取设置 | Read | 直接执行 |
| 读取 Activity / Console | Read | 直接执行 |
| 普通配置修改 | Write | 按当前权限模式 |
| API Key / Token 修改 | Secret | 必须明确确认 |
| 删除 Provider / Model | Destructive | 必须确认 |
| 停止/重启 Router | Runtime Control | 按应用策略确认 |
| 不可逆数据清理 | Destructive | 必须确认 |

关键要求：**风险由应用/Tool 元数据定义，不由 LLM 自己判断。**

确认发生在副作用真正执行之前。

## 6. Secret 边界

继续使用现有 `SecretBoundary`。

任何 ToolResult、AgentEvent、Session Persistence、普通日志都不能携带 API Key 明文。

允许返回：

```json
{
  "configured": true,
  "secret_ref": "secret://provider/..."
}
```

用户要求“修改 API Key”时，可以接收用户输入并安全写入现有 Secret 存储流程，但不能为了让模型“确认成功”而把明文重新返回。

## 7. UI 状态同步

不要增加“AI 刷新 UI”这种 Tool。

正确模型：

```text
小助手 Function
  ↓
现有 Application Service
  ↓
Persistence / Runtime
  ↓
现有 State / Event
  ↓
Avalonia ViewModel 感知变化
  ↓
UI 更新
```

如果当前某个页面只有“保存后手动重新读取”的实现，可以补充明确的状态通知机制，但应首先复用已有 MVVM / Observable / Event 体系。

目标是：

> AI 改完配置，用户不需要重新打开页面，就能看到真实的新状态。

## 8. Activity 与 Console 的分析闭环

例如：

```text
用户：刚才这个请求为什么失败？

读取最近 Activity
 ↓
定位目标 Request
 ↓
读取安全详情
 ↓
关联 Endpoint / Combo / Provider / Model
 ↓
必要时读取对应时间窗口 Console
 ↓
组合上下文
 ↓
模型分析
 ↓
给出事实、原因候选和可执行建议
```

不要在 Tool 层硬编码“某种 HTTP 错误一定代表某种原因”；Tool 提供事实，模型负责综合分析，诊断 Worker 负责需要主动探测的网络/API 检查。

## 9. 与已有 Skills 的关系

Skill 继续保持“知识 + 调用规则”的定位。

例如：

```text
Skill
  ↓
告诉小助手 Codex / Claude Code / 中转站的配置知识
  ↓
调用 LoomX Tools
  ↓
完成外部系统配置
```

本任务不把 App 设置、Router、Activity、Console 再包装成 Skill；这些是 LoomX 自身原生能力，应由 Tool 暴露。

## 10. 实施顺序

### Phase A：代码盘点

先阅读并记录真实实现：

- `LoomX/Assistant/AssistantService.cs`
- `LoomX/Assistant/LoomXTools.cs`
- `LoomX/Assistant/SecretBoundary.cs`
- `LoomX.Harness/AgentLoop.cs`
- `LoomX.Harness/ToolRegistry.cs`
- `LoomX/Services/GatewayProcessService.cs`
- `LoomX/Services/GatewayStateHub.cs`
- `LoomX/Activity/ActivityStore.cs`
- `LoomX/Activity/ActivityModels.cs`
- Console/Log 对应实现
- Settings 页面及对应 ViewModel/Store
- `LoomX.Tests/Assistant/*`

先回答：哪些能力已经存在，哪些只缺 Tool 暴露，哪些真的缺 Service。

### Phase B：补齐 App Control Tools

优先：

```text
App Settings Read
App Settings Write
Router Status
Router Start/Stop/Restart
```

### Phase C：补齐 Observability Tools

优先：

```text
Activity Query
Activity Detail
Activity Summary
Console Query
Recent Errors
```

### Phase D：安全与状态同步

验证：

```text
普通读取 → 自动
普通修改 → 正常执行
敏感修改 → 二次确认
删除 → 二次确认
Runtime 高影响操作 → 应用策略
修改后 → Runtime 与 UI 一致
```

### Phase E：Agent 场景验证

至少验证：

```text
看看现在有哪些 Provider。

把 xxx Provider 关掉。

Router 现在运行吗？

帮我启动 Router。

看看最近有没有失败请求。

分析一下刚才这个请求为什么失败。

把 xxx Provider 的 Base URL 改成 xxx。

修改 API Key。
```

## 11. 测试要求

新增测试优先放在现有 `LoomX.Tests/Assistant/`，不要另建测试体系。

至少覆盖：

1. 设置完整读取。
2. 普通设置修改。
3. Provider/Model/Endpoint/Combo 修改后现有 Tool 行为不回归。
4. Router 状态读取。
5. Router 启停状态同步。
6. Activity 查询有限制。
7. Console 查询有限制。
8. Activity 与 Console 可以用于同一请求诊断。
9. Secret 永不出现在 ToolResult。
10. Secret 永不进入 Session 持久化。
11. 敏感操作触发审批。
12. 用户拒绝后不产生副作用。
13. 非法参数被拒绝。
14. UI 状态来源与 AI 操作后的真实状态一致。

现有测试已经覆盖大量 Assistant / Tool / Secret / Diagnostic 能力，因此先扩展已有测试，不要重复测试基础 Harness 已经验证过的行为。

## 12. 非目标

本次不要：

- 新建 Agent Runtime。
- 新建独立 AI 后台服务。
- 新建第二套数据库。
- 新建第二套 UI 状态系统。
- 把所有设置控件变成独立 Tool。
- 让模型直接执行 SQL。
- 让模型通过 Shell 操作 LoomX 内部状态。
- 重写现有 Assistant / Harness。
- 因为这个需求重新引入 WebView。
- 为了“统一”而破坏已有 `LoomXTools`、`AssistantService`、`GatewayProcessService` 等成熟实现。

## 13. 最终总结

本需求的本质不是“增加一个 AI”，而是**增强现有小助手对 LoomX 本体的可见性、可操作性和可诊断性**。

实现上可以把它理解为：让已有应用服务逐步拥有稳定的自然语言 Function 入口，同时把持久化、运行时状态、活动数据和 UI 状态保持在同一真实数据流中。

产品概念上，最终希望现有小助手成为 LoomX 的“Brain”，但代码、类名、服务名和文档实现层都不应因此引入 Brain 命名体系。
