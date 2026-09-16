## Context

`App` 在通过 `explorer.exe` 启动 Shell 子进程失败时会继续执行当前进程，这是已有容错路径，但当前以 `Warning` 记录完整异常，用户会误以为应用启动失败。`MainWindow.ApplyAppearance` 在启动、配置刷新和外观预览时都会执行，每次固定写入两条 `Information` 日志，形成高频重复输出。

## Goals / Non-Goals

**Goals:**

- 让两类诊断消息不再进入默认最低级别为 `Information` 的运行时日志和控制台。
- 保留故障回退、透明外观应用和所有 UI 行为不变。
- 保留需要时可启用的开发诊断信息。

**Non-Goals:**

- 不修改日志基础设施或全局最低日志级别。
- 不移除其他启动、窗口激活或配置刷新日志。
- 不改变 Shell 启动策略和透明外观算法。

## Decisions

- 将目标日志降为 `Debug`，而不是删除。`LoggingBootstrap` 当前最低级别为 `Information`，因此默认不会进入控制台或日志文件，同时仍保留开发诊断语义。
- 对 `MainWindow.ApplyAppearance` 使用注入的记录型 logger 做行为测试，验证两条消息只以 `Debug` 级别发出。
- 对难以在单元测试中稳定触发的 Shell 启动失败分支使用现有启动源码契约测试，锁定该消息不再使用 `Warning`。

## Risks / Trade-offs

- [Risk] 默认日志不再记录 Shell 子进程启动失败的异常细节 → Mitigation：回退逻辑继续运行，消息和异常仍保留在 `Debug` 调用中，可在开发诊断配置下启用。
- [Risk] 误将所有外观日志清理掉 → Mitigation：测试只约束目标消息的级别，不修改外观应用逻辑和状态断言。
