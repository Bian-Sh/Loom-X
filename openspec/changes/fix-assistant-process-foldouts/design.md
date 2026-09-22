# 修复设计

## 上下文

`AgentLoop` 已提供能够区分中间 assistant 消息与最终 assistant 消息的完整事件：带工具调用的 `MessageCompleted` 仍属于处理中，无工具调用的 assistant `MessageCompleted` 才代表最终内容完整输出。修复只调整 Avalonia 投影和展示，不修改 Harness 事件契约或持久化格式。

## 决策

1. `currentGroup` 的生命周期从单个 step 提升到单轮：`StepStarted` 和 `TextDelta` 只切换正文流，不结束过程组；最终 assistant `MessageCompleted` 才冻结耗时、切换“已完成”并折叠父组。新一轮 `SessionStarted` 或历史中的下一条用户消息只断开旧引用，不把未完成过程误标为完成。
2. 父过程组运行时默认展开，标题统一使用“处理中/已完成 + 耗时”；结束后强制折叠。历史重放沿用同一投影规则，工具活动缺失时把消息中的工具调用补入当前父组。
3. `ProcessItemViewModel` 在整轮内按标签复用，只形成“思考”和“工具调用”两个子组。标题在处理中且折叠时取最新非空行；父过程结束后通知子项刷新，无论子项是否展开都固定显示多语言标签。
4. `MessageCompleted` 中 assistant 工具调用提供函数名与完整参数，tool 消息提供完整结果；投影层按 `ToolCallId` 关联到 `ProcessToolCallViewModel`，分别维护“调用详情”和“完成/失败详情”的独立折叠状态，不扩展 Harness 事件契约或 JSONL 格式。
5. XAML 对过程按钮显式覆盖 pointerover、pressed、checked 和 checked+pointerover 状态，全部保持透明；父子箭头默认隐藏，划入时显示 `#CCFFFFFF`。工具内层入口只使用低对比文字，无背景、无 selected 底色、无箭头。
6. 本地 `.local/assistant-session-mock` 模块直接组装 v2 JSONL 并写入会话目录用于视觉验收；通过 `.git/info/exclude` 排除，不加入项目引用与 Release 发布内容。

## 风险与缓解

- 历史失败、拒绝或取消会话没有最终内容时过程组保持“处理中” → 继续严格以 `finish_content` 为唯一完成边界，不用任务状态冒充完成。
- 同一时间戳的历史 entry 顺序可能退化 → 仍以消息与活动已有顺序投影，并用最终 assistant 消息作为唯一完成边界。
- 旧会话可能缺少工具活动 → 仍从 tool message 恢复结果，并把存在工具结果视为该次调用已完成。
- 输出中切换会话存在既有的跨会话持久化与 UI 投影风险 → 本次只增加可复现的跳过测试和 TODO，不把会话生命周期修复混入 foldout hotfix。

## 验证

先用 ViewModel 回归测试覆盖两个稳定子组、最终内容完成边界、工具参数/结果关联和内容可见性，再执行完整测试、构建及 `win-x64` 发布包界面验收；视觉阶段使用本地 Mock JSONL 覆盖长内容和异常分支。
