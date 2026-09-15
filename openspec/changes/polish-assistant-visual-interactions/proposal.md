## Why

AI 助手当前仍存在历史消息图标失色、滚动条视觉与边缘布局不一致、异常气泡材质不透明，以及标题区新会话图标间距不足等问题，影响透明主题下的视觉一致性和可操作性。

## What Changes

- 保证 Markdown 中的彩色 Unicode 图标在流式首显和历史会话恢复后保持一致颜色。
- 将消息滚动条调整为 Codex 风格：顶部/底部三角按钮、居中的胶囊滑块、划入加深，并紧贴应用右侧边缘。
- 异常气泡复用普通消息气泡的透明材质逻辑，仅使用红色语义色。
- 保持历史时钟图标位置不变，将新会话图标额外右移 10px。

## Capabilities

### New Capabilities
- `assistant-visual-interactions`: 约束助手消息渲染、滚动条、异常气泡和标题图标的视觉交互一致性。

### Modified Capabilities
- 无。

## Impact

主要影响 `AssistantView` 的 XAML、代码后置、主题资源与对应 Avalonia 控件测试；历史消息恢复可能需要对 Markdown 文本规范化。不会改变数据库结构、外部 API 或助手请求协议。
