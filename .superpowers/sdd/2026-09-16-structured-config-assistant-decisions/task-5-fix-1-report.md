# Task 5 修复轮 1 报告

## 结论

本轮仅修复首轮审查 Important 1–5；Important 6 按协调者裁定保持流程状态不变，Minor 文件拆分未处理。实现、定向测试、TOML 敏感策略回归、全量测试、格式检查和 diff 检查均通过。

## 开始前远端状态

- 当前分支：`codex/structured-config-assistant-decisions`。
- 初始 `git status --short --branch` 显示远端跟踪引用为 `[gone]`。
- 执行 `git fetch origin` 后确认远端分支真实存在，远端 SHA 为 `3a4918b0009a0c4e4115a9d43cb011e74d5e69aa`。
- `git rev-list --left-right --count HEAD...origin/codex/structured-config-assistant-decisions` 结果为 `0 0`。
- fetch/pull 的后台 geometric repack 报告本地对象库中存在 `bad tree object e42a7d...`，但远端引用已成功恢复，`git pull --ff-only` 明确报告 `Already up to date.`；本轮未执行 `reset`、`clean`、`stash`、强推或共享 `.git` 元数据修改。

## Finding 修复映射

### Important 1：内容级敏感检测

- 在 `SensitiveKeyPolicy` 新增统一 `ContainsSensitiveContent` API，与 TOML 路径级 `IsSensitivePath` 分离，未改变既有 TOML 路径脱敏流程。
- 内容扫描覆盖：
  - camelCase、复数、分隔符及嵌套形式，如 `accessToken`、`clientSecrets`、`serviceKeys`、`api-keys`、`headers[refresh_tokens]`、`provider.credentials.value`；
  - `Authorization`、`Bearer`；
  - 常见 token/API key 值前缀和 JWT 形态，如 `sk-`、GitHub token、Slack token、AWS/Google key、JWT。
- AskUser 标题、问题、说明、影响摘要、字段标签、选项标签/说明、文本默认值统一调用内容级检测。
- Text 提交值在生成 `UserDecisionResult` 之前被检测；敏感提交返回 `false`，pending 保留，后续安全提交可完成，敏感值不会构造为提交结果。
- 错误仅包含字段 id 与固定中文安全消息，不回显原始敏感值。

### Important 2：字段判别封闭性与 JSON 契约

- 保持 brief 指定的 `enum + 明确属性` 模型。
- 为四种字段逐类型拒绝全部不适用属性：
  - `SingleSelect` 拒绝多选、数值、文本专属属性；
  - `MultiSelect` 拒绝单选默认值、数值、文本专属属性；
  - `Number` 拒绝选择与文本专属属性；
  - `Text` 拒绝选择与数值专属属性。
- 使用 .NET 10 `JsonStringEnumMemberName` + 泛型 `JsonStringEnumConverter` 固定外部枚举值：`single_select`、`multi_select`、`number`、`text`。
- 新增四类合法模型的 JSON 序列化/反序列化往返测试，以及四类典型跨类型非法组合测试。

### Important 3：Submit 单次快照与原子性

- `Submit` 在 `TryRemove` 前只顺序枚举调用方字典一次，并将多选 `IEnumerable<string>` 只枚举一次，生成受控只读快照。
- 校验和结果构造均使用同一快照；只有快照和校验成功后才构造结果并尝试移除 pending。
- 快照枚举抛异常或校验失败时返回 `false`，不移除 pending，不记录异常消息或值，允许后续重新提交。
- `TryRemove` 成功后不再访问调用方字典或多选 enumerable；完成 TCS 的路径通过 `finally` 释放 `CancellationTokenRegistration`。
- 新增拒绝随机读取/重复枚举的状态化字典、单次枚举多选序列、快照异常后重试、Submit/Cancel 并发、Submit/token 并发测试。

### Important 4：无订阅者与释放收敛

- `IUserDecisionBroker` 明确继承 `IDisposable`。
- 无 `PendingRequested` 订阅者时立即返回固定安全异常，不创建 pending，不永久等待。
- Broker 使用生命周期锁封闭订阅、请求创建和 dispose 状态；dispose 原子标记后移除并取消全部 pending，重复 dispose 安全。
- Submit、Cancel、token、事件发布失败和 dispose 均以 `TryRemove` 决胜，并统一保证 registration 释放。
- 新增无订阅者、dispose 清空、dispose 后请求、dispose/Submit 并发测试；任务在成功或取消两种竞态结果下均有界收敛。

### Important 5：OwnerId 日志安全

- 所有 Broker 日志移除原始 `ownerId` 属性和值。
- 日志仅保留 request id、字段/请求计数和异常类型等安全摘要。
- 新增包含 Authorization、Bearer token、Windows 用户路径和用户文本的 ownerId 测试，同时检查格式化消息、logger state 和异常文本均不包含原值。
- 提交值、自由文本、取消 reason、ownerId 均不进入日志；快照异常仅记录异常类型，不记录原异常对象或消息。

## TDD RED / GREEN 证据

### 轮次 1：统一内容级敏感检测

- RED：
  - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~SensitiveKeyPolicyTests|FullyQualifiedName~UserDecisionModelsTests"`
  - 预期失败：`SensitiveKeyPolicy.ContainsSensitiveContent` 尚不存在。
  - 结果：失败，`CS0117` 两处，确认测试命中缺失 API。
- GREEN：同命令通过，`44/44`。
- 补充变体 RED：`dotnet test ... --filter FullyQualifiedName~SensitiveKeyPolicyTests`，`serviceKeys` 用例失败，`1` 失败、`24` 通过。
- 补充变体 GREEN：同命令通过，`25/25`。

### 轮次 2：字段封闭性和序列化契约

- RED：
  - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~UserDecisionModelsTests`
  - 预期失败：枚举仍输出数字；跨类型属性未拒绝。
  - 结果：`2` 失败、`22` 通过；分别为 JSON 判别值缺失和非法组合未报错。
- GREEN：同命令通过，`24/24`。

### 轮次 3：Submit 单次快照

- RED：
  - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~UserDecisionBrokerTests`
  - 预期失败：旧实现读取 `Keys` 并在结果构造时二次枚举。
  - 结果：`2` 失败、`13` 通过；一处因读取 `Keys` 抛异常，一处在移除后结果复制时抛异常。
- GREEN：同命令通过，`15/15`。

### 轮次 4：无订阅者与 dispose

- RED：
  - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~UserDecisionBrokerTests`
  - 预期失败：Broker 尚未实现 `Dispose`。
  - 结果：失败，出现多处预期 `CS1061`（缺少 `Dispose`）。同次检查发现 dispose 后请求测试的 lambda 误选异步断言重载，先修正测试表达式，再实现生产代码。
- GREEN：同命令通过，`19/19`。

### 轮次 5：OwnerId 日志安全

- RED：
  - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~UserDecisionBrokerTests`
  - 预期失败：格式化日志和 state 包含原始 ownerId。
  - 结果：`1` 失败、`19` 通过，断言捕获原始 ownerId。
- GREEN：同命令通过，`20/20`。

## 最终验证

1. Task 5 定向测试：
   - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecisionModelsTests|FullyQualifiedName~UserDecisionBrokerTests|FullyQualifiedName~SensitiveKeyPolicyTests"`
   - 结果：通过，`69/69`。
2. Task 4 敏感策略/TOML 回归：
   - 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~SensitiveKeyPolicyTests|FullyQualifiedName~TomlDocumentServiceTests"`
   - 结果：通过，`71/71`。
3. 全量测试：
   - 命令：`dotnet test LoomX.slnx --no-restore`
   - 结果：通过，`825/825`。
4. 格式验证：
   - 命令：`dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/UserDecisions/UserDecisionModels.cs LoomX/Assistant/UserDecisions/UserDecisionBroker.cs LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs LoomX.Tests/Assistant/UserDecisionModelsTests.cs LoomX.Tests/Assistant/UserDecisionBrokerTests.cs LoomX.Tests/Assistant/SensitiveKeyPolicyTests.cs`
   - 结果：通过；仅输出既有工作区加载警告提示。
5. Diff 检查：
   - 命令：`git diff --check`
   - 结果：通过，无输出。

测试过程持续出现仓库既有警告：`NU1903`、`CS8618`、`CA2024`、`CS8602`；本轮未修改相关文件，且全量测试通过。

## 改动文件

- `LoomX/Assistant/Configuration/SensitiveKeyPolicy.cs`
- `LoomX/Assistant/UserDecisions/UserDecisionModels.cs`
- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs`
- `LoomX.Tests/Assistant/SensitiveKeyPolicyTests.cs`
- `LoomX.Tests/Assistant/UserDecisionModelsTests.cs`
- `LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-5-fix-1-report.md`

未修改 `docs/superpowers/plans`、OpenSpec tasks、`.comet.yaml`、`subagent-progress.md` 或其他流程状态文件。

## 自审

- 检查所有日志调用，未发现 values、自由文本、取消 reason、ownerId、prompt、API Key、Authorization、Header 值或正文进入日志模板参数或异常对象。
- 检查所有 Broker 完成路径，均由 `TryRemove` 决胜，并在 `finally` 中释放 registration。
- 检查 Submit 时序，敏感/非法值只进入临时快照和校验，不会先构造 `UserDecisionResult`；成功移除后不再访问调用方对象。
- 检查字段判别规则，四种类型均覆盖所有其他类型的专属属性。
- 检查作用域，除允许文件和本报告外无其他改动。

## 已知风险

- 内容级敏感检测有意采用保守策略，裸 `key`、`token`、`secret` 等词可能拒绝少量本来无敏感值的展示文本；这是安全优先的取舍，调用方应改用不含凭据术语的业务文案。
- 未生成 standalone 发布包：本轮用户明确限制可修改文件范围，且工作内容为领域模型/Broker/测试修复；已以 `825/825` 全量测试替代发布包验证。
- Git 后台 geometric repack 仍报告既有 `bad tree object e42a7d...`；本轮未触碰共享 `.git` 元数据。远端分支引用与当前 HEAD 在开始时一致，推送结果需在提交后单独记录。
