# Task 7 第 1 轮修复报告

日期：2026-09-17
基线：`c654fcea47b6292f51d3e84f6308d4a7029d1798`

## 修复范围

- 在 `IUserDecisionBroker` / `UserDecisionBroker` 增加原子 `TryClaim`、`Release` 与带 claimant 的 `Submit` / `Cancel` 契约。
- 每个 pending 同时只允许一个 claimant；未 claim 或 claimant 不匹配的调用不能提交、取消或释放。提交/取消开始后锁定 claim，避免 `Release` 与完成动作交叉后由旧 claimant 完成新 owner 的请求。
- `AssistantViewModel` 为每个实例生成仅用于 Broker 校验的 claimant id；该 id 不进入日志、Toast 或用户界面。busy、停用、未 claim 的实例只忽略 pending，不再取消他人请求。
- 本地 ownership 只在 Broker 的提交/取消调用返回后清理；提交失败时尝试安全取消，Dialog 完成与 `Deactivate` / `Dispose` 竞态使用幂等结果收敛，不再产生停用后的伪失败 Toast。
- `AssistantView` 在 attached / detached 及已挂载时切换 `DataContext` 的路径上调用 `Activate` / `Deactivate`。`AssistantViewModel` 构造时不再提前订阅隐藏页面的 Broker。
- `MainWindowViewModel` 实现幂等 `IDisposable`，并释放其持有的 `AssistantViewModel`。
- 为 pending、claim、Dialog、dispatcher、submit、cancel、页面激活/停用补齐结构化日志。异常路径向 `ILogger<AssistantViewModel>` 传入安全异常对象，只记录 request id、固定事件类型、结果分类与原异常类型名。
- `AskUserDialogViewModel.ErrorSummary` 改为 `assistant.decision.validation_failed` Locale 资源；提交/取消 Toast 继续保留既定固定安全摘要。
- 未修改 OpenSpec 勾选、`.comet/subagent-progress.md`、数据库路径、Task 8 发布产物、`outputs/`、`.planning/` 或 `.codegraph/`。

## 设计裁决

### Broker ownership

- `ownerId` 继续表示发起 AskUser 的 Assistant run，用于 `CancelOwner` 收敛整个运行；新增 `claimantId` 表示实际处理 pending 的 UI 实例，两者职责分离。
- `TryClaim` 在 `PendingEntry` 内部锁下原子设置 claimant；重复 claim 返回 `false`。
- `Submit` / `Cancel` 先在同一状态锁下进入 completion 状态；completion 期间 `Release` 和第二个完成动作均失败。校验失败会退出 completion 状态并保留原 claim，允许同一 claimant 重试。
- `CancelOwner`、请求 `CancellationToken` 与 Broker `Dispose` 仍作为运行级管理动作，可以终止已 claim 的 pending；UI claimant 随后的完成操作安全返回 `false`。

### UI ownership 与竞态

- `AssistantViewModel` 只有在 active、已订阅且当前没有 owned pending 时才尝试 claim。
- 未 claim 的订阅者不会打开 Dialog，也不会在停用时取消请求。
- Dialog 返回后，提交/取消在 `userDecisionGate` 内确认本地 ownership，并在 Broker 完成后清理本地 request id。
- 若 `Deactivate` 已先完成取消，迟到的 Dialog 结果按“ownership 已结束”忽略，不显示错误 Toast。

### 日志安全

- 日志不记录字段值、问题正文、用户自由文本、API Key、Authorization、自定义 Header、请求/响应正文、prompt、工具参数、OwnerId、claimantId 或取消 reason。
- 捕获到的异常可能携带敏感 message，因此日志首参使用固定安全消息的新异常对象，原异常只提取类型名作为结构化字段，不保留原 message / stack 中的用户内容。

## TDD 证据

### RED 1：缺少原子 claim 与主窗口生命周期契约

先新增真实 Broker ownership、多 VM、生命周期、日志安全和 Locale 测试，执行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantViewModelUserDecisionTests|FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~UserDecisionBrokerTests|FullyQualifiedName~AssistantDecisionLifecycleTests"
```

退出码：1。预期失败包括：

- `UserDecisionBroker` 缺少 `TryClaim` / `Release`。
- `Submit` / `Cancel` 没有 claimant 参数重载。
- `MainWindowViewModel` 缺少可注入受管 `AssistantViewModel` 的生命周期测试入口。

### RED 2：提交进行中仍可释放 claim

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~Submit进行中_不能释放Claim给其他处理者"
```

退出码：1。`broker.Release(...)` 实际返回 `true`，证明旧实现允许 completion 与 claim 转移交叉。

### RED 3：停用与 Dialog 完成竞态产生伪失败 Toast

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~Deactivate_解除订阅并取消当前页面请求且重复完成安全收敛"
```

退出码：1。集合中同时出现“已取消助手决策”和错误级“助手决策未能提交，请重试”。

### RED 4：ErrorSummary 仍为硬编码中文

新增 Locale 回归测试后，将实现临时回退为硬编码以验证测试能够捕获 finding：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~ErrorSummary_使用当前Locale资源"
```

退出码：1。`en-US` 期望 `Review the decision fields and try again.`，实际仍为 `请检查决策字段后重试。`。恢复 Locale 实现后同一测试 1/1 通过。
### GREEN

- 引入 completion 状态后，`Submit进行中_不能释放Claim给其他处理者`：1/1 通过。
- 区分“未拥有”与“完成失败”后，Deactivate 竞态测试：1/1 通过。
- 首轮修复定向集合：39/39 通过；补齐 completion / Toast 竞态测试后，最终 Task 7 定向集合：64/64 通过。

## 最终验证

- Task 7 定向测试：

  ```powershell
  dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AssistantViewModelUserDecisionTests|FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~AssistantDecisionLifecycleTests|FullyQualifiedName~UserDecisionBrokerTests|FullyQualifiedName~WindowAppearanceCoordinatorTests"
  ```

  - 通过：64，失败：0，跳过：0。

- 受影响的 UserDecision / AssistantTools / AssistantService：

  ```powershell
  dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecision|FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests"
  ```

  - 通过：94，失败：0，跳过：0。

- 全量测试：

  ```powershell
  dotnet test LoomX.slnx --no-restore
  ```

  - 通过：870，失败：0，跳过：0。

- Formatter（所有本轮新增文件及原本 formatter-clean 的受影响 C# 文件）：

  ```powershell
  dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/UserDecisions/UserDecisionBroker.cs LoomX/ViewModels/AssistantViewModel.cs LoomX/ViewModels/AskUserDialogViewModel.cs LoomX.Tests/Assistant/UserDecisionBrokerTests.cs LoomX.Tests/Assistant/UserDecisionBrokerTestExtensions.cs LoomX.Tests/Assistant/AssistantViewModelTests.cs LoomX.Tests/Assistant/AssistantServiceTests.cs LoomX.Tests/Views/AssistantDecisionLifecycleTests.cs LoomX.Tests/Views/AskUserDialogContractTests.cs
  ```

  - 退出码：0；仅输出既有工作区加载警告。
  - `MainWindowViewModel.cs` 全文件仍有本任务前已存在的多处 whitespace / charset formatter 报告；`AssistantView.axaml.cs` 保留其既有无 BOM 编码时会触发 charset 报告。为避免无关整文件格式化或编码转换，本轮不扩大写集，改由行为测试、编译和 `git diff --check` 验证本轮修改。

- `git diff --check`
  - 通过。

测试过程中仍可见仓库既有警告：`SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903、`SettingsViewModel` 的 CS8618、`AnthropicResponseMapper` 的 CA2024、测试代码的 CS8602；本任务未扩大范围处理。

## 安全边界

- 未新增搜索 API Key、爬虫、Cookie 注入、TLS / 浏览器指纹伪装或网站安全绕过。
- 未修改设置数据库路径或创建第二份运行时数据库。
- 未使用 `Console.WriteLine`、`Console.Error.WriteLine` 或 `Debug.WriteLine` 记录诊断。
- Toast 仍只包含固定摘要；日志安全测试覆盖敏感问题文本、OwnerId、claimantId、异常 message 和取消 reason 不进入日志。

## 改动文件

- `LoomX/Assistant/UserDecisions/UserDecisionBroker.cs`
- `LoomX/ViewModels/AssistantViewModel.cs`
- `LoomX/ViewModels/AskUserDialogViewModel.cs`
- `LoomX/ViewModels/MainWindowViewModel.cs`
- `LoomX/Views/AssistantView.axaml.cs`
- `LoomX/Resources/Strings.resx`
- `LoomX/Resources/Strings.en-US.resx`
- `LoomX/Resources/Strings.ja-JP.resx`
- `LoomX/Resources/Strings.zh-TW.resx`
- `LoomX.Tests/Assistant/UserDecisionBrokerTests.cs`
- `LoomX.Tests/Assistant/UserDecisionBrokerTestExtensions.cs`
- `LoomX.Tests/Assistant/AssistantViewModelTests.cs`
- `LoomX.Tests/Assistant/AssistantServiceTests.cs`
- `LoomX.Tests/Views/AssistantDecisionLifecycleTests.cs`
- `LoomX.Tests/Views/AskUserDialogContractTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-7-report.md`

## 剩余风险

- Task 8 的真实桌面视觉验收与发布打包不在本轮范围；透明主题颜色仍需后续 GUI 验证。
- `MainWindowViewModel.cs` 与 `AssistantView.axaml.cs` 的全文件 formatter 基线问题仍存在，但本轮没有新增对应的行为失败或 diff whitespace 问题。
- 仓库对象库仍存在与当前 HEAD 无关的 `bad tree object e42a7d13307188ed6a5459b2a5c6ce4d1d47930d` geometric repack 错误；开始时 `git pull --ff-only` 同时确认分支 Already up to date。提交推送后将按要求核对本地 commit、远端 `ls-remote` 与工作区状态，不修复或重写共享 `.git` 元数据。
