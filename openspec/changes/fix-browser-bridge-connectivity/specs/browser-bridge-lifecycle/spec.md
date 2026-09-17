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

#### Scenario: AI 主动释放非最后一个 Session 租约
- **WHEN** Session A 与 Session B 都持有租约且 Session A 关闭 Bridge
- **THEN** 系统只移除 Session A，Bridge 继续监听

#### Scenario: AI 主动释放最后一个 Session 租约
- **WHEN** 仅 Session B 持有租约且 Session B 关闭 Bridge
- **THEN** 系统移除 Session B 并停止 Bridge

#### Scenario: 切换或离开 Session
- **WHEN** 用户新建、切换、离开或关闭当前 Session UI
- **THEN** 系统不自动移除任何租约，Bridge 状态保持不变

#### Scenario: 删除持有租约的 Session
- **WHEN** 用户意外删除一个仍残留租约的 Assistant Session
- **THEN** 系统把删除动作作为被动兜底，尝试释放该 Session ID；若仍有其他租约则保持 Bridge，否则停止 Bridge

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
- **THEN** 模型只收到占位符与 `secret_ref`，明文不进入工具结果、对话、Session 文件或日志

#### Scenario: 网络记录包含 Authorization
- **WHEN** browser.network 返回 Authorization 或 API Key 头
- **THEN** 头值在进入模型前转换为 `secret_ref`

### Requirement: Browser Skill 管理自身租约

系统 MUST 向模型提供当前 Assistant Session ID，并在内置 Browser Skill 中要求模型在使用 browser 操作前申请该 ID 的租约、在确认本 Session 不再需要浏览器时释放同一 ID。

#### Scenario: AI 开始浏览器自动化
- **WHEN** AI 判断当前任务需要 Browser Bridge
- **THEN** AI 先调用启用工具并传入当前 Assistant Session ID，再调用 browser.open 等工具

#### Scenario: AI 完成浏览器自动化
- **WHEN** 当前 Session 已完成所有 browser 操作且无需保持连接
- **THEN** AI 关闭自己的自动化 tab，并调用关闭工具释放当前 Assistant Session ID
