# Provider 面板

## MODIFIED Requirements

### Requirement: Provider 面板按固定摘要与自适应工作区布局
Provider 页面 MUST 将摘要区域固定在顶部，将目录与详情放入填满剩余高度的两列工作区；目录列表和详情 Tab 内容 MAY 独立滚动，但摘要、目录标题、搜索框和详情标题 MUST 保持可见。

#### Scenario: 窗口缩放保持底部对齐
- **WHEN** 用户调整窗口大小
- **THEN** Provider 目录和详情面板的下边沿保持对齐，并与主内容区底部保留 18px gap

### Requirement: 空 Provider 显示详情空状态
当 Provider 集合为空时，右侧详情区域 MUST 显示简短空状态提示，而不是显示空白编辑表单。

#### Scenario: 无 Provider
- **WHEN** 页面没有 Provider
- **THEN** 右侧显示“暂无 Provider”及引导添加的提示

### Requirement: Provider 卡片提供启用与删除操作
Provider 卡片 MUST 保留启用开关，开关 MUST 不显示 `On/Off` 文案且 MUST 提供悬停提示；卡片右下角 MUST 提供删除图标，删除 MUST 在实际执行前要求二次确认。

#### Scenario: 取消删除
- **WHEN** 用户点击卡片删除图标并在确认窗口选择取消
- **THEN** Provider 集合和持久化配置保持不变

#### Scenario: 确认删除
- **WHEN** 用户点击卡片删除图标并确认
- **THEN** 对应 Provider 被删除，选中项、摘要统计和目录同步更新

### Requirement: 基础 Tab 不重复提供 Provider 启用入口
基础 Tab MUST 不显示 Provider 启用复选框，Provider 启用状态只通过目录卡片开关操作。
