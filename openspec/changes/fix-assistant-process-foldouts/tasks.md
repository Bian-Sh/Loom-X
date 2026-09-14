# 任务

## 1. 回归测试

- [x] 1.1 增加同一轮仅两个子组、完成后固定标签、工具参数/结果关联与内容可见性的失败测试，并验证测试按预期失败。

## 2. 最小修复

- [x] 2.1 修正子组复用、完成通知、工具详情投影和历史重放，更新四种语言资源与透明 XAML 交互状态，并验证定向 ViewModel 测试通过。

## 3. 集成验证

- [x] 3.1 使用本地忽略的 JSONL Mock 覆盖长思考、多工具、成功、失败、拒绝、取消、处理中和已完成场景；执行完整测试与构建，重新发布 `win-x64` 到带可读时间的 `outputs/` 并完成 GUI 验收。

## 后续 TODO（本次范围外）

- AI 正文流输出期间切换会话时，运行循环虽然继续写入启动时的 `AgentSession`，但 `AssistantService.SendAsync` 的活动记录和持久化仍读取可变的 `CurrentSession`，`AssistantViewModel.Project` 也未按 `AgentEvent.SessionId` 隔离当前视图。已加入跳过的回归测试 `SendAsync_SwitchSessionDuringStreaming_PersistsOriginalRunWithoutPollutingViewedSession` 固化预期；后续应让旧运行继续写回原会话，同时禁止其增量污染新查看会话。

<!-- review skipped: hotfix 预设 review_mode=off；已执行定向测试、完整回归、Release 构建与发布包 GUI 验收。 -->
