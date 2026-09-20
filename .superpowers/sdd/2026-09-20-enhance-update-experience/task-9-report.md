# Task 9 实施报告：设置页版本历史、本地化与日志安全

- 日期：2026-09-21
- Task base：`09cd3e7e14f8cd7472bc12882acd7e6bfa99efad`
- 分支：`codex/merge-structured-config-assistant-decisions`
- 实现提交：`5817dbfbe961fe552a992496daace8374748e58c`
- 提交消息：`完善设置页版本历史与更新本地化`

## 状态

- `SettingsViewModel` 已增加 `SelectedTabIndex` 与 `ReleaseHistory`；更新 Tab（索引 1）只在首次进入时触发 `EnsureLoadedAsync`。
- `MainWindowViewModel` 将 Task 5 创建的同一个 `ReleaseHistoryViewModel` 注入设置页；组合根保持唯一释放，设置页不会重复释放注入实例。
- 设置页更新 Tab 保留当前版本、自动检查、更新代理和手动检查，并增加约 430px 的版本历史左右分栏。
- 已覆盖首次加载、空态、无缓存错误、有缓存错误、正常态、加载更多和刷新状态。
- 更新入口、浮窗、进度、Release Notes 空态和设置页历史相关资源已补齐 neutral、en-US、zh-TW、ja-JP。
- `UpdateCoordinator`、`ReleaseHistoryViewModel`、`ReleaseNotesContentViewModel` 的动态用户文案均由资源即时解析或在文化切换时重新通知，不永久缓存已格式化状态、日期或错误摘要。
- 更新服务、协调器和历史日志测试覆盖 Release Body、测试 API Key、代理密码和异常响应正文；日志异常对象使用脱敏异常，业务日志只保留版本、页码、条数、阶段、字节数和耗时等安全摘要。
- OpenSpec 2.3、5.3、5.4 已勾选。

## 改动文件

### 生产代码与视图

- `LoomX/ViewModels/SettingsViewModel.cs`
- `LoomX/ViewModels/MainWindowViewModel.cs`
- `LoomX/ViewModels/UpdateCoordinator.cs`
- `LoomX/ViewModels/ReleaseHistoryViewModel.cs`
- `LoomX/ViewModels/ReleaseNotesContentViewModel.cs`
- `LoomX/Services/UpdateService.cs`
- `LoomX/Views/SettingsView.axaml`
- `LoomX/MainWindow.axaml`

### 本地化资源

- `LoomX/Resources/Strings.resx`
- `LoomX/Resources/Strings.en-US.resx`
- `LoomX/Resources/Strings.zh-TW.resx`
- `LoomX/Resources/Strings.ja-JP.resx`

### 测试

- `LoomX.Tests/Views/SettingsViewContractTests.cs`
- `LoomX.Tests/Views/UpdateExperienceContractTests.cs`
- `LoomX.Tests/ViewModels/SettingsViewModelTabLifecycleTests.cs`
- `LoomX.Tests/ViewModels/UpdateCoordinatorTests.cs`
- `LoomX.Tests/ViewModels/ReleaseHistoryViewModelTests.cs`
- `LoomX.Tests/ViewModels/ReleaseNotesContentViewModelTests.cs`
- `LoomX.Tests/UpdateServiceTests.cs`
- `LoomX.Tests/Logging/RecordingLogger.cs`
- `LoomX.Tests/LocalizationNoCjkTest.cs`
- `LoomX.Tests/LocalizationResourceParityTest.cs`

### 流程状态

- `openspec/changes/enhance-update-experience/tasks.md`

## TDD

### RED

先添加设置页契约、Tab 生命周期、资源键/无 CJK 和敏感日志测试，再运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~LocalizationNoCjkTest|FullyQualifiedName~LocalizationResourceParityTest"
```

结果：失败 3、通过 18、总计 21。预期失败点：

- `TabControl` 尚未双向绑定 `SelectedTabIndex`，历史分栏不存在。
- 四套资源缺少全部更新体验新键。
- 三个更新 ViewModel 仍包含用户可见中文回退字面量。

Tab 生命周期独立红灯：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SettingsViewModelTabLifecycleTests"
```

结果：失败 1、通过 0；`SettingsViewModel` 构造函数尚未接收 `ReleaseHistoryViewModel`。

敏感日志独立红灯：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Release元数据与代理凭据不会进入更新服务日志|FullyQualifiedName~Release正文与凭据不会进入历史日志|FullyQualifiedName~Release正文与异常响应不会进入协调器日志"
```

结果：失败 3、通过 0；`RecordingLogger` 纳入异常文本后，原始异常中的测试敏感正文会进入日志。

### GREEN

最小实现包括：

- 设置页第一次进入更新 Tab 时只调度一次历史加载。
- 组合根注入共享历史实例，并保留唯一 Dispose 所有权。
- 增加固定高度历史阅读区及全部状态覆盖。
- 补齐四套资源并替换 Task 8 暂留的浮窗硬编码文案。
- 将协调器状态、错误和历史错误改为按资源键即时计算；文化切换只发属性通知，不发网络请求。
- 日志仍通过 `ILogger<T>` 结构化记录，但传入脱敏异常对象，避免原始异常正文泄漏。

## 定向测试与联合回归

### brief 步骤 1

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~LocalizationNoCjkTest|FullyQualifiedName~LocalizationResourceParityTest"
```

- 失败：0
- 通过：21
- 跳过：0

### brief 更新联合回归

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~UpdateCoordinatorTests|FullyQualifiedName~ReleaseHistoryViewModelTests|FullyQualifiedName~UpdateServiceTests"
```

- 失败：0
- 通过：34
- 跳过：0

### 生命周期、组合根与共享 Release Notes 补充回归

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SettingsViewModelTabLifecycleTests|FullyQualifiedName~UpdateExperienceContractTests|FullyQualifiedName~ReleaseNotesContentViewModelTests|FullyQualifiedName~ReleaseNotesViewContractTests"
```

- 失败：0
- 通过：23
- 跳过：0

另一次只筛选生命周期与组合根契约的最终运行通过 8/8。

未运行完整测试套件，符合任务限制。

## 构建与警告

```powershell
dotnet build LoomX.slnx -c Release --no-restore
```

- 结果：成功
- 错误：0
- 警告：2
- 警告均为既有 `NU1903`：`SQLitePCLRaw.lib.e_sqlite3 2.1.11` 已知高严重性漏洞；本任务未修改依赖。

定向测试首次编译还显示既有 `CS8618`、`CA2024` 和测试 `CS8602` 警告；均与 Task 9 无关，未修改。

## 调试记录

曾并行启动两条 `dotnet test`，两个 MSBuild 进程同时写入 `LoomX/obj/Release/net10.0/Avalonia/resources`，其中一条因文件锁退出。根据错误路径确认根因是并行构建竞争，不是代码失败；随后所有 .NET 测试与构建均改为串行，原失败筛选串行通过 38/38。

## 自审

- 设置页原四项能力仍存在，历史区使用同一个共享 `ReleaseNotesView`。
- 无缓存错误覆盖阅读区；有缓存错误仅在 Header 显示非阻塞提示，旧列表与 Markdown 保持可读。
- `SelectedTabIndex` 只在第一次进入索引 1 时调度加载；失败后由页面重试按钮显式重试。
- `MainWindowViewModel` 释放注入历史实例；`SettingsViewModel` 只释放自行创建的回退实例。
- neutral、en-US、zh-TW 键完全对齐；ja-JP 新值非空；en-US、zh-TW 占位符顺序与 neutral 一致。
- `LocalizationNoCjkTest` 已纳入三个更新 ViewModel。
- 未引入 WebView 或第二 Markdown 引擎；`LiveMarkdown.Avalonia 1.12.2` 保持不变。
- `git diff --check` 通过；仅出现工作区既有 LF/CRLF 转换提示。
- 未删除、移动或清理任何其他 Session 产物。

## 剩余风险

- 本任务按要求未运行完整测试套件。
- 设置页视觉状态仅通过 AXAML 编译和契约测试验证；实际浅色/深色/透明主题 CUA 与发布包验证属于后续 Task 10。
- 当前分支相对 `origin/master` 为 ahead 24 / behind 1；任务基线固定为指定 SHA，因此未在本任务中拉取、合并或推送远端变更。
---

# Fix Round 1/5（2026-09-20）

## Reviewer findings 修复

1. 将 `UpdateService`、`UpdateCoordinator`、`ReleaseHistoryViewModel` 的伪装 `InvalidOperationException` 替换为语义明确的 `SafeUpdateDiagnosticException`：
   - 不保存原始 `Message`、`InnerException`、响应正文或凭据。
   - 保留原始异常类型全名、原始 `HResult`、白名单 `HttpRequestException.StatusCode`、固定更新阶段。
   - 安全异常自身继承原始 `HResult`，并仅复制不含异常消息的原始 `StackTrace`。
   - 三处日志继续使用 `ILogger<T>` 结构化模板，显式记录 `{ExceptionType}`、`{HResult}`、`{HttpStatusCode}`、`{Stage}`。
2. `ReleaseHistoryViewModel` 增加 `CanShowLoadMore => HasMore && !IsLoadingMore`，并在 `HasMore` / `IsLoadingMore` 变化时发出属性通知；加载更多按钮改绑该属性，与加载提示互斥。
3. `MainWindow.axaml` 标题栏系统关闭按钮恢复 `window.close`；更新浮窗关闭按钮继续使用 `update.dialog.close`，契约测试分别截取两个按钮并断言资源键不会串用。

## 改动文件

- `LoomX/Services/SafeUpdateDiagnosticException.cs`
- `LoomX/Services/UpdateService.cs`
- `LoomX/ViewModels/UpdateCoordinator.cs`
- `LoomX/ViewModels/ReleaseHistoryViewModel.cs`
- `LoomX/Views/SettingsView.axaml`
- `LoomX/MainWindow.axaml`
- `LoomX.Tests/Logging/RecordingLogger.cs`
- `LoomX.Tests/UpdateServiceTests.cs`
- `LoomX.Tests/ViewModels/UpdateCoordinatorTests.cs`
- `LoomX.Tests/ViewModels/ReleaseHistoryViewModelTests.cs`
- `LoomX.Tests/Views/SettingsViewContractTests.cs`
- `LoomX.Tests/Views/UpdateExperienceContractTests.cs`

## RED 证据

日志安全聚焦红灯：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Release元数据与代理凭据不会进入更新服务日志|FullyQualifiedName~Release正文与凭据不会进入历史日志|FullyQualifiedName~Release正文与异常响应不会进入协调器日志"
```

- 失败：3
- 通过：0
- 三项均因日志异常对象仍为 `InvalidOperationException` 失败，证明 reviewer finding 可复现。

设置页、关闭按钮和加载更多行为红灯：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~UpdateExperienceContractTests|FullyQualifiedName~加载更多期间隐藏加载按钮并在仍有后续页时恢复"
```

- 失败：3
- 通过：18
- 分别失败于缺少 `CanShowLoadMore`、标题栏仍使用 `update.dialog.close`、ViewModel 尚无加载更多互斥属性。

## GREEN 与回归

最终串行验证统一增加 `--blame-hang-timeout 60s`，结果如下：

1. 日志安全聚焦：通过 3、失败 0。
2. `SettingsViewContractTests`、`UpdateExperienceContractTests`、加载更多行为：通过 21、失败 0。
3. 设置页与本地化定向回归：通过 21、失败 0。
4. `UpdateCoordinatorTests`、`ReleaseHistoryViewModelTests`、`UpdateServiceTests` 联合回归：通过 35、失败 0。
5. Tab 生命周期、更新体验契约、Release Notes 回归：通过 23、失败 0。

未运行完整测试套件，符合任务限制。

## 构建与检查

```powershell
dotnet build LoomX.slnx -c Release --no-restore
git diff --check
```

- Release build：成功，0 错误、2 警告。
- 两项警告均为既有 `NU1903`：`SQLitePCLRaw.lib.e_sqlite3 2.1.11` 已知高严重性漏洞，本轮未修改依赖。
- 定向测试首次编译仍可见既有 `CS8618`、`CA2024`、`CS8602`，与本轮无关。
- `git diff --check`：通过；仅显示既有 LF/CRLF 转换提示。

## 自审

- 日志中的异常对象不再伪装为 `InvalidOperationException`，类型名与固定消息明确表示安全更新诊断异常。
- 安全异常没有 `InnerException`，不会通过 `Exception.ToString()` 泄漏原始消息；测试覆盖 Release Body、API Key、代理密码、Authorization 和响应正文均不出现。
- 原始异常类型、`HResult`、HTTP 状态码、阶段和安全堆栈仍可用于诊断；测试同时检查异常对象和结构化字段。
- 加载更多按钮与加载提示互斥，加载完成且 `HasMore` 仍为 `true` 时按钮恢复。
- 标题栏关闭按钮与更新浮窗关闭按钮的本地化资源键已精确区分。
- 未合并 `origin/master`，未进入 Task 10，未引入新日志框架或第二 Markdown 引擎，未修改无关中文或既有警告。

## 提交

- 提交消息：`修复更新日志诊断与设置页交互细节`
- 提交 SHA：`0514e16ec54fd145db6776543df041189cb85fa2`

## 剩余风险

- 按要求未运行完整测试套件。
- 本轮为逻辑、AXAML 契约和构建验证，未执行实际桌面端主题/透明效果视觉验收。
- 分支仍相对 `origin/master` behind 1；遵照要求未合并远端。
- 一次组合契约测试进程曾无输出停滞并被中断；拆分运行全部通过，最终组合运行在 hang timeout 保护下通过 21/21，未复现功能失败。
---

# Fix Round 2/5（2026-09-20）

## Reviewer finding

`SafeUpdateDiagnosticException` 原实现直接读取并复制 `source.StackTrace`。异常子类可以重写该属性并返回 Release Body、API Key、代理密码或响应正文，因此安全异常的 `StackTrace`、`ToString()` 以及 `RecordingLogger.Messages` 仍可能泄漏不可信文本。

## RED

新增 `重写堆栈和异常数据不会进入安全诊断日志`，使用自定义异常完成以下恶意输入：

- `Message` 包含敏感标记。
- `InnerException` 的消息包含敏感标记，同时携带 `HttpStatusCode.BadGateway`。
- `Data` 包含敏感标记。
- 重写 `StackTrace`，直接返回敏感标记。
- 使用自定义 `HResult`。

聚焦命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~重写堆栈和异常数据不会进入安全诊断日志"
```

RED 结果：失败 1、通过 0。失败点为 `SafeUpdateDiagnosticException.StackTrace` 包含 `stack-body-api-key-proxy-password-secret`，证明 reviewer finding 可复现。

## 最小修复

- 删除所有对不可信 `source.StackTrace` 的读取和 `SetRemoteStackTrace` 复制。
- 改用 `ExceptionDispatchInfo.SetCurrentStackTrace(this)`，只在安全异常构造现场重新捕获受信任调用栈。
- 继续只保留白名单诊断字段：原始异常类型名、原始 `HResult`、HTTP 状态码和固定阶段。
- 安全异常仍不复制原始 `Message`、`Data`、`InnerException` 或任意响应内容。

## GREEN 与回归

1. 恶意 StackTrace 聚焦测试：通过 1、失败 0。
2. 日志安全聚焦（含原三项和恶意 StackTrace）：通过 4、失败 0。
3. SettingsView、本地化资源回归：通过 21、失败 0。
4. `UpdateCoordinatorTests`、`ReleaseHistoryViewModelTests`、`UpdateServiceTests` 联合回归：通过 36、失败 0。
5. Tab 生命周期、UpdateExperience、Release Notes 回归：通过 23、失败 0。

未运行完整测试套件。

## 构建与检查

```powershell
dotnet build LoomX.slnx -c Release --no-restore
git diff --check
git diff --check -- LoomX/Services/SafeUpdateDiagnosticException.cs LoomX.Tests/UpdateServiceTests.cs
```

- Release build：成功，0 错误、2 警告。
- 警告仍为既有 `NU1903`：`SQLitePCLRaw.lib.e_sqlite3 2.1.11`。
- 全局与本轮 scoped `git diff --check` 均通过；全局仅显示 Comet 文件既有 LF/CRLF 转换提示。

## 自审

- 生产代码不再读取 `source.StackTrace`，恶意重写属性不会被访问或复制。
- `SafeUpdateDiagnosticException.StackTrace` 由当前受信任构造现场生成，仍包含 `UpdateService.CheckAsync` 等诊断调用路径，但不含原异常消息。
- 测试同时验证安全异常的 `StackTrace`、`ToString()`、`Data`、`InnerException` 和 `RecordingLogger` 捕获文本均不含敏感标记。
- 原始类型名、`HResult`、HTTP 状态码、阶段以及日志结构化字段保持一致。
- 未修改、添加、删除、移动或清理 Comet 激活生成的 `.gitignore`、`.comet/config.yaml`、`openspec/.../.comet.yaml` 和工具目录。

## 改动文件

- `LoomX/Services/SafeUpdateDiagnosticException.cs`
- `LoomX.Tests/UpdateServiceTests.cs`
- `.superpowers/sdd/2026-09-20-enhance-update-experience/task-9-report.md`

## 剩余风险

- 按要求未运行完整测试套件。
- 分支仍相对 `origin/master` behind 1；本轮未拉取或合并远端。