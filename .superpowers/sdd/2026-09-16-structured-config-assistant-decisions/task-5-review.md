# Task 5 首轮任务级审查

### Spec Compliance

- ❌ **Issues found**：实现覆盖了基础模型、集中校验器、Broker 接口及主要 happy-path 生命周期，但未完整满足任务与全局设计。阻断项包括：敏感结果过滤不完整、字段判别契约允许互相矛盾的数据、Broker 存在校验/快照竞态且没有无订阅者/释放时的有界收敛、原始 `OwnerId` 可进入日志，以及任务简报明确要求的 OpenSpec 勾选未提交。
- ⚠️ **Cannot verify from diff**：Task 6/7 的 `assistant.ask_user` JSON 适配、DI、UI/Dialog 尚不在本任务 diff 中，因此无法验证最终工具 Schema 和 UI 消费方式；但本报告指出的模型与 Broker 契约缺陷已经能从当前 diff 直接确认，会阻碍后续安全消费。
- 聚焦外部检查（敏感模式）：检查了 `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:42-55`，确认现有策略只识别精确名称或以下划线结尾的后缀名称，不能把它当作完整的自由文本秘密扫描器。
- 聚焦外部检查（序列化契约）：检查了仓库中的 `JsonSerializerOptions`/`JsonStringEnumConverter` 使用点；当前模型自身没有固定字段类型的字符串表示或专用 converter，Task 5 测试也没有序列化往返测试，因此 `single_select` 等外部契约在本任务中未被锁定。
- 按要求未重新运行整套或定向测试；代码和实施报告已经足以回答本次发现。报告中的既有 `NU1903`、`CS8618`、`CA2024`、`CS8602` 警告不作为本任务问题。

### Strengths

- `LoomX/Assistant/UserDecisions/UserDecisionModels.cs:100-125`、`139-152`、`747-768` 对字段集合、选项集合、默认多选值、请求字段和常规多选结果进行了防御性复制；在预期的字符串/数字/字符串列表输入下，调用方后续修改不会改变已保存快照。
- `LoomX/Assistant/UserDecisions/UserDecisionModels.cs:168-182`、`596-643` 将校验错误收敛为字段 id 与固定中文消息，未把标题、问题、选项说明或提交原值拼入错误消息。
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:50-68` 正确使用不可预测 GUID、`ConcurrentDictionary` 和 `RunContinuationsAsynchronously`，并处理了 CancellationToken 在 registration 写入前触发的窗口。
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:121-127`、`141-147`、`163-170`、`187-195` 的正常完成路径遵循先 `TryRemove`、再完成 TCS、最后释放 registration 的总体顺序；重复完成会返回 `false`。
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:80-93` 没有把事件订阅者异常原文作为日志异常写出；`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs:128-147` 对该安全行为做了实际断言。
- 基础测试覆盖了任务简报列出的重复字段、默认选项、范围/步长、必填文本、取消空结果、request id 唯一、Submit/Cancel、CancelOwner、CancellationToken、事件异常和异步 continuation。

### Issues

#### Critical (Must Fix)

无。

#### Important (Should Fix)

1. **AskUser 敏感边界没有覆盖提交结果和常见自由文本秘密变体**
   - **位置**：`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:544-566`、`645-675`；聚焦外部检查：`LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs:42-55`；现有测试：`LoomX.Tests/Assistant/UserDecisionModelsTests.cs:127-149`。
   - **问题**：提交的 Text 值只检查类型、必填和长度，完全不调用敏感策略。展示文本扫描则先按 ASCII token 切分，再调用只支持“精确名称/下划线后缀”的 key policy；真实 API Key 值（如 `sk-...`）、`accessToken`、`clientSecret`、`credentials`、Authorization/Bearer 值等常见变体可以通过。现有测试只覆盖 `Authorization`、`api_key`、`database_password` 这些键名，不覆盖实际秘密值、变体或结果。
   - **影响**：违反 OpenSpec 4.4 和全局“AskUser 展示/工具结果不得泄漏 API Key、Authorization、自定义 Header 值或用户敏感输入”的边界；敏感自由文本可进入 `UserDecisionResult.Values`，后续 Task 6 工具结果会直接继承该风险。
   - **修复建议**：为自由文本建立明确的内容级敏感检测 API（不要仅复用路径 key 判断），在问题、说明、影响摘要、所有可展示字段以及 Text 提交结果上统一调用；补充实际 key/token 值、camelCase、复数、嵌套/分隔符变体和“结果不含敏感内容”的测试。

2. **四类字段并未形成封闭的判别联合，类型不相关属性可携带未校验数据**
   - **位置**：`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:30-98`、`266-291`、`294-437`。
   - **问题**：单个 `UserDecisionField` 同时公开选择、数字和文本的全部属性；validator 只按 `Type` 校验当前分支，未拒绝其他分支属性。例如 Number/Text 字段可以携带 `Options` 和敏感选项说明，SingleSelect 可以携带 `DefaultText`、number 范围或多选默认值，这些内容会留在不可变快照中却不经过对应校验。Task 5 也没有序列化往返测试来固定 `single_select`/`multi_select` 等外部表示。
   - **影响**：模型虽然集合不可变，但状态并不合法且不具备可靠判别性；后续 JSON/tool/UI 若序列化或绑定全部公共属性，会遇到冲突数据、安全校验绕过和不稳定契约。此实现没有真正满足 `task-5-brief.md:11`、`:36-38` 以及设计中的“模型序列化与校验”要求。
   - **修复建议**：优先改为共享基类/接口加四个 sealed 字段 record；若保持单类，必须逐类型拒绝所有不适用属性，并显式定义 JSON discriminator/枚举字符串契约。为四类合法/非法组合和 JSON round-trip 增加契约测试。

3. **Submit 对可变/惰性 values 先校验、后再次枚举，并在移除 pending 后才构造结果**
   - **位置**：`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:238-263`、`491-527`、`747-768`；`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:105-127`。
   - **问题**：多选 `IEnumerable<string>` 在校验时 `ToArray()` 一次，`UserDecisionResult.Submit` 又枚举一次；整个 dictionary 也在校验后再次枚举。调用方可在两次读取之间修改集合，或提供状态化 enumerable，使“被验证的值”与“被保存的值”不同。更严重的是，`TryRemove` 已在第 121 行成功后，第 126 行的结果复制仍可能因并发修改/第二次枚举而抛异常。
   - **影响**：可绕过选项/数量校验；若复制抛异常，请求已从字典移除但 TCS 未完成、registration 未释放，造成永久悬挂和资源泄漏。现有测试只使用稳定的 `Dictionary`/`List`，没有覆盖该竞态。
   - **修复建议**：在移除 pending 前一次性把输入规范化为受控不可变快照，并对该同一快照完成校验；只有快照和校验都成功后才 `TryRemove`，随后用不会再访问调用方对象的结果完成 TCS。增加可变集合、单次枚举 enumerable，以及 Submit/Cancel/token 并发竞态测试。

4. **Broker 对无订阅者和自身释放没有有界收敛路径**
   - **位置**：`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:22-26`、`28-97`、`202-261`；需求：`openspec/changes/structured-config-assistant-decisions/tasks.md:24`，设计 `docs/superpowers/specs/2026-09-16-structured-config-assistant-decisions-design.md:168`、`:197`。
   - **问题**：`PendingRequested` 为 null 时，第 78 行什么也不做，`CancellationToken.None` 请求会永久保留并等待；类也未实现 `IDisposable`/`IAsyncDisposable`，无法在 singleton/Host 释放时取消并清空 pending。测试始终先订阅事件，且没有超时、无订阅者或 Broker dispose 用例。
   - **影响**：UI 未激活、订阅丢失或应用关闭时会留下永不完成的工具调用和 CancellationTokenRegistration，违反“超时/会话取消且不会永久阻塞”的生命周期要求，并使后续 Task 6/7 难以可靠收敛。
   - **修复建议**：定义 Broker 级有界等待策略（可配置超时或无订阅者立即安全失败），实现释放时原子移除并取消所有 pending；补充无订阅者、超时、dispose 与并发完成的测试。

5. **日志直接记录未经约束的 OwnerId，安全声明并不成立**
   - **位置**：`LoomX/Assistant/UserDecisions/UserDecisionBroker.cs:33-36`、`70-74`、`88-93`、`113-117`、`128-132`、`148-151`、`176-179`、`196-199`；测试：`LoomX.Tests/Assistant/UserDecisionBrokerTests.cs:200-222`。
   - **问题**：`ownerId` 只校验非空，却在所有开始/完成/失败日志中原样记录。当前日志测试只检查提交值和取消原因，未验证 owner id；调用方若误传会话标题、路径、Header、token 或其他用户文本，会直接进入 Serilog。
   - **影响**：违反业务日志不得包含 API Key、Authorization、自定义 Header 值或用户自由文本的全局约束。Broker 的公开接口没有证据能保证所有未来调用方只传安全 GUID。
   - **修复建议**：把 owner id 收敛为内部生成/强格式标识，或日志中仅记录不可逆摘要并限制长度；增加敏感 owner id 不进入 logger state/格式化文本/异常的测试。

6. **任务简报要求的 OpenSpec 状态更新完全缺失**
   - **位置**：`.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-5-brief.md:3-8`、`:54-63`；`openspec/changes/structured-config-assistant-decisions/tasks.md:23-26`。
   - **问题**：简报把 `tasks.md` 列为必须修改文件，并要求勾选 4.1–4.2、4.4 的模型/Broker部分；review package 的文件列表没有该文件，当前对应条目仍全部为 `[ ]`。
   - **影响**：这是 task-reviewer 模板明确规定的 Missing finding，导致任务状态与提交内容不一致，后续 Comet/OpenSpec 流程无法可靠判断 Task 5 已完成的范围。
   - **修复建议**：在代码问题修复并验证后，按实际完成范围更新 4.1、4.2、4.4；若条目包含后续任务内容，应拆分或明确只勾选可独立完成的子项，不能用实施报告替代状态文件。

#### Minor (Nice to Have)

1. **772 行新文件聚合了过多职责**
   - **位置**：`LoomX/Assistant/UserDecisions/UserDecisionModels.cs:1-772`，其中 validator 单独占 `185-709`。
   - **问题**：选项/字段/请求模型、错误与异常、全部请求/提交校验、结果模型和 pending event 模型都放在一个文件中；四类字段校验和展示文本校验需要跨数百行追踪。
   - **影响**：没有立即造成功能错误，但已经增加审查和后续 Task 6/7 修改的认知成本，也让判别类型与安全策略更难独立测试。
   - **修复建议**：在不扩大公共 API 的前提下至少拆成 Models、Validation、Result/Pending 三个清晰单元；不要为拆分引入额外框架或抽象层。

### Assessment

**Task quality:** Needs fixes

**Reasoning:** 基础不可变快照和 Broker 正常路径实现较扎实，但敏感结果过滤、字段判别契约、Submit 原子性以及无订阅者/释放生命周期均存在阻断风险，且 OpenSpec 必改文件缺失。修复这些 Important findings 并补齐针对性测试前，Task 5 不能批准。
