## Context

见 `proposal.md`。当前标题区的历史与新会话按钮仅有 2px 间距，新会话按钮中心比消息滚动条中心左偏 3px。Avalonia Fluent 纵向滚动条 Thumb 默认以右边缘作为缩放原点，因此悬停展开会侵入左侧消息区域。输入框仍是固定单行控件，模型弹层固定为 360×420px。

## Goals / Non-Goals

**Goals:**

- 使用局部样式和现有控件能力完成布局调整，不改变全局 ScrollBar 或 TextBox 外观。
- 输入框上限随窗口高度动态变化，并在窗口缩放时即时更新。
- 用自动化测试锁定关键尺寸、缩放方向和键盘行为。

**Non-Goals:**

- 不重做聊天页视觉主题、消息气泡布局或模型数据加载逻辑。
- 不修改其他页面的滚动条、输入框或模型选择弹层。

## Decisions

1. 给新会话按钮增加独立的 3px 横向渲染位移，不改变标题 StackPanel 的测量宽度和历史按钮位置；相较修改整个容器边距，这能精确增加两个按钮的视觉间距并对齐滚动条中心。
2. 给消息 `ScrollViewer` 增加专用 class，仅覆盖其纵向 ScrollBar 模板内 Thumb 的 `RenderTransformOrigin` 为左侧中心。保留 Fluent 原有宽度和动画，只改变展开方向。
3. 输入框使用 `AcceptsReturn=True`、自动换行和内部纵向滚动。`AssistantView` 在自身尺寸变化时读取顶层窗口客户区高度，将输入框 `MaxHeight` 设置为其 32%；该计算保持为独立纯函数以便测试。
4. 键盘处理仅拦截没有 Shift 修饰键的 Enter 并执行发送；Shift+Enter 交回 TextBox 默认多行行为。
5. 模型弹层固定宽度从 360px 收窄为 256px，最大高度从 420px 调整为 360px；面板间距收至 6px，搜索框、Provider 标题行与模型行分别压缩到约 32px、32px 和 30px，以实际增加每屏可见模型数量。
6. 模型搜索框背景在普通、悬停和聚焦状态均保持透明，由弹层自身背景统一承接透明或不透明主题；除设置 `TextBox.Background` 外，还通过局部 `/template/ Border#PART_BorderElement` 样式覆盖 Fluent 模板在聚焦态注入的白色背景，同时保留聚焦边框。模型名称列使用剩余宽度布局，越界时显示省略号，并在整行悬停时通过标准 ToolTip 展示完整名称。

## Risks / Trade-offs

- [Risk] 局部模板选择器在 Fluent 主题升级后失效。 → 使用真实控件测试读取纵向 ScrollBar Thumb 的缩放原点。
- [Risk] 输入框过高会压缩消息浏览区域。 → 上限固定为客户区高度的 32%，且随窗口缩放重新计算。
- [Risk] 256px 宽度下极长模型名更容易截断。 → 保留现有摘要省略和模型列表滚动，不改变选择能力。
- [Risk] 所有模型行都提供 ToolTip，而不是只在文本实际截断时提供。 → 保持实现简单且行为可预测，短名称的重复提示不影响选择流程。
- [Risk] 只测试 `TextBox.Background` 会漏掉 Fluent 模板内部的实际绘制背景。 → 聚焦真实控件并检查 `PART_BorderElement` 的最终画刷，避免属性测试与实机渲染不一致。
