# 单实例重复启动激活验证报告

## 结果

通过。LoomX 重复启动时，第二实例会通过用户会话内命名管道向首实例发送激活请求；首实例恢复最小化窗口、执行 Avalonia 激活和 Windows 原生前置，第二实例随后退出。

## OpenSpec 一致性

| 维度 | 状态 |
|---|---|
| 完整性 | PASS，3/3 任务完成，无 delta spec |
| 正确性 | PASS，proposal 的 4 项目标均有实现和验收证据 |
| 一致性 | PASS，实现符合 change design 与关联 Design Doc |

- `InstanceActivationClient` 使用当前 Windows Session ID 隔离管道，固定发送 `activate`，并以短超时有限重试。
- `InstanceActivationServer` 随首实例生命周期监听，只接受固定命令，通过 UI dispatcher 激活主窗口。
- `MainWindow` 恢复最小化状态，并组合 Avalonia `Activate()`、Windows 前台授权、原生前置和任务栏闪烁降级。
- 未发现 CRITICAL、WARNING 或 SUGGESTION 问题；可以进入归档阶段。

## 自动验证

- `InstanceActivationTests`：3/3 通过，覆盖短超时重试、固定激活命令校验和通信失败降级。
- `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore`：564/564 通过。
- `dotnet build LoomX/LoomX.csproj --no-restore`：0 错误。
- `scripts/publish-desktop.ps1 -Configuration Release`：发布成功，最终产物为 `outputs/20260912-173754/`。

## 发布包验收

使用 `outputs/20260912-173754/LoomX.exe` 启动首实例，将窗口最小化后再次运行同一入口：

- 首实例保持存活，发布包进程数保持为 1。
- 首窗口从最小化状态恢复。
- 日志记录第二实例“激活请求已发送”、首实例“收到重复启动激活请求”以及 `NativeActivated=True`。
- 验收结束后已正常关闭测试实例，没有终止其他应用进程。

## 已知警告

- 构建保留仓库既有的 `SQLitePCLRaw.lib.e_sqlite3` 漏洞告警、`SettingsViewModel` 可空性告警和 `AnthropicResponseMapper` 分析器告警，本次未新增这些问题。
- 当前环境没有 `openspec` CLI，也没有可加载的 `systematic-debugging` 与 `verification-before-completion` 技能文件；已使用 Comet 状态脚本、最小失败测试、完整测试、构建和发布包双启动验收完成等价验证。
