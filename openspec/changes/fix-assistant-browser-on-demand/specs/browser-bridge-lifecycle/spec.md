## ADDED Requirements

### Requirement: AI 判定新增 Provider 后按需启动浏览器通信

AI 助手 MUST 根据用户消息与上下文判断，只有用户提供链接、希望新增 Provider，且确实需要读取页面时才调用 browser.bridge_start。系统 MUST 下发一致的系统提示、工具说明和接入 Skill。意图判断 MUST 由 AI 模型完成，LoomX MUST NOT 新增关键词意图匹配器。

#### Scenario: 新建恢复会话或普通对话
- **WHEN** 用户新建或恢复会话、普通对话、仅提供链接、查看或修改已有 Provider
- **THEN** AI 不为这些动作调用 browser.bridge_start，应用不直接启动 Bridge

#### Scenario: 用户希望从链接新增 Provider
- **WHEN** AI 判断用户提供链接并希望新增 Provider，且需要读取页面
- **THEN** AI 使用当前 Assistant Session ID 调用 browser.bridge_start 后再读取页面

#### Scenario: 意图不明确或资料完整
- **WHEN** 新增 Provider 意图不明确，或已有完整接入资料
- **THEN** AI 先澄清或直接使用已有资料，不为收集可选资料预先连接 Chrome

#### Scenario: 历史会话使用最新策略
- **WHEN** 载入历史会话
- **THEN** 系统刷新当前系统提示，让模型收到按需启动约束

### Requirement: 异常结束清理运行所属租约

运行失败或取消时宿主 MUST 清理该运行开始时所属Session的Bridge租约，MUST NOT 清理其他Session或误用当前查看Session的ID。正常完成 SHALL 继续由AI主动管理租约。

#### Scenario: 失败运行与其他Session共享Bridge
- **WHEN** Session A运行失败或取消且Session B仍持有租约
- **THEN** 只移除Session A租约，Bridge保持运行供Session B使用
