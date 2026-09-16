# Codex 启动 Prompt：增强现有 LoomX 小助手

你现在负责在 **LoomX** 仓库中继续开发现有“小助手”。

## 0. 先纠正一个前提

这是**增强现有实现**，绝对不是从零创建 AI Assistant。

开始前必须先阅读真实代码，再决定修改什么。

当前仓库已经存在完整的小助手基础设施：

- `LoomX/Assistant/AssistantService.cs`
- `LoomX/Assistant/LoomXTools.cs`
- `LoomX/Assistant/SecretBoundary.cs`
- `LoomX/Assistant/DiagnosticSubagent.cs`
- `LoomX/Assistant/SkillStore.cs`
- `LoomX/Assistant/Browser/`
- `LoomX.Harness/AgentLoop.cs`
- `LoomX.Harness/AgentSession.cs`
- `LoomX.Harness/ToolRegistry.cs`
- `LoomX.Harness/AgentEvent.cs`
- `LoomX/Services/GatewayProcessService.cs`
- `LoomX/Services/GatewayStateHub.cs`
- `LoomX/Activity/ActivityStore.cs`
- `LoomX/Activity/RequestTelemetryHub.cs`
- `LoomX.Tests/Assistant/`

尤其注意：现有 `LoomXTools` 已经拥有 Endpoint、Combo、Provider、Model、测试、Skill 等能力；现有 Harness 已经支持 streaming、tool calling、重试、取消、审批、事件；现有 Router 也已经有 `GatewayProcessService` 生命周期控制和 `GatewayStateHub`。

**不要重做这些已有能力。**

---

## 1. 本次真正要做的事情

让现有小助手进一步成为 LoomX 的统一自然语言操作入口。

用户可以直接说：

```text
看看 LoomX 现在的所有设置。

把这个 Provider 关掉。

Router 现在运行吗？

帮我启动 Router。

看看最近请求为什么失败。

分析一下刚才这个请求。

把 Base URL 改成这个。

把 API Key 换掉。
```

核心不是增加聊天能力，而是**让现有 Agent 看得见、改得动、查得清 LoomX 本身**。

---

## 2. 第一阶段：先考察，不要立即写代码

请先阅读：

1. `AGENTS.md`
2. `.design/AGENTS.md`
3. `.design/LoomX_Assistant_Spec_CN_v2.md`
4. `.design/LoomX_Assistant_Dev_Prompt_CN_v2.md`
5. `LoomX/Assistant/AssistantService.cs`
6. `LoomX/Assistant/LoomXTools.cs`
7. `LoomX/Assistant/SecretBoundary.cs`
8. `LoomX/Assistant/DiagnosticSubagent.cs`
9. `LoomX/Assistant/SkillStore.cs`
10. `LoomX.Harness/*`
11. `LoomX/Services/GatewayProcessService.cs`
12. `LoomX/Services/GatewayStateHub.cs`
13. `LoomX/Activity/*`
14. Settings 页面及真实对应的 ViewModel / Store
15. Console 页面及真实日志来源
16. `LoomX.Tests/Assistant/*`

然后输出一份简短盘点：

```text
已经存在：...
只缺 Tool：...
只缺 Application Service：...
需要补 UI 状态通知：...
需要补测试：...
```

**如果现有代码已经能完成某件事情，就不要重新实现。**

---

## 3. App Settings

实现小助手读取和修改当前 Settings 页面中的业务配置。

先根据真实代码确认设置项，不要从设计稿猜。

当前原型设置页涉及：

- Language
- Theme
- Proxy
- Update
- Data & Privacy
- About

同时要正确区分这些与 Router / Endpoint / Provider / Model 等配置域。

推荐提供聚合能力：

```text
loomx.get_app_settings
loomx.update_app_settings
```

但如果现有 Service 已经有更合理的领域接口，直接复用，不要为了统一而强行增加聚合 Service。

不要为每个 UI 控件增加一个 Tool。

### 修改流程

```text
Read
 ↓
Validate
 ↓
Backup if needed
 ↓
Persist
 ↓
Refresh runtime if applicable
 ↓
Publish state change
 ↓
UI observes and refreshes
```

---

## 4. Router / Gateway 控制

现有：

```text
GatewayProcessService
GatewayStateHub
```

已经提供 Router/Gateway 生命周期能力。

不要创建新的 Router Manager。

小助手需要能够：

```text
loomx.get_router_status
loomx.start_router
loomx.stop_router
loomx.restart_router
loomx.reload_router_config
```

具体名字按现有 `LoomXTools` 规范决定。

需要正确处理：

```text
Stopped
Starting
Running
Stopping
Failed
```

并保证 Tool 返回的状态与 `GatewayStateHub` / `GatewayProcessService` 一致。

**禁止：**

```text
AI → PowerShell
AI → CMD
AI → Process.Start
AI → 自己启动 LoomX
```

Tool 必须调用现有生命周期 Service。

---

## 5. Activity

现有：

```text
LoomX/Activity/ActivityStore.cs
LoomX/Activity/ActivityModels.cs
LoomX/Activity/RequestTelemetryHub.cs
LoomX/Activity/ActivityMiddleware.cs
```

小助手需要能够读取：

```text
最近请求
请求详情
成功/失败
HTTP 状态
延迟
Endpoint
Combo
Provider
Model
Token/统计（已有字段才暴露）
```

建议能力：

```text
loomx.get_recent_activity
loomx.get_activity_detail
loomx.get_activity_summary
```

查询必须有：

```text
limit
时间范围
必要过滤
```

禁止无界读取 Activity 数据库。

---

## 6. Console / Log

小助手需要能够读取与诊断有关的 Console / Log。

支持：

```text
最近日志
错误日志
Warning/Error
时间过滤
关键词过滤
```

建议：

```text
loomx.get_console_logs
loomx.get_recent_errors
```

不要把完整日志文件送给模型。

应提供“模型需要的信息”，而不是“日志文件原文”。

同时必须脱敏：

```text
API Key
Authorization
Cookie
Token
Secret
```

---

## 7. 诊断闭环

最终应该支持：

```text
用户：刚才这个请求为什么失败？

get_recent_activity
        ↓
找到目标请求
        ↓
get_activity_detail
        ↓
关联 Provider / Model / Endpoint / Combo
        ↓
必要时 get_console_logs
        ↓
必要时 loomx.diagnose
        ↓
模型综合分析
        ↓
中文回答
```

注意：

- Tool 提供事实。
- DiagnosticSubagent 做主动探测。
- 模型负责综合解释。

不要把复杂诊断逻辑全部塞进查询 Tool。

---

## 8. 安全确认

仓库当前已经存在：

```text
ToolRiskLevel
ToolApprovalGate
ToolApprovalRequested
AssistantPermissionMode
ApprovalHandler
SecretBoundary
```

继续使用这些机制。

不要另造 ConfirmService 除非真实架构明确需要。

风险至少区分：

```text
Read
Write
External
Destructive
Secret
```

建议：

```text
读取设置          Read       自动
读取 Activity     Read       自动
读取 Console      Read       自动
普通配置修改      Write      遵循当前权限模式
修改 API Key      Secret     必须确认
删除 Provider     Destructive 必须确认
删除 Model        Destructive 必须确认
停止/重启 Router   高影响      遵循应用策略
```

关键要求：**不能让 LLM 自己决定是否危险。**

风险等级来自 Tool / 应用策略。

确认必须发生在副作用执行之前。

---

## 9. Secret

现有 `SecretBoundary` 是唯一安全边界之一，继续使用。

ToolResult / AgentEvent / Session Persistence / Log 中绝不能出现 Secret 明文。

允许：

```json
{
  "configured": true,
  "secret_ref": "secret://provider/..."
}
```

不要为了方便模型判断而回显 API Key。

---

## 10. UI 刷新

**不要实现 `refresh_ui` Tool。**

AI 不应该操作页面按钮。

正确方向：

```text
Assistant Tool
 ↓
Application Service
 ↓
Persistence / Runtime
 ↓
State / Event
 ↓
ViewModel
 ↓
UI
```

例如 AI 修改 Provider：

```text
update_provider
 ↓
ConfigurationManagementService
 ↓
SQLite
 ↓
DatabaseConfigurationProvider / Runtime Snapshot
 ↓
状态变化通知
 ↓
Provider 页面更新
```

如果当前 UI 缺少通知机制，补通知机制；不要让 Agent 自己“刷新页面”。

---

## 11. 不要重复造 Tool

当前 `LoomXTools.cs` 已经有大量能力，包括：

```text
loomx.get_status
loomx.list_endpoints
loomx.get_endpoint
loomx.create_endpoint
loomx.update_endpoint
loomx.delete_endpoint

loomx.list_combos
loomx.get_combo
loomx.create_combo
loomx.update_combo
loomx.delete_combo

loomx.list_providers
loomx.get_provider
loomx.create_provider
loomx.update_provider
loomx.delete_provider

loomx.list_models
loomx.get_model
loomx.create_model
loomx.update_model
loomx.delete_model

loomx.test_provider
loomx.test_model
loomx.test_endpoint

skill.list
skill.load
```

先检查这些工具能否组合完成需求。

只补真正缺失的：

```text
App Settings
Router lifecycle
Activity query/detail/summary
Console query/recent errors
```

---

## 12. 测试

不要重新建立测试项目。

继续使用：

```text
LoomX.Tests/Assistant/
```

现有测试已经覆盖：

- AgentLoop
- Retry
- AssistantService
- Session Store
- ViewModel
- Browser Bridge
- Browser Tools
- Secret Harvester
- Diagnostic Subagent
- LoomXTools

因此本次只增加新增能力的测试。

至少覆盖：

```text
get_app_settings
update_app_settings

get_router_status
start_router
stop_router
restart_router

get_recent_activity
get_activity_detail
get_activity_summary

get_console_logs
get_recent_errors
```

以及：

```text
Secret 不泄漏
确认前不产生副作用
拒绝后不产生副作用
非法参数失败
查询有上限
修改后 Runtime 状态一致
修改后 UI 状态最终一致
```

---

## 13. 开发纪律

不要大规模重构。

不要新建：

```text
BrainService
BrainApi
BrainDatabase
AgentDatabase
LoomXControlPlane
```

不要建立第二套状态管理。

不要因为 AI 而重写现有 Harness。

不要把成熟的 `LoomXTools`、`AssistantService`、`GatewayProcessService` 推倒重来。

不要使用 Shell 操作 LoomX 内部状态。

不要引入 WebView。

---

## 14. 实施顺序

### Step 1

完成代码盘点并给出计划。

### Step 2

补齐 App Settings Tool。

### Step 3

补齐 Router lifecycle Tool。

### Step 4

补齐 Activity / Console Tool。

### Step 5

接入已有审批与 SecretBoundary。

### Step 6

补状态通知，使 UI 自动反映 AI 修改。

### Step 7

补测试并运行：

```powershell
dotnet build LoomX.slnx
dotnet test LoomX.Tests\LoomX.Tests.csproj
```

### Step 8

用真实自然语言场景验证。

---

## 15. 最终验收

以下场景必须可以通过现有“小助手”完成：

```text
看看 LoomX 现在有哪些设置。

看看现在有哪些 Provider。

把 xxx Provider 关掉。

Router 现在运行吗？

帮我启动 Router。

看看最近有没有失败请求。

分析一下刚才这个请求为什么失败。

把 xxx Provider 的 Base URL 改掉。

修改 API Key。
```

其中 API Key、删除和高影响操作必须走应用级安全确认。

## 最终总结

目标不是增加一个新的 Agent，而是**增强现有小助手对 LoomX 本体的控制能力**：读取设置、修改设置、控制 Router、读取 Activity、读取 Console，并让这些操作自然地复用现有 Application Service、Persistence、Runtime 和 UI 状态机制。

产品概念上，这使现有小助手逐渐成为 LoomX 的“Brain”；实现层面不要出现 Brain 命名体系。
