# Provider 面板

## Purpose

定义 Provider 页面摘要、目录、详情区域、操作入口和透明层级的布局与交互约束，确保 Provider 管理界面在不同窗口尺寸下保持可读、可操作和视觉一致。

## Requirements

### Requirement: Provider 面板按固定摘要与自适应工作区布局
Provider 页面 MUST 将摘要区域固定在顶部，将目录与详情放入填满剩余高度的两列工作区；目录列表和详情 Tab 内容 MAY 独立滚动，但摘要、目录标题、搜索框和详情标题 MUST 保持可见。

#### Scenario: 窗口缩放保持底部对齐
- **WHEN** 用户调整窗口大小
- **THEN** Provider 目录和详情面板的下边沿保持对齐，并与主内容区底部保留 18px gap

### Requirement: 空 Provider 显示详情空状态
当 Provider 集合为空时，右侧详情区域 MUST 显示简短空状态提示，而不是显示空白编辑表单。

#### Scenario: 无 Provider
- **WHEN** 页面没有 Provider
- **THEN** 右侧显示"暂无 Provider"及引导添加的提示

### Requirement: Provider 卡片提供启用与删除操作
Provider 卡片 MUST 保留启用开关，开关 MUST 不显示 `On/Off` 文案且 MUST 提供悬停提示；卡片右下角 MUST 提供删除图标，删除 MUST 在实际执行前要求二次确认。

#### Scenario: 取消删除
- **WHEN** 用户点击卡片删除图标并在确认窗口选择取消
- **THEN** Provider 集合和持久化配置保持不变

#### Scenario: 确认删除
- **WHEN** 用户点击卡片删除图标并确认
- **THEN** 对应 Provider 被删除，选中项、摘要统计和目录同步更新

### Requirement: Provider 卡片使用统一透明层级
Provider 左侧列表容器 MUST 保持透明，Provider 卡片 MUST 直接使用 `SurfaceSubtleBrush`，不得通过列表项或列表容器重复叠加 `SurfaceBrush`。

#### Scenario: Provider 卡片与 Gateway 卡片透明度一致
- **WHEN** 用户打开 Provider 页面
- **THEN** 左侧 Provider 卡片与 Gateway Endpoint 卡片使用相同的透明背景层级，桌面底色不会因列表容器额外叠加而变得浑浊

### Requirement: 基础 Tab 不重复提供 Provider 启用入口
基础 Tab MUST 不显示 Provider 启用复选框，Provider 启用状态只通过目录卡片开关操作。

#### Scenario: 基础 Tab 隐藏启用复选框
- **WHEN** 用户打开 Provider 的基础 Tab
- **THEN** 页面不显示 Provider 启用复选框，启用状态仍可通过目录卡片开关修改

### Requirement: Provider 详情 Tab 保持内容边界并独立定位主滚动条
Provider 右侧详情面板的基础、高级、模型和测试 Tab MUST 保持表单 ScrollContent 的既有右侧边界，同时让主纵向滚动条在 ScrollViewer 原生模板层级对齐用户指定的右侧参考线。实现不得通过移动整个 ScrollViewer 或把 ScrollBar 渲染到父级布局边界外完成。

#### Scenario: 内容与滚动条使用独立横向位置
- **WHEN** 当前 Tab 内容需要纵向滚动
- **THEN** ScrollContent 右边缘保持原位，主纵向滚动条位于右侧参考线

#### Scenario: 切换详情 Tab
- **WHEN** 用户在基础、高级、模型和测试 Tab 之间切换
- **THEN** 每个 Tab 均使用相同的内容 inset 和原生 ScrollBar 位置

#### Scenario: API Key 输入框保持内容边界
- **WHEN** 基础 Tab 显示 API Key 输入框和可见性按钮
- **THEN** API Key 输入框右边缘与显示名称、Base URL 输入框一致，不延伸到主滚动条预留区域

#### Scenario: 嵌套内容保留自身滚动行为
- **WHEN** 测试响应文本框的内容需要独立滚动
- **THEN** 该嵌套滚动区域保持自身布局，不应用详情 Tab 主滚动区域的内容 Padding
