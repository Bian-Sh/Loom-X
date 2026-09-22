## ADDED Requirements

### Requirement: Endpoint 面板使用非选择型展示容器

网关页面左侧 Endpoint 面板 SHALL 使用不产生整行选中、悬浮或点击语义的展示容器；Endpoint 内部明确的按钮、开关、组合选择器和下拉框 SHALL 继续保持各自交互。

#### Scenario: 浏览 Endpoint 列表

- **WHEN** 用户将鼠标移入、移出或点击 Endpoint 子项的空白区域
- **THEN** 子项不显示 ListBox 选中或悬浮反馈，也不因空白区域点击改变页面状态

### Requirement: 网关面板布局与操作图标保持一致

网关页面主工作区 SHALL 让左侧 Endpoint 面板宽于右侧 Combo 面板；右侧删除操作 SHALL 使用 Provider 供应商 cell 的垃圾桶图标；需要 API Key 的 Endpoint SHALL 始终显示使用主题次要文字色的刷新按钮。

#### Scenario: 查看网关面板操作

- **WHEN** 用户打开网关页面
- **THEN** 左右面板按约 60/40 分配空间，右侧删除按钮显示 Provider cell 同款垃圾桶图标，API Key 刷新按钮常驻且不呈纯黑
