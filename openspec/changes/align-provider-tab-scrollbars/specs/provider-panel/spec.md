## ADDED Requirements

### Requirement: Provider 详情 Tab 保持内容边界并独立定位主滚动条
Provider 右侧详情面板的基础、高级、模型和测试 Tab MUST 保持表单 ScrollContent 的既有右侧边界，同时让主纵向滚动条在 ScrollViewer 原生模板层级对齐用户指定的右侧参考线。实现不得通过移动整个 ScrollViewer 或把 ScrollBar 渲染到父级布局边界外完成。

#### Scenario: 内容与滚动条使用独立横向位置
- **WHEN** 当前 Tab 内容需要纵向滚动
- **THEN** ScrollContent 右边缘保持原位，主纵向滚动条位于右侧参考线

#### Scenario: 切换详情 Tab
- **WHEN** 用户在基础、高级、模型和测试 Tab 之间切换
- **THEN** 每个 Tab 均使用相同的内容 inset 和原生 ScrollBar 位置

#### Scenario: 嵌套内容保留自身滚动行为
- **WHEN** 测试响应文本框的内容需要独立滚动
- **THEN** 该嵌套滚动区域保持自身布局，不应用详情 Tab 主滚动区域的内容 Padding
