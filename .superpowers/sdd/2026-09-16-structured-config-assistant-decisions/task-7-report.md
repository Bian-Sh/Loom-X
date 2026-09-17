# Task 7 实现报告

日期：2026-09-17

## 实现范围

- 新增 `AskUserDialogViewModel`，将 `single_select`、`multi_select`、`number`、`text` 投影为可绑定字段，并保留请求默认值。
- `TryBuildResult(out IReadOnlyDictionary<string, object?> values)` 复用 `UserDecisionValidator` 完成 required、多选 min/max、数字 min/max/step、文本 max length 与敏感内容校验；失败只输出固定安全摘要或既有安全校验消息，不回显原值、问题正文或敏感字段。
- 新增专用 `AskUserDialog`，沿用透明窗口、动态资源、圆角、边框与 `WindowAppearanceCoordinator`；字段区可滚动，并通过四种 `DataTemplate` 渲染控件。
- `AssistantViewModel` 增加 Broker 激活/停用/释放生命周期、UI 线程调度、request id 所有权检查、并发/重复请求收敛、Dialog 提交/关闭处理和固定安全 Toast。
- 未修改 Task 6 生产契约、AgentLoop/审批语义、数据库路径、OpenSpec tasks、Comet 状态或发布目录；未生成 `outputs/`。

## TDD 证据

### RED

先创建/调整测试，再执行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests"
```

首次有效 RED 退出码为 1，失败原因与新增能力直接对应：

- `AskUserDialogViewModel` 与四类字段 ViewModel 尚不存在（`CS0246`）。
- `AssistantViewModel` 尚无 `userDecisionBroker`、`uiDispatcher`、`showAskUserDialog` 构造参数（`CS1739`）。
- `AssistantViewModel` 尚未实现 `IDisposable`，也没有 `Activate` / `Deactivate`（`CS1674`、`CS1061`）。

### GREEN

最小实现后，同一命令通过：25/25。

完整回归首次运行发现新增 XAML 的“取消/提交”硬编码触发既有 `LocalizationNoCjkTest`；已改为复用 `assistant.cancel` 与 `assistant.approval.approve` 动态本地化资源。修复后完整回归通过。

## 最终验证

- `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests"`
  - 通过：25，失败：0。
- `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~WindowAppearanceCoordinatorTests"`
  - 通过：31，失败：0。
- `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~UserDecision|FullyQualifiedName~AssistantToolsTests|FullyQualifiedName~AssistantServiceTests"`
  - 通过：88，失败：0。
- `dotnet test LoomX.slnx --no-restore`
  - 通过：861，失败：0，跳过：0。
- `dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/ViewModels/AskUserDialogViewModel.cs LoomX/ViewModels/AssistantViewModel.cs LoomX/Views/AskUserDialog.axaml.cs LoomX.Tests/Assistant/AssistantViewModelTests.cs LoomX.Tests/Views/AskUserDialogContractTests.cs`
  - 通过；仅输出既有工作区加载警告。
- `git diff --check`
  - 通过。

测试过程中仍可见仓库既有警告：`SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903、`SettingsViewModel` 的 CS8618、`AnthropicResponseMapper` 的 CA2024、测试代码的 CS8602；本任务未扩大范围处理。

## 改动文件

- `LoomX/ViewModels/AskUserDialogViewModel.cs`
- `LoomX/Views/AskUserDialog.axaml`
- `LoomX/Views/AskUserDialog.axaml.cs`
- `LoomX/ViewModels/AssistantViewModel.cs`
- `LoomX.Tests/Assistant/AssistantViewModelTests.cs`
- `LoomX.Tests/Views/AskUserDialogContractTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-7-report.md`

## 剩余风险

- 本任务按约束未执行 Task 8 的发布打包与真实桌面人工视觉验收；透明主题颜色仍以既有动态资源和外观协调器为准。
- 仓库对象库存在一个与当前 HEAD 无关的缺失 dangling tree（`e42a7d...`），导致开始时 `git pull --ff-only` 的 geometric repack 报错，但远端分支 SHA 与基线 HEAD 均为 `27dd97e...`，当前分支读取、测试、提交路径未受影响。推送后将再次核对远端 SHA。
