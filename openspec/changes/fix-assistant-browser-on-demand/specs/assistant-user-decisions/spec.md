## ADDED Requirements

### Requirement: 明确区分凭据名称与凭据值

AskUser SHALL 支持字段 purpose=credential_name，限单行 text 或 single_select，最长200字。单个名称字段的名称、选项和说明 SHALL 允许 API Key、Token 等术语；MUST 继续拒绝可识别凭据值、认证赋值、凭据引用及非法标识。默认 general 和 TOML 读取隐私规则 MUST 保持严格限制。判断命名用途由模型和 Skill 通过结构化参数表达，MUST NOT 从用户语言硬编码判定。

#### Scenario: 收集新名称
- **WHEN** Skill 请求 credential_name 文本字段且默认值为 LoomX
- **THEN** 名称请求通过 Broker 并显示默认值，用户可直接提交或改名，真实凭据值被拒绝

#### Scenario: 选择已有名称并输入自定义名称
- **WHEN** 单选字段声明 credential_name 并允许自由输入
- **THEN** 英文 Key/Token 名称可作为选项展示，选择与自定义名称分别保留在 values 和 custom_inputs 中