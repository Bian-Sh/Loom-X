# Comet Design Handoff

- Change: fix-browser-bridge-connectivity
- Phase: design
- Mode: compact
- Context hash: ba12acdf96669dbb49794de83d2208ac8733180b52b6786ec180fd9d216b9e14

Generated-by: comet-handoff.sh

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/fix-browser-bridge-connectivity/proposal.md

- Source: openspec/changes/fix-browser-bridge-connectivity/proposal.md
- Lines: 1-28
- SHA256: b9abface45235ab1523520f94a4d7f77177cf0905acf7c807da69600f2ce2c96

```md
## Why

LoomX 的 Chrome Extension 当前无法稳定与 Browser Bridge 联通：MV3 Service Worker 在空闲后会终止，Bridge 又被错误地绑定到 Gateway 启停，导致 AI 助手在网关关闭、切换会话或重连场景下失去浏览器能力。现有 Bridge 还是一次性生命周期，停止后不能安全重启；Extension 断线时会销毁自动化目标；页面正文中的 API Key 也没有在 .NET 安全边界内转换为 `secret_ref`。此外，Serilog 文件 sink 的不兼容参数组合会阻断最新桌面包启动。

## What Changes

- 新增基于 Assistant Session ID 的 Browser Bridge 租约：Session 显式启用时登记 ID，显式关闭或删除 Session 时移除 ID；仅当租约集合为空时停止单例 Bridge。
- 将 Bridge 改为可重复启动、停止和重连的独立生命周期，不再由 Gateway 启停驱动。
- 为 Assistant 暴露当前 Session ID，并新增显式 Bridge 启停工具；Skill 决定何时申请和释放租约。
- Extension 使用 WebSocket 心跳与 `chrome.alarms` 双重恢复；断线不销毁自动化标签页，重新握手时同步目标快照。
- 自动化标签页后台创建，通过目标 tab 的 CDP 截图。
- 将正文 Secret 扫描移至 .NET `BrowserSecretHarvester`，Extension 只返回页面原始读取结果；模型仅看到占位符与 `secret_ref`。
- 在 `loomx.create_provider` schema 中公开 `api_key_secret_ref`。
- 修正 Serilog 文件 sink 的不兼容参数组合。

## Capabilities

### New Capabilities

- `browser-bridge-lifecycle`：定义 Session 租约、Bridge 可重启生命周期、Extension 恢复和 Secret 边界。

### Modified Capabilities

无。

## Impact

涉及 Browser Bridge、Assistant Session 生命周期、browser.* 工具、内置 Skill、Chrome Extension、Provider 工具 schema、日志引导配置及对应测试。不改变公开 HTTP API、数据库 schema 或运行时数据库路径。

```

## openspec/changes/fix-browser-bridge-connectivity/design.md

- Source: openspec/changes/fix-browser-bridge-connectivity/design.md
- Lines: 1-72
- SHA256: a575966ac8e2f4e02e8e63bcc11f0d2f00c09d7ef67985121e0e47304e75b725

```md
## 根因

1. Browser Bridge 注册为 Gateway Host 的 hosted service，桌面端只初始化容器但不一定启动 Host；Gateway 停止又会释放容器，浏览器能力因此错误地依赖 Gateway 生命周期。
2. `BrowserBridge` 持有一次性的 `HttpListener`/`CancellationTokenSource`，只有 `Start()` 和最终 `Dispose()`，无法表达 Session 按需启停及停止后重启。
3. Chrome MV3 Extension 仅用 `setTimeout` 重连，WebSocket 无周期消息；Service Worker 休眠后不能可靠恢复。断线时还执行 `detachAllTargets()`，把传输故障扩大为自动化会话销毁。
4. 页面 Secret 识别临时落在 `background.js`，违反 Extension 只负责传输和 Chrome API 的边界；服务端收割器又只检查敏感字段名，不能处理正文。
5. `LoggingBootstrap` 同时配置 `shared: true` 与 `buffered: true`，Serilog 启动时拒绝该组合。

## 架构决策

### Session 租约

新增进程级单例 `BrowserBridgeLeaseManager`，内部维护去重的 Assistant Session ID 集合：

- `browser.bridge_start` 接收 `assistant_session_id`，加入集合；第一个租约启动 Bridge。
- `browser.bridge_stop` 接收 `assistant_session_id`，移除集合；移除后仍有租约则保持监听，为空才停止 Bridge。
- 重复 start/stop 幂等。
- 新建或切换 Session 不自动释放旧租约。
- 删除 Assistant Session 必须尝试释放该 ID；释放失败记录安全日志，但会话文件删除语义保持明确。
- 应用退出最终释放 Bridge，不依赖 Session 主动清理。

Assistant 系统提示词包含当前 Session ID，内置 Browser Skill 负责判断何时启停并把该 ID 传给工具。`session_id` 继续专指 Chrome target session，工具参数使用 `assistant_session_id` 避免歧义。

### Bridge 生命周期

`BrowserBridge` 支持 `Stopped → Listening → Stopped` 的重复循环：

- `StartAsync()`、`StopAsync()` 幂等并串行化。
- 停止时取消当前生命周期 token、关闭 WebSocket、停止 listener、等待监听/接收任务退出并清理服务端目标快照。
- 停止后可重新启动同一端口。
- `DisposeAsync()` 只负责最终停止和释放底层资源。
- 状态区分“正在监听”与“Extension 已连接”。

Gateway 初始化、启动和停止不得直接调用 Bridge 启停。

### Extension 恢复与目标重同步

- 已连接时每 20 秒发送 `Bridge.keepAlive`。
- 增加 `alarms` 权限与最低 Chrome 120；创建 30 秒周期 alarm，在脚本加载、安装、浏览器启动和 alarm 回调时调用幂等 `connect()`。
- WebSocket 断开只停止心跳并计划重连，不 detach 或清空自动化目标。
- `Bridge.hello` 附带当前目标快照；服务端握手时重建 `Targets`。
- 用户主动关闭 tab 或 `browser.close` 才移除目标。

### Secret 边界

- Extension 返回原始 `content/html/markdown`，不识别、不记录 Key。
- `.NET` `BrowserSecretHarvester` 同时处理敏感结构化字段和正文中的严格 Key 形态。
- 正文命中替换为安全占位符，明文立即进入 `BrowserSecretVault`，结果追加只含 `secret_ref`/类型提示的 `harvested_secrets`。
- 对话、Session JSONL、日志、Toast 和最终汇报不得出现明文 Key。

### 其他修复

- 自动化 tab 使用 `active: false`。
- 截图使用 `chrome.debugger` 的 `Page.captureScreenshot`。
- `loomx.create_provider` schema 公开 `api_key_secret_ref`。
- 日志 sink 保留 `buffered: true`，移除 `shared`。

## 错误处理

- 端口占用时 `browser.bridge_start` 返回安全错误码，不使桌面应用崩溃，也不把失败 Session 留在租约集合。
- Extension 未连接时 browser 操作返回 `browser_bridge_offline`；Skill 可等待 alarm 重连或提示用户检查扩展。
- 删除 Session 释放租约失败时记录 `Warning`，仍继续删除会话文件；应用退出执行最终清理。
- 所有日志只记录 Session ID、端口、状态和错误码，不记录页面正文、Key、请求头值或工具参数。

## 验证

- 单元测试覆盖租约去重、共享、最后释放停止、启动失败回滚和删除 Session 释放。
- 回环测试覆盖 Bridge 启停幂等、停止后重启、目标快照恢复和断线清理。
- 源码契约测试覆盖 alarm、心跳、不 detach、后台 tab、CDP 截图、Skill 启停规则和服务端正文收割。
- 运行 Assistant/Browser 定向测试、完整 `dotnet test` 与 Release build。
- 发布新的独立桌面包，重新加载 Extension，验证多 Session 共享、持续连接和所有启动顺序。
- 让 LoomX 小助手访问授权页面，以 `business_id=loomx`、显示名 `Loomx` 完成 Provider 录入和测试，并扫描数据库、日志和 Session 文件确认无明文 Key。

```

## openspec/changes/fix-browser-bridge-connectivity/tasks.md

- Source: openspec/changes/fix-browser-bridge-connectivity/tasks.md
- Lines: 1-22
- SHA256: bfe0a3da2d077a192ac2de715af8957dce5bffe7438746f24ded934f53118a0f

```md
## 1. 规格与回归测试

- [ ] 1.1 固化 Session 租约、Bridge 可重启、Extension 恢复、目标重同步和 Secret 边界规格。
- [ ] 1.2 添加租约管理、Bridge 生命周期、Session 删除、正文 Secret 收割和 Extension/Skill 契约的失败测试。

## 2. Bridge 与 Session 生命周期

- [ ] 2.1 实现可重复启动/停止的 BrowserBridge 与 Session ID 租约管理器。
- [ ] 2.2 新增显式 Bridge 启停工具，向模型公开当前 Assistant Session ID，并在删除 Session 时尝试释放租约。
- [ ] 2.3 移除 Gateway 对 Bridge 生命周期的控制，保留应用退出最终释放。

## 3. Extension 与安全边界

- [ ] 3.1 实现 alarm + WebSocket 心跳、断线保留目标和 hello 目标快照重同步。
- [ ] 3.2 将正文 Secret 收割集中到 .NET，完善 Provider `api_key_secret_ref` schema、后台 tab、CDP 截图和日志启动修复。
- [ ] 3.3 更新 Browser Skill，规定 AI 按需申请/释放自己的 Session 租约。

## 4. 验证与交付

- [ ] 4.1 运行定向测试、完整测试和 Release build，检查日志与持久化无明文 Secret。
- [ ] 4.2 发布时间命名的独立桌面包，重新加载 Extension，验证多 Session 与重连矩阵。
- [ ] 4.3 让 LoomX 小助手使用 Browser Bridge 完成 `Loomx` Provider 录入并验证。

```

## openspec/changes/fix-browser-bridge-connectivity/specs/browser-bridge-lifecycle/spec.md

- Source: openspec/changes/fix-browser-bridge-connectivity/specs/browser-bridge-lifecycle/spec.md
- Lines: 1-97
- SHA256: 667681ca92b256b5e18203f931e1344d9afa6cdcf154c9697806cdc75e6762d4

[TRUNCATED]

```md
## Purpose

定义 LoomX AI 助手按 Session 共享本地 Browser Bridge、Chrome Extension 自动恢复以及浏览器 Secret 不进入模型的运行契约。

## ADDED Requirements

### Requirement: Assistant Session 通过租约共享单例 Browser Bridge

系统 MUST 允许 Assistant Session 使用自身 Session ID 显式申请和释放进程内单例 Browser Bridge。相同 ID 重复申请或释放 MUST 幂等；仅当最后一个 ID 被移除时才停止 Bridge。

#### Scenario: 第一个 Session 启用 Bridge
- **WHEN** 当前没有租约且 Session A 使用自己的 Assistant Session ID 启用 Bridge
- **THEN** 系统登记 Session A 并开始监听本地 Bridge 端口

#### Scenario: 多个 Session 共享 Bridge
- **WHEN** Session A 已持有租约且 Session B 启用 Bridge
- **THEN** 系统登记 Session B，并保持同一个 Bridge 实例和监听端口

#### Scenario: 非最后一个 Session 关闭 Bridge
- **WHEN** Session A 与 Session B 都持有租约且 Session A 关闭 Bridge
- **THEN** 系统只移除 Session A，Bridge 继续监听

#### Scenario: 最后一个 Session 关闭 Bridge
- **WHEN** 仅 Session B 持有租约且 Session B 关闭 Bridge
- **THEN** 系统移除 Session B 并停止 Bridge

#### Scenario: 删除持有租约的 Session
- **WHEN** 用户删除一个 Assistant Session
- **THEN** 系统尝试释放该 Session ID；若仍有其他租约则保持 Bridge，否则停止 Bridge

### Requirement: Browser Bridge 可重复启动和停止

系统 MUST 让同一 Bridge 实例支持幂等启动、幂等停止和停止后重新启动，并将监听状态与 Extension 连接状态分别公开。

#### Scenario: 重复启动或停止
- **WHEN** Bridge 已处于目标状态且再次收到相同生命周期操作
- **THEN** 系统保持当前状态且不重复创建监听任务或抛出异常

#### Scenario: 停止后重启
- **WHEN** Bridge 完成停止后新的 Session 申请租约
- **THEN** 系统在原端口重新监听并允许 Extension 再次握手

#### Scenario: 端口被占用
- **WHEN** 第一个租约启动 Bridge 时端口不可用
- **THEN** 工具返回安全错误且该 Session ID 不留在租约集合中，桌面应用继续运行

### Requirement: Bridge 生命周期独立于 Gateway

系统 MUST 让 Gateway 启动、停止和容器初始化不直接启动或停止 Browser Bridge。

#### Scenario: Gateway 关闭时使用浏览器
- **WHEN** Gateway 处于停止状态且 Assistant Session 申请 Bridge 租约
- **THEN** Bridge 仍可监听并接受 Extension 连接

#### Scenario: Gateway 切换状态
- **WHEN** Browser Bridge 正在被 Session 使用且 Gateway 启动或停止
- **THEN** Bridge 监听与 Extension 连接不因 Gateway 状态变化而中断

### Requirement: Extension 自动恢复并保留自动化目标

Extension MUST 使用已连接 WebSocket 的周期消息维持 Service Worker，并使用周期 alarm 在未连接时唤醒重连。传输断开 MUST NOT 主动关闭、detach 或遗忘自动化目标；重新握手 MUST 上报当前目标快照。

#### Scenario: LoomX 晚于 Chrome 启动
- **WHEN** Extension 已加载但 Bridge 尚未监听
- **THEN** alarm 在后续周期唤醒 Extension 并重新尝试连接

#### Scenario: WebSocket 临时断开
- **WHEN** 已有自动化目标且 WebSocket 断开后恢复
- **THEN** 自动化 tab 保持打开，Extension 在 hello 中上报目标快照，服务端恢复目标注册

#### Scenario: 用户主动关闭自动化 tab
- **WHEN** 用户关闭 tab 或 AI 调用 browser.close
- **THEN** Extension 移除对应目标并向已连接 Bridge 发送关闭事件

### Requirement: 浏览器 Secret 在服务端安全边界收割

系统 MUST 在 browser.* 结果进入模型前，于 .NET 侧扫描敏感结构化字段和页面正文中的严格 Secret 形态，将明文替换为安全占位符并保存到 Browser Secret Vault。Extension MUST NOT 承担 Secret 识别业务。

#### Scenario: 页面正文包含 API Key
- **WHEN** browser.read 返回包含严格 API Key 形态的正文

```

Full source: openspec/changes/fix-browser-bridge-connectivity/specs/browser-bridge-lifecycle/spec.md
