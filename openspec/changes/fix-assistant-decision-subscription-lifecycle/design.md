## Context

见 `proposal.md`。底层 `assistant.ask_user` 工具本身由 ToolRegistry 直接注册，实际并不依赖 Skill 或 Browser Bridge；过度限制来自规格措辞与系统提示中的使用场景偏置。当前 Dialog 为 620×680 的全部字段表单，无法表达用户指定的逐题 Approval Card 交互。

视觉与交互参考来自 Beautiful UI 的 Approval Card registry item：单题分页、步骤计数、Skip/Continue、单选自动前进和多选等待确认。实现只借鉴交互模型，不复制 React/Tailwind 代码或配色。

## Goals / Non-Goals

**Goals:**

- 让模型在用户明确要求或确有交互需要时直接调用 AskUser。
- 让 AskUser 与 Skill、Bridge、Chrome 生命周期彻底解耦。
- 用紧凑、逐题、可回看且不丢值的 Approval Card 替换大型全量表单。
- 保持四类字段、敏感信息边界、Broker Submit/Cancel 和请求级订阅生命周期稳定。

**Non-Goals:**

- 不增加新的字段类型或改变 UserDecisionResult 的值类型。
- 不为单选/多选混入“自定义文本”复合返回值；自由文本继续使用独立 `text` 字段，避免破坏 option id 契约。
- 不修改 Browser Bridge 租约协议、Skill 内容加载协议或 Chrome Extension。

## Decisions

### 1. AskUser 是通用工具，而不是资料兜底工具

更新系统提示与工具描述：用户明确要求测试 AskUser、收集选择/数字/文本、澄清歧义或确认行动时，可以直接调用。普通步骤“不强制询问”只表示默认不打扰，不构成禁止条件。Skill 与 Bridge 仅在任务本身需要领域知识或浏览器时加载。

### 2. 每个 UserDecisionField 对应一页

`AskUserDialogViewModel` 增加当前索引、当前字段、步骤文本、前后导航、跳过、继续和提交状态。字段 ViewModel 继续保存真实输入，因此来回切换不会丢值。最终仍由 `TryBuildResult` 一次性验证和构造原有字典结果。

### 3. 跳过只处理当前可选字段

可选字段可跳过并清空当前值；必填字段的跳过按钮禁用。最后一页跳过后尝试提交整个请求。关闭按钮和 Escape 只在 `allow_cancel=true` 时取消整个请求。

### 4. 单选自动前进属于 View 交互

选择单选项后由 Dialog 代码后置执行短延迟自动前进；ViewModel 只提供确定性的 `TryAdvanceCurrentField`。多选、数字和文本必须由用户点击继续。这样业务状态可单元测试，动画与延迟不会渗入模型层。

### 5. 主题与动画采用 Avalonia 原生能力

窗口使用固定紧凑宽度、内容驱动高度、现有 DynamicResource、无系统装饰和 Owner 居中。字段内容使用 ContentControl/DataTemplate 切换，并使用轻量透明度/位移过渡；窗口与内容在禁用动画时仍完整可用。不会照搬参考页面的黑色主题。

### 6. “Something else”映射为 text 字段

现有 selection 返回 option id，直接加入自由文本会导致返回类型含义不稳定。因此选择页只显示定义的 options；需要补充文本时，AI 在同一请求中追加一个 `text` 字段，它会作为下一页呈现，并使用类似参考图的透明输入视觉。

## Risks / Trade-offs

- [逐题模式隐藏了其他字段] → 提供明确步骤计数、前后导航和最终统一校验。
- [单选自动前进可能过快] → 使用短延迟，并仅在当前页和选中项仍有效时前进。
- [可选字段与跳过含义混淆] → Skip 明确清空当前字段；Cancel 明确取消整个请求。
- [窗口动态高度动画在不同平台表现不同] → 高度自适应优先，动画为渐进增强，不作为提交逻辑前提。
