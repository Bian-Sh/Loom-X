# 全仓库测试停滞安全摘要

- 命令：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore`，默认输出，属于指定 worktree。
- 全量运行长期未完成。仅终止经 CommandLine 和父子进程核对的本次 testhost（PID 10436）；未终止其它 dotnet、用户服务或应用。父运行退出 1，报告中止前 409 项通过，不能视为全量通过。
- dotnet_debug_mcp 服务未配置，没有手工启动；改用现有 dotnet-dump 收集托管栈。dump 与原始全栈存于 ignored `.comet/evidence/`，可能有内存敏感内容，禁止提交。
- 阻塞栈：`ReleaseNotesContentViewModel.RunOnUiThread` 第 172 行同步等待 Dispatcher → 构造第 49 行 → `ReleaseHistoryViewModel` 构造第 93 行 → xUnit AsyncTestSyncContext。
- 上述两个生产文件在本次 diff 中均无修改。调用点注入的 dispatch 仅作用于历史 ViewModel，Content 构造仍使用全局 Dispatcher；全量集成上下文下等待未完成。这是观察到的阻塞位置，不宣称已彻底确定所有 Dispatcher 初始化与跨测试交互原因。
- 对照：只运行 `ReleaseHistoryViewModelTests`，加 35s blame-hang 超时且不生成dump，14/14 通过；因此不能说该类单独稳定复现或把问题归为每次必然死锁。
- 本次不扩大范围修改更新历史/UI调度。完整相关插件和 ProviderExecutionPipeline 测试直接运行 689/689，包含所有链接夹具与最新流式/保护失败测试。