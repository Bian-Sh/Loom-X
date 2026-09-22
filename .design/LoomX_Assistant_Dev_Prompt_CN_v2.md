# LoomX 小助手开发启动 Prompt v2

你现在负责在现有 LoomX 项目中实现“小助手”。

## 最高优先级

LoomX 是：

> **纯粹、智能的 AI Router。**

小助手只是 LoomX 的智能配置、操作、诊断与连接层。

禁止把 LoomX 做成：
- 通用 Agent Runtime
- OpenClaw
- Agent Marketplace
- 多 Agent 编排平台
- 通用桌面 Agent
- 通用浏览器 Agent

## 1. 先读现有项目

开始开发前：
1. 阅读 LoomX 当前项目结构。
2. 阅读 AGENTS.md。
3. 理解 Router / Provider / Model / Endpoint / Combo 数据模型。
4. 理解现有 NodeGraph。
5. 理解现有 Secret/API Key 存储。
6. 不破坏已有 Router 功能。
7. 不为了“小助手”重构整个 LoomX。

## 2. 硬约束：不要 WebView

此前 WebView 用于 Three.js 3D 拓扑图，现在已经废弃。

不要使用 WebView：
- 做 Chat
- 做 Markdown
- 做 NodeGraph
- 做浏览器
- 做 LoomX 主 UI

使用：
```text
Avalonia Native UI
+
NodeGraph
+
Native Chat
```

浏览网页使用：
```text
Chrome
+
Chrome Extension
+
Local Browser Bridge
```

## 3. Harness 必须分三步

不要一次性实现全部功能。

### Phase 1：最小 C# Harness

只实现：
```text
AgentSession
AgentLoop
ModelClient
ToolRegistry
AgentEvent
```

支持：
- streaming
- tool calling
- tool result
- cancellation
- timeout
- max steps
- error recovery
- session lifecycle
- structured events

先使用 Mock Tool 验证。

验收：
```text
用户 → Model → Tool → Tool Result → Model → Answer
```

只有 Phase 1 稳定后才能进入 Phase 2。

### Phase 2：LoomX Tools + Skills

实现：
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
```

不要创建：
```text
CodexAdapter
ClaudeAdapter
OpenCodeAdapter
NewApiAdapter
Sub2ApiAdapter
```

客户端和中转站知识放入 Skill。

### Phase 3：Browser Bridge + Diagnostic Subagent

最后才接 Chrome 和诊断 Subagent。

## 4. Skills

Skill = 知识 + 调用规则。

建议：
```text
<InstallDirectory>\Skills\
  loomx\
  providers\
  relays\
  clients\
```

Skill 描述：
- 目标识别
- 配置路径
- 配置结构
- 修改方法
- 备份
- 恢复
- 验证
- 测试
- 重启
- Tool 使用方式

Skill 不是插件，不直接成为任意代码执行环境。

## 5. Chrome Browser Bridge

参考 LiveAgent：
https://github.com/Stack-Cairn/LiveAgent/blob/main/crates/agent-gui/browser-extension/background.js

采用：
```text
Chrome MV3
 ↓ WebSocket localhost
LoomX Browser Bridge
 ↓
CDP-like protocol
 ↓
chrome.debugger
 ↓
Chrome Tab
```

至少支持：
```text
Target.getTargets
Target.createTarget
Target.attachToTarget
Target.closeTarget
```

Session-level command 通过：
```text
chrome.debugger.sendCommand
```

事件通过：
```text
chrome.debugger.onEvent
```

维护：
```text
targetId
sessionId
tabId
```

默认只允许 LoomX 创建/登记的 automation tabs。

## 6. All API Hub

参考：
https://github.com/qixing-jk/all-api-hub

不要复制代码。

将它作为 AI 中转站生态知识参考，重点：
```text
New-API
Sub2API
One-API
OpenAI Compatible
API Key
Base URL
Models
Health Check
Availability
Channel
Account
```

最终沉淀为：
```text
Skills/relays/
Skills/providers/
```

而不是 C# Adapter。

## 7. Browser Tools

最小：
```text
browser.tabs
browser.open
browser.read
browser.click
browser.type
browser.wait
browser.screenshot
browser.network
```

尤其重视：
```text
browser.network
```

因为真实中转站的 Base URL / API Path / Auth / Model 可能从网络请求比网页文本更可靠地获得。

但 Secret 必须经过 SecretBoundary。

## 8. SecretBoundary

绝对禁止：
```text
API Key
 ↓
LLM Context
```

必须：
```text
API Key
 ↓
Secret Store
 ↓
secret_ref
 ↓
Model
```

模型只能看到：
```json
{
  "configured": true,
  "secret_ref": "secret://..."
}
```

Secret 不允许进入：
- Chat
- History
- AgentEvent
- Log
- Debug
- Exception
- Telemetry

## 9. 中转站自动配置

用户：
> 帮我把这个中转站配置到 LoomX。

自动流程：
```text
识别
 ↓
Load Skill
 ↓
Chrome
 ↓
登录/授权
 ↓
发现 API
 ↓
Secret Store
 ↓
Provider
 ↓
Models
 ↓
Combo
 ↓
Test
 ↓
Verify
```

正常步骤不要逐步询问用户。

只在：
- 登录
- CAPTCHA
- 2FA
- 明确授权
- 高风险不可逆操作

时 WaitingForUser。

## 10. Diagnostic Subagent

只作为：
> Provider / Model / Network Diagnostic Worker

典型：
```text
loomx.test_provider
 ↓
Diagnostic Subagent
 ↓
DNS / TLS / HTTP / Auth / Model / Chat
 ↓
Structured Result
 ↓
Main Assistant
```

禁止把它扩展为 Multi-Agent Platform。

## 11. Chat UI

参考 Aily Chat：
https://github.com/ailyProject/aily-blockly/tree/master/src/app/tools/aily-chat

只借鉴：
- 单窗口 Chat
- Session
- History
- Streaming
- 外部文本注入
- Tool/状态消息

不要复制 Angular/NG-Zorro。

LoomX 使用 Avalonia Native UI。

布局：
```text
┌───────────────────────────────┐
│                    历史   ＋  │
│                               │
│ 用户：帮我测试 Provider       │
│                               │
│ 助手：                         │
│ ✓ 已读取配置                  │
│ ✓ 已找到 Provider             │
│ ● 正在测试                    │
│                               │
│ ┌───────────────────────────┐ │
│ │ 输入消息...               │ │
│ └───────────────────────────┘ │
└───────────────────────────────┘
```

不要巨大 Agent Sidebar。

不要展示 Chain of Thought。

只展示安全的 Action Summary。

## 12. Markdown

不要自行实现。

不要 WebView。

优先：
https://github.com/DearVa/LiveMarkdown.Avalonia

备选：
https://github.com/whistyun/Markdown.Avalonia

## 13. Event

定义：
```text
SessionStarted
TextDelta
ToolCallStarted
ToolCallCompleted
SkillLoaded
SubagentStarted
SubagentCompleted
TaskCompleted
TaskFailed
WaitingForUser
```

可扩展：
```text
SecretStored
BrowserTargetCreated
BrowserTargetClosed
ConfigBackupCreated
ConfigChanged
TestStarted
TestCompleted
```

UI 使用：
```text
AgentEvent
 ↓
Activity Projection
 ↓
UI
```

不要直接绑定 Harness 内部状态。

## 14. Tool Registry

每个 Tool 具备：
```text
Name
Description
JSON Schema
Handler
Timeout
Risk Level
```

Risk：
```text
Read
Write
External
Destructive
Secret
```

默认自动执行 Read / Write / External。

## 15. 配置修改原则

统一：
```text
Read
 ↓
Backup
 ↓
Patch
 ↓
Validate
 ↓
Test
 ↓
Restart if necessary
 ↓
Verify
```

不要直接覆盖配置。

## 16. 开发顺序

严格执行：

### Step 1
```text
C# Harness
↓
Mock Tool
↓
Streaming Chat
↓
Tool Calling
```

### Step 2
```text
LoomX Tools
+
Skills
+
SecretBoundary
+
真实配置读写
+
Provider Test
```

### Step 3
```text
Chrome Extension
+
Browser Bridge
+
CDP-like Session
+
Relay Skill
+
真实网页配置
+
Diagnostic Subagent
```

UI 可以先做基础骨架，但不能让 UI 反过来决定 Harness 架构。

## 17. 开发纪律

每完成一个阶段：
1. 编译
2. 单元测试
3. 最小 E2E
4. 修复
5. 再进入下一阶段

禁止：
```text
先把全部代码写出来
最后一起调
```

不要提前建设：
- Plugin SDK
- Agent Marketplace
- Multi-Agent Graph
- MCP Marketplace
- Browser Plugin Framework
- Provider Adapter Framework

## 18. 最终判断标准

每个设计决策都问：

> 这是否让 LoomX 成为一个更聪明、更可靠的 AI Router？

如果不是，不加入。

如果某功能让 LoomX 越来越像：
```text
OpenClaw
通用 Agent 平台
桌面 Agent
浏览器 Agent
```
立即停止并重新收敛。

最终：
```text
LoomX
│
├── Router Core
├── NodeGraph
├── Activity
│
└── 小助手
    ├── C# Harness
    ├── Skills
    ├── Tools
    ├── Browser Bridge
    ├── SecretBoundary
    └── Diagnostic Subagent
```

**现在只开始 Phase 1，不要提前实现 Phase 2 / Phase 3。**
