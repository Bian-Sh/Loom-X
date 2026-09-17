## Purpose

为 AI 助手提供统一的用户决策交互，使 Profile 构建、重启提醒和资料歧义处理可以一次性收集单选、多选、数值与文本输入，而不是依赖自然语言猜测。

## ADDED Requirements

### Requirement: Assistant 必须支持结构化用户决策请求
系统 SHALL 支持由助手发起包含标题、问题、字段、选项、默认值、必填标记和可取消状态的结构化 AskUser 请求，并 SHALL 支持单选、多选、数字输入和自由文本字段。

#### Scenario: 发起组合决策
- **WHEN** 助手需要同时确认上下文窗口、reasoning levels 和补充说明
- **THEN** 系统可以在一个 AskUser 请求中展示对应的单选、多选和文本输入字段

#### Scenario: 用户取消决策
- **WHEN** 用户关闭或取消 AskUser Dialog
- **THEN** 助手收到结构化取消结果，不得把取消解释为用户同意任何默认值

### Requirement: AskUser 必须暂停并恢复当前助手执行
系统 SHALL 在等待用户输入期间暂停当前工具调用或 Agent 步骤，保留请求标识和会话上下文，并在用户提交后恢复原流程。

#### Scenario: 用户提交选择
- **WHEN** 用户完成所有必填字段并提交
- **THEN** 系统按字段 id 返回结构化结果，助手可以继续执行原操作

#### Scenario: Assistant 运行中关闭页面
- **WHEN** AskUser Dialog 所属页面被关闭或会话被取消
- **THEN** 等待中的请求收到取消/失败结果，不能永久阻塞 Agent Loop

### Requirement: AskUser 必须控制询问时机和信息边界
系统 SHALL 允许调用方说明询问原因和影响摘要，且 SHALL 不要求用户在每个普通读写步骤中确认；AskUser 展示内容不得包含 API Key、Authorization、完整请求正文或其他敏感数据。

#### Scenario: 高影响配置决策
- **WHEN** Catalog Profile 缺少关键字段或配置变化需要用户决定是否稍后重启 Codex
- **THEN** 助手可以发起 AskUser，并展示安全摘要和可选行动

#### Scenario: 普通内部步骤
- **WHEN** 助手只是读取状态、验证 TOML 或执行已确认的非破坏性 Patch
- **THEN** 系统不强制额外弹出 AskUser

### Requirement: 资料收集必须优先复用已有能力且不绕过网站安全机制
系统 SHALL 优先使用当前 Assistant 模型已经提供的搜索或资料能力；不可用时 SHALL 允许通过现有 Chrome Extension/Browser Bridge 读取用户明确打开并授权的页面；系统 MUST NOT 实现第三方搜索 API Key、验证码绕过、登录墙绕过、Cloudflare/JS challenge 绕过、Cookie 注入、TLS fingerprint 或浏览器指纹伪装。

#### Scenario: 模型具备搜索能力
- **WHEN** 当前 Assistant 模型提供可用的官方搜索能力
- **THEN** 助手优先使用该能力获取资料，不要求用户配置新的搜索 API Key

#### Scenario: 模型没有搜索能力但浏览器已连接
- **WHEN** Assistant 没有搜索能力且 Chrome Extension 已连接
- **THEN** 助手可以打开官方文档或 Provider 页面，并读取用户授权的页面内容

#### Scenario: 页面需要用户处理障碍
- **WHEN** 页面出现登录、验证码、Cloudflare 或 JS challenge
- **THEN** 助手暂停并提示用户自行处理，处理完成后再继续读取，不尝试绕过页面安全机制

#### Scenario: 没有任何资料通道
- **WHEN** Assistant 没有搜索能力、Browser Bridge 未连接且用户未提供资料
- **THEN** 助手明确说明无法验证资料，并通过 AskUser 请求用户提供结论或文档内容
