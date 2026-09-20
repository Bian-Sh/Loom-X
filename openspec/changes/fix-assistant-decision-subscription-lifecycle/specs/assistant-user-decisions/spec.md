## MODIFIED Requirements

### Requirement: AskUser 必须控制询问时机和信息边界
系统 SHALL 允许调用方说明询问原因和影响摘要，且 SHALL 不要求用户在每个普通读写步骤中确认；桌面端 SHALL 仅在活动 Assistant 请求期间订阅用户决策 Broker，页面挂载、导航、模型选择或配置浏览不得单独激活订阅；AskUser 展示内容不得包含 API Key、Authorization、完整请求正文或其他敏感数据。

#### Scenario: 高影响配置决策
- **WHEN** Catalog Profile 缺少关键字段或配置变化需要用户决定是否稍后重启 Codex
- **THEN** 助手可以发起 AskUser，并展示安全摘要和可选行动

#### Scenario: 普通内部步骤
- **WHEN** 助手只是读取状态、验证 TOML 或执行已确认的非破坏性 Patch
- **THEN** 系统不强制额外弹出 AskUser

#### Scenario: 页面导航不激活决策订阅
- **WHEN** 用户进入或离开 Assistant、Provider、控制台页面，且没有正在发送的 Assistant 请求
- **THEN** 系统不订阅用户决策 Broker，也不记录“助手决策订阅已激活”

#### Scenario: 用户请求激活决策订阅
- **WHEN** 用户发送 Assistant 请求且服务初始化成功
- **THEN** 系统在 AgentLoop 和工具执行前订阅用户决策 Broker，使随后加载的 Skill 可以按需调用 `assistant.ask_user`

#### Scenario: 用户请求结束解除决策订阅
- **WHEN** Assistant 请求完成、失败、取消或因页面离开而停止处理用户决策
- **THEN** 系统解除 Broker 订阅，并对已领取的决策请求执行幂等取消，不能遗留等待项
