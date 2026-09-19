## ADDED Requirements

### Requirement: 不存在的模型组合按使用场景差异化展示

网关页面 SHALL 在右侧模型组合编辑面板隐藏已不存在的 Combo，同时在左侧 Endpoint 的 flags 组合下拉框中保留已绑定但不存在的 Combo，以便用户识别并调整历史绑定。

#### Scenario: 查看包含不存在组合的网关页面

- **WHEN** 配置中保留一个已软删除且仍被 Endpoint 绑定的 Combo
- **THEN** 右侧模型组合编辑面板不显示该 Combo
- **AND** 左侧对应 Endpoint 的 flags 组合下拉框继续显示该 Combo，并标记为“不存在”