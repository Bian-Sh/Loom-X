## Why

桌面端当前会把两类不影响正常运行的诊断事件展示到用户可见控制台：Windows Shell 子进程启动失败后的正常回退，以及透明外观每次应用时的开始/完成明细。这些日志会制造误报并淹没有意义的业务信息。

## What Changes

- 将 Windows Shell 自启动子进程失败但继续当前进程的日志从 `Warning` 调整为 `Debug`。
- 将透明外观应用开始和完成日志从 `Information` 调整为 `Debug`。
- 增加回归测试，确保上述消息不再以用户可见的 `Information` 或 `Warning` 级别写入控制台。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

无。本次仅调整实现层诊断级别，不改变产品功能或既有规格。

## Impact

影响 `LoomX/App.axaml.cs`、`LoomX/MainWindow.axaml.cs` 及对应测试；不涉及 API、数据库、配置结构或依赖变更。
