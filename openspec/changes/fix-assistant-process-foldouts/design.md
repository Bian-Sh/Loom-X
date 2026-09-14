# 修复设计

## 上下文

`AgentLoop` 已提供能够区分中间 assistant 消息与最终 assistant 消息的完整事件：带工具调用的 `MessageCompleted` 仍属于处理中，无工具调用的 assistant `MessageCompleted` 才代表最终内容完整输出。修复只调整 Avalonia 投影和展示，不修改 Harness 事件契约或持久化格式。

## 决策

1. `currentGroup` 的生命周期从单个 step 提升到单轮：`StepStarted` 和 `TextDelta` 只切换正文流，不结束过程组；最终 assistant `MessageCompleted` 才冻结耗时、切换“已完成”并折叠父组。新一轮 `SessionStarted` 或历史中的下一条用户消息只断开旧引用，不把未完成过程误标为完成。
2. 父过程组运行时默认展开，标题统一使用“处理中/已完成 + 耗时”；结束后强制折叠。历史重放沿用同一投影规则，工具活动缺失时把消息中的工具调用补入当前父组。
3. `ProcessItemViewModel` 承担子 foldout 状态。折叠标题取该子项最新非空行，展开标题显示固定标签并在下方显示完整文本。父子箭头都放在标题文本后并使用白色。
4. 保留现有 `ChatMessageViewModel` 与事件投影结构，不新增控件、依赖或协议字段。

## 风险与缓解

- 历史失败会话没有最终内容时过程组保持“处理中” → 新用户轮次会断开该组，避免内容串组；本次不发明新的失败标题状态。
- 同一时间戳的历史 entry 顺序可能退化 → 仍以消息与活动已有顺序投影，并用最终 assistant 消息作为唯一完成边界。

## 验证

先用 ViewModel 回归测试覆盖多 step、多工具、最终内容完成边界和子 foldout 标题，再执行完整测试、构建及 `win-x64` 发布包界面验收。
