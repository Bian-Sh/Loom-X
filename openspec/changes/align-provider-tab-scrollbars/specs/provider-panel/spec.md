## ADDED Requirements

### Requirement: Provider 详情 Tab 滚动条保持统一右侧对齐
Provider 右侧详情面板的基础、高级、模型和测试 Tab 的主纵向滚动条 MUST 统一对齐用户指定的右侧参考线；页面不得在 `TabControl` 模板内缩之外继续增加右侧补偿。各 Tab 的内容高度变化或切换不得改变该横向位置。

#### Scenario: 切换详情 Tab
- **WHEN** 用户在基础、高级、模型和测试 Tab 之间切换，且当前 Tab 内容需要纵向滚动
- **THEN** 当前 Tab 的主纵向滚动条使用统一位置并对齐右侧参考线

#### Scenario: 页面不重复增加右侧补偿
- **WHEN** `TabControl` 模板已经提供内容内缩
- **THEN** Tab 外层滚动区域不再增加额外右侧 Margin

#### Scenario: 嵌套内容保留自身滚动行为
- **WHEN** 测试响应文本框的内容需要独立滚动
- **THEN** 该嵌套滚动区域保持自身布局，不替代或改变 Tab 主纵向滚动条的统一位置
