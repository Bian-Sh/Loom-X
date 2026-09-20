## Why

当前 AssistantView 在挂载到可视树时就激活 `IUserDecisionBroker` 订阅，导致单纯进入或切换页面也写出“助手决策订阅已激活”日志；页面被重新挂载时还会重复出现。这个生命周期早于任何用户请求、Skill 加载或 `assistant.ask_user` 工具调用，与已有规格限定的“按需要询问”及“由 AI 判断后启用 Browser Bridge”不一致。

## What Changes

- 将助手决策订阅从 AssistantView 可视树挂载生命周期迁移到单次用户请求生命周期。
- 只有用户发送请求且 AssistantService 已就绪后，才在 AgentLoop/工具执行前订阅 Broker；请求完成、失败或取消时解除订阅。
- 保留页面离开时取消已领取决策请求的安全收敛，但页面挂载、打开模型选择器、切换 Provider 或控制台本身不再激活订阅。
- 保持既有 Skill 与 Browser Bridge 契约：AI 先判断任务是否涉及 Provider/中转站并加载 Skill，再由 Skill 指引按需调用 `browser.bridge_start`，导航行为不得启动 Bridge。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `assistant-user-decisions`: 明确 Broker UI 订阅属于活动 Assistant 请求，而不是页面可见性；补充导航空操作、请求开始和请求结束场景。

## Impact

- 影响 `AssistantViewModel` 的 Broker 订阅入口与请求收尾。
- 影响 `AssistantView` 的挂载/卸载职责：挂载只维护视图行为，卸载仅负责活动决策的取消收敛。
- 更新 Assistant 决策生命周期测试与 OpenSpec 验收场景；不改变 Broker、Skill、Browser Bridge、工具协议或数据库结构。
