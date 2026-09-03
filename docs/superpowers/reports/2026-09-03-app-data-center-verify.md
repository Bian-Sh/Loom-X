---
change: app-data-center
design-doc: docs/superpowers/specs/2026-09-03-app-data-center-design.md
---

# AppDataStore 数据中心验证报告

## 总结

| 维度 | 结果 |
| --- | --- |
| 完整性 | 5/5 任务完成；无未勾选任务 |
| 正确性 | 配置快照、写穿刷新、活动窗口、复合游标、待合并、导航复用和活动 View 契约均有实现与测试证据 |
| 一致性 | 实现遵循设计文档的长期数据中心、异步初始化、单锁快照替换和 UI 线程协调决策 |

## 验证证据

- `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj --no-restore`：141/141 通过。
- `dotnet build OllamaHub.slnx --no-restore`：构建成功，0 个错误。
- `node comet-guard.mjs app-data-center build --apply`：build 守卫通过并推进至 verify；Comet 自动构建探测不识别 `.slnx`，已用 `COMET_SKIP_BUILD=1` 跳过重复探测，构建证据以上述显式命令为准。
- 配置写入挂起回归：`SuccessfulConfigurationWriteReplacesSnapshotAndPublishesChange` 通过；修复了刷新锁未释放问题。
- 活动行为测试覆盖 500 条淘汰、历史待合并、筛选不匹配、实时/分页去重和 `(CreatedAt, Id)` 游标。
- 源码契约测试覆盖长期 ViewModel 导航复用、活动尾部加载、加载指示器、待合并回到最新和滚动事件绑定。

## 设计对应关系

- `AppDataStore` 集中持有配置快照、Gateway/Provider 展示快照和活动窗口，并由 `App` 持有其进程级生命周期。
- `MainWindowViewModel` 预创建并复用 Overview、Provider、Gateway、Activity、Console、Settings ViewModel；导航只替换 `CurrentView` 引用。
- 配置写操作仍以数据库为事实源，成功后完整刷新快照；失败时保留原快照。
- 活动查询使用 `(CreatedAt, Id)` 复合游标；历史模式缓冲新活动，回到最新时按当前筛选合并。
- 外部配置变更和页面事件均通过 Avalonia UI 线程更新集合与外观，活动 View 卸载不会释放长期 ViewModel。

## 已知非阻塞项

- 当前环境没有 `requesting-code-review` 和 `verification-before-completion` 技能文件，自动代码审查/完成前技能无法调用；已在 `openspec/changes/app-data-center/tasks.md` 记录审查跳过原因，并完成人工差异审查。
- 构建保留既有 `SQLitePCLRaw.lib.e_sqlite3` 安全公告、`AnthropicRequestFactoryTests` 空引用警告及 `AnthropicResponseMapper` 的 CA2024 警告；本 change 未引入这些问题。
- 未执行真实桌面窗口的手动视觉回归；Avalonia View 契约测试和解决方案构建通过。

## 结论

无 CRITICAL 问题。实现、测试和设计目标已完成，待用户决定分支处理方式后进入最终 verify 守卫并可归档。
