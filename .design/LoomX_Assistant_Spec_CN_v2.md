# LoomX 小助手：产品与技术规格 v2

> 版本：2026-09-09
> 定位：**LoomX 是一个纯粹、智能的 AI Router。**

## 1. 本次修订结论

### 1.1 坚决不再使用 WebView

WebView/HTML/Three.js 不再承担 LoomX 核心 UI。

此前 WebView 用于 3D 拓扑图；现在已经改为 Native NodeGraph，因此应继续沿这条路线收敛：

```text
LoomX
├── Native Avalonia UI
│   ├── NodeGraph
│   ├── Router / Activity
│   └── 小助手 Chat
└── C# Backend
    ├── Router Core
    ├── Harness
    ├── Tools
    ├── Skills
    └── Browser Bridge
```

网页浏览只发生在用户自己的 Chrome 中，由 Chrome Extension + Local Browser Bridge 完成。

因此：
- 不为了 Markdown 引入 WebView
- 不为了 Chat 引入 WebView
- 不为了浏览器自动化嵌入浏览器
- 不重新引入 Three.js 拓扑
- NodeGraph 是 Router Runtime Visualization
- 小助手 Chat 是 Native Avalonia Chat

## 2. 产品边界

LoomX 不是：
- 通用 Agent 平台
- OpenClaw 替代品
- 插件市场
- 多 Agent 编排平台
- 通用浏览器 Agent
- 通用桌面自动化框架

LoomX 是：

> 一个负责统一管理、路由、诊断和连接各种 AI Provider / Model / Endpoint 的智能 AI Router。

“小助手”不是另一个产品，而是 LoomX 的**智能控制层**。

它负责：
- 配置 LoomX
- 测试 Provider / Model / Endpoint
- 诊断网络与 API
- 连接 AI 中转站
- 配置现有 AI Client（如 Codex / Claude Code）
- 必要时通过 Chrome 完成真实网页操作

## 3. 核心模型

保持：

```text
Endpoint
   ↓
 Combo
   ↓
Provider
   ↓
 Model
```

运行时：

```text
Client
  ↓
Endpoint
  ↓
Combo
  ↓
Provider
  ↓
Model
  ↓
Upstream AI Service
```

NodeGraph 负责把 Router 运行状态可视化。

小助手可以读取/修改这些实体，但不能把 NodeGraph 变成 Agent 执行图。

# 4. 小助手架构

```text
┌──────────────────────────────────────────┐
│                 LoomX                    │
│                                          │
│  Router Core        NodeGraph            │
│       │                │                 │
│       └───────┬────────┘                 │
│               │                          │
│          小助手 Chat                      │
│               │                          │
│        ┌──────▼──────┐                   │
│        │ C# Harness  │                   │
│        └──────┬──────┘                   │
│               │                          │
│    ┌──────────┼───────────┐              │
│    │          │           │              │
│  Skills     Tools    Browser Bridge      │
│    │          │           │              │
│    │      LoomX API      │               │
│    │      File/Process   │               │
│    │                      │               │
└────┼──────────────────────┼───────────────┘
     │                      │
     │                  Chrome Extension
     │                      │
     │                  Chrome Browser
     │                      │
     └──────── AI/Provider/Relay Web ──────┘
```

# 5. Harness：严格三阶段

不要一次性开发完整 Agent Runtime。

## Phase 1：最小 C# Harness

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
- max steps
- timeout
- error recovery
- session lifecycle
- structured events

最小循环：

```text
User
 ↓
Model
 ↓
Tool Call?
 ├─ No → Answer
 └─ Yes
      ↓
   Tool Execute
      ↓
   Tool Result
      ↓
     Model
```

验收：

```text
用户：查看一下 LoomX 当前有哪些 Provider。
助手：
→ loomx.list_providers
→ 中文总结
```

Phase 1 稳定后才能进入 Phase 2。

## Phase 2：LoomX Tools + Skills

加入真实生产能力。

### LoomX Tools

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

Tool 是能力；Skill 不实现这些函数。

## Phase 3：Browser Bridge + Diagnostic Subagent

最后才接 Chrome 与诊断 Subagent。

# 6. Skills

Skill = **知识 + 调用规则**。

不是插件，不直接成为任意代码执行环境。

建议：

```text
<InstallDirectory>\Skills
  loomx    manifest.json
    SKILL.md

  providers    generic-openai-compatible      manifest.json
      SKILL.md

  relays    new-api      manifest.json
      SKILL.md

  clients    codex      manifest.json
      SKILL.md

    claude-code      manifest.json
      SKILL.md
```

Skill 描述：
- 什么时候加载
- 如何识别目标
- 配置文件在哪里
- 配置结构
- 如何备份
- 如何修改
- 如何恢复
- 如何验证
- 如何测试
- 是否需要重启
- 应调用哪些 Tool
- 完成后如何清理现场

不要建立 CodexAdapter / ClaudeAdapter / OpenCodeAdapter / NewApiAdapter / Sub2ApiAdapter 等 Adapter 地狱。

正确模型：

```text
Tool
 +
Skill
 +
Config File
 +
Browser Bridge
```

# 7. Chrome Browser Bridge

参考 LiveAgent 的 Browser Bridge：

https://github.com/Stack-Cairn/LiveAgent/blob/main/crates/agent-gui/browser-extension/background.js

其关键设计非常适合 LoomX：

```text
Chrome MV3 Extension
       │
       │ WebSocket localhost
       ▼
LoomX Browser Bridge
       │
       ▼
CDP-like session
       │
       ▼
chrome.debugger
       │
       ▼
Chrome Tab
```

建议保持 CDP 语义：
- Target.getTargets
- Target.createTarget
- Target.attachToTarget
- Target.closeTarget
- session-level `chrome.debugger.sendCommand`
- `chrome.debugger.onEvent`

Extension 与 LoomX 之间维护：

```text
targetId
sessionId
tabId
```

的映射。

默认只允许 LoomX 创建/登记的 automation tabs，不能默认读取用户所有 Chrome Tab。

## Extension 的职责

只负责：

```text
Chrome API
chrome.debugger
chrome.tabs
WebSocket
Target/session mapping
```

不要在 Extension 中实现：
- Provider 业务
- Relay 业务
- AI Agent
- Skill
- Model discovery business logic

这些属于 LoomX。

# 8. All API Hub：作为真实中转生态知识来源

参考：

https://github.com/qixing-jk/all-api-hub

不要复制其代码，而是吸收其对真实 AI 中转站生态的认知：

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

这些知识应最终沉淀为：

```text
Skills/relays/
Skills/providers/
```

而不是 C# Adapter。

重点关注真实网页中的：
- 登录
- API Key 创建/选择
- Base URL
- 模型列表
- 可用性
- 渠道
- 账户状态
- 网络请求

# 9. Browser Tools

最小能力：

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

其中 `browser.network` 很重要。

真实中转站的：

```text
网页
 ↓
XHR / fetch
 ↓
API
 ↓
Request
 ↓
Base URL
 ↓
Authorization
 ↓
Model
```

往往比网页展示文本更可靠。

因此小助手必要时应能够从网络请求发现：

```text
Base URL
API Path
Method
Headers
Model ID
Authentication
```

但 Secret 必须经过 SecretBoundary。

# 10. Secret Boundary

必须独立于 Harness。

错误：

```text
Browser
 ↓
API Key
 ↓
Tool Result
 ↓
LLM Context
```

正确：

```text
Browser
 ↓
API Key
 ↓
Secret Store
 ↓
secret_ref
 ↓
LLM
```

模型只能看到：

```json
{
  "configured": true,
  "secret_ref": "secret://provider/..."
}
```

Secret 不允许进入：
- Chat History
- Agent Event
- Tool Result
- Debug Log
- Exception Message
- Telemetry

# 11. 中转站自动配置

用户：

> 帮我把这个中转站配置到 LoomX。

小助手应能够：

```text
识别网站
 ↓
Load relay Skill
 ↓
Browser.open
 ↓
判断登录
 ↓
必要时 WaitingForUser
 ↓
找到/创建 API Key
 ↓
Key → Secret Store
 ↓
发现 Base URL
 ↓
发现 Models
 ↓
创建 Provider
 ↓
创建 Models
 ↓
创建 Combo
 ↓
test_provider
 ↓
test_model
 ↓
test_endpoint
 ↓
判断是否需要重启
 ↓
完成
```

正常步骤不要逐步询问用户。

只在：
- 登录
- CAPTCHA
- 2FA
- 明确授权
- 高风险不可逆操作

时打断。

# 12. Subagent

Subagent 只做：

> Provider / Model / Network Diagnostic Worker

不是 Multi-Agent Platform。

例如：

```text
loomx.test_provider
       ↓
Diagnostic Subagent
       ├── DNS
       ├── TCP
       ├── TLS
       ├── HTTP
       ├── Auth
       ├── Models
       └── Chat Request
       ↓
Structured Diagnostic Result
       ↓
Main Assistant
```

结果示例：

```json
{
  "reachable": true,
  "authenticated": true,
  "models_found": 12,
  "chat_test": true,
  "proxy_required": false,
  "diagnosis": "provider_ok"
}
```

不要允许 Subagent 无限调用工具或构建复杂 Agent Tree。

# 13. Chat UI

参考 Aily Chat：

https://github.com/ailyProject/aily-blockly/tree/master/src/app/tools/aily-chat

值得借鉴的是交互模型：
- 单窗口 Chat
- Session
- History
- Streaming
- 外部文本注入
- Tool/状态消息可以进入消息流

但不要复制 Angular / NG-Zorro。

LoomX 使用：

```text
Avalonia Native UI
```

建议：

```text
┌─────────────────────────────────────┐
│                         历史   ＋   │
│                                     │
│ 用户                                │
│ 帮我添加一个 OpenAI Provider        │
│                                     │
│ 助手                                │
│ ✓ 已读取 LoomX 配置                 │
│ ✓ 已创建备份                        │
│ ● 正在测试模型                      │
│                                     │
│ ┌─────────────────────────────────┐ │
│ │ 输入消息...                     │ │
│ └───────────────────────────── ↑ │ │
└─────────────────────────────────────┘
```

不要左侧巨大 Agent Sidebar。

不要显示 Chain of Thought。

只显示安全的 Action Summary：

```text
✓ 读取配置
✓ 创建备份
● 测试 Provider
```

# 14. Markdown

不要自行实现 Markdown。

不要使用 WebView。

优先：

LiveMarkdown.Avalonia

https://github.com/DearVa/LiveMarkdown.Avalonia

备选：

Markdown.Avalonia

https://github.com/whistyun/Markdown.Avalonia

# 15. Agent Events

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

UI 不直接绑定 Harness 内部对象，而是：

```text
AgentEvent
 ↓
Activity Projection
 ↓
UI
```

# 16. Tool Registry

Tool 统一描述：

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

例如：

```text
loomx.list_providers   Read
loomx.create_provider  Write
browser.click          External
delete_provider        Destructive
secret.store           Secret
```

默认自动执行 Read / Write / External。

Destructive 根据 Tool Policy / Skill 决定是否确认。

# 17. Session Persistence

至少保存：

```text
Session
Messages
Tool Activity
Skill references
Task status
```

永远不保存：

```text
Secret Value
```

# 18. 配置修改原则

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

支持安全原子写时：

```text
temp
 ↓
validate
 ↓
atomic replace
```

# 19. 开发顺序

## Step 1

```text
C# Harness
↓
Mock Tool
↓
Streaming Chat
↓
Tool Calling
```

## Step 2

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

## Step 3

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

# 20. MVP 验收

### Case A：查询

“看看我现在有几个 Provider。”

```text
Agent
 → loomx.list_providers
 → 中文回答
```

### Case B：新增 Provider

“把这个 OpenAI Compatible 服务加进 LoomX。”

```text
Skill
 → read
 → backup
 → create
 → test
```

### Case C：中转站

“帮我配置这个中转站。”

```text
Browser
 → login if needed
 → discover
 → secret store
 → Provider
 → Models
 → Combo
 → Test
```

### Case D：Codex

“让 Codex 使用 LoomX。”

```text
load codex skill
 → backup
 → patch config
 → validate
 → test
```

### Case E：诊断

“为什么这个模型请求失败？”

```text
loomx.test_model
 → diagnostic subagent
 → DNS/TLS/Auth/Model
 → structured result
 → 中文解释
```

# 21. 最终形态

用户面对：

```text
LoomX
│
├── NodeGraph
├── Endpoints
├── Providers
├── Models
├── Combos
├── Activity
│
└── 小助手
```

用户说：

```text
帮我接入这个中转站。
这个 Provider 为什么不能用？
给我测试一下所有模型。
让 Codex 走 LoomX。
这个网站的 API 地址是多少？
帮我把这个 Key 配进去。
```

后台：

```text
理解
→ Skill
→ Tool
→ Browser
→ 配置
→ 测试
→ 修复
→ 验证
```

用户最终只需要看到：

```text
✓ 完成
```

## 一句话原则

> Router 是产品；Harness 是智能控制层；Skill 是知识；Tool 是能力；Chrome Extension 是浏览器桥梁；Subagent 是诊断工人。

五者严格分层，不互相越界。
