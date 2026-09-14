## Why

AI 助手聊天页的标题操作区、消息滚动条、输入框和模型选择弹层存在局部对齐与空间利用问题：新会话按钮未与消息滚动条中心对齐，滚动条悬停向消息侧扩张，输入框无法随多行内容增长，模型弹层也显得偏宽。

## What Changes

- 精确调整新会话按钮的视觉位置，使其中心与消息区纵向滚动条中心对齐。
- 让消息区滚动条 Thumb 在悬停时向右侧扩张，避免压住用户消息气泡。
- 将底部输入框改为自动增高的多行输入，最高为应用客户区高度的 32%，达到上限后显示内部滚动条；Enter 发送，Shift+Enter 换行。
- 将模型选择弹层收窄并提高列表信息密度；搜索框透出弹层材质，过长模型名省略显示并可通过悬停气泡查看完整名称。

## Capabilities

### New Capabilities

- `assistant-chat-ui`: 定义 AI 助手聊天页操作区、消息滚动条、输入框和模型选择弹层的布局与交互要求。

### Modified Capabilities

无。

## Impact

- 影响 `LoomX/Views/AssistantView.axaml`、`LoomX/Views/AssistantView.axaml.cs` 及对应 UI 契约测试。
- 不改变公开 API、数据库 schema、会话存储格式、模型协议或日志内容。
