# Task 7 审查报告

## 结论

- 规格符合性：未通过
- 任务质量：需要修复
- 审查范围：`27dd97e86897ce60793df87d9514db126e211ea1..9f0b128dcddabaf13559c8a4c4fbff23fda7b35e`
- 修复轮：进入第 1 轮

## 优点

- 四类字段从当前可变状态构造结果，并复用 `UserDecisionValidator.ValidateSubmission` 进行最终校验。
- 取消路径不构造提交结果，不会把默认值带回 Broker。
- Dialog 使用透明窗口、动态资源、圆角边框、滚动字段区和四类模板，提交/取消 Toast 为固定安全摘要。
- 定向测试覆盖默认值、required、多选 min/max、数字 min/max/step、文本最大长度及关闭 Dialog 不提交默认值。

## Critical

### request ownership 非排他，且可能取消未拥有的请求

`AssistantViewModel` 在忙碌、停用或解除订阅时，会直接取消新收到的 request id，但该 request id 从未被当前实例声明为 owned。真实 Broker 会把同一个 pending 发布给所有订阅者，旧页面、第二窗口或 busy VM 可能取消另一个 UI 正在处理的请求；两个空闲 VM 也可能同时打开同一请求的 Dialog。

正常完成路径还会先清空 owned id，再调用 `Submit`/`Cancel`，导致完成动作发生时形式上已不再拥有请求，并留下停用、提交和新请求进入之间的竞态窗口。

要求：引入 Broker 级原子 claim/ownership，只有成功 claim 的 UI 才能完成或取消请求；未 claim 的订阅者必须忽略；提交/取消完成前不得提前释放 ownership；使用真实 Broker 与多订阅者并发测试覆盖。

## Important

### 生命周期未接入生产页面与窗口

构造函数立即 `Activate`，但 `AssistantView` 的 attached/detached 未调用 `Activate`/`Deactivate`，`MainWindowViewModel.Dispose` 也未释放 `AssistantViewModel`。因此生产页面关闭或主窗口释放后仍可能保留 Broker 订阅和 pending ownership。

要求：把激活、停用和释放接入真实页面/窗口生命周期，并以测试证明订阅和 owned pending 会可靠收敛。

### 新事件流程吞掉异常且未记录安全日志

UI dispatcher、Dialog 和提交处理中的异常只显示 Toast，没有通过 `ILogger<AssistantViewModel>` 记录异常对象及安全事件边界。

要求：按项目日志规范补齐开始、完成、降级与失败日志；禁止记录问题正文、字段值、OwnerId、取消原因、Header、prompt 或工具参数。

## Minor

### ErrorSummary 未接入本地化资源

`ErrorSummary` 为硬编码中文。若不扩大写集，应接入现有 Locale 资源体系；若因范围控制延期，需在修复报告中说明裁决与代价。

## 复审要求

- 先写失败测试，再修复实现。
- 重点复审真实 Broker 的原子 ownership、多订阅者竞争、提交/取消竞态、页面和主窗口生命周期、日志安全。
- 透明主题实际视觉与 Avalonia 运行时模板/双向绑定仍需在后续 GUI 验证中确认。