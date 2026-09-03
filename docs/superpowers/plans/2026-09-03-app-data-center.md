---
change: app-data-center
design-doc: docs/superpowers/specs/2026-09-03-app-data-center-design.md
base-ref: 0213e0cdc67816f70d4255415b8240102e2d86f5
---

# AppDataStore 数据中心实施计划

## 目标

将桌面进程内的配置、导航 ViewModel 和活动窗口统一收拢到长期存活的 `AppDataStore`，让普通页面切换只读内存状态；显式刷新、保存和活动分页仍通过现有服务访问 SQLite。

## 任务

1. 扩展 `ConfigSnapshotService`，提供可取消的异步配置快照加载、写入后重载入口，并保留现有数据库路径、文件监视和结构化日志语义。
2. 新增桌面 `AppDataStore`：单次初始化任务、原子配置快照、Provider/模型/Endpoint 展示快照、配置变更事件、活动事件订阅和有界活动窗口。
3. 将 `App` 和 `MainWindowViewModel` 改为创建并持有一个数据中心，预创建并复用概览、Provider、网关、活动、控制台和设置 ViewModel；初始化期间显示加载/失败状态。
4. 改造 Provider、Gateway、Overview、Settings ViewModel，使普通加载从数据中心读取，配置写入后调用数据中心重载，不在构造函数或导航路径直接查询数据库。
5. 重写活动查询游标和 `ActivityViewModel` 状态机：复合 `(CreatedAt, Id)` 游标、最多 500 条滑动窗口、历史追加、实时待合并提示、筛选重置、返回最新和结束/失败状态。
6. 更新活动 View 的尾部加载区、旋转状态、回到最新命令和 ScrollViewer 事件协调，移除视觉树卸载即释放长期订阅的行为。
7. 增加数据中心、导航复用、配置写穿、活动窗口/游标/去重和 View 契约测试，运行 `dotnet test`、桌面构建与必要的发布打包验证。

## 执行约束

- 所有新增代码注释和持久文档使用中文。
- 不修改控制台缓冲、清空、跟随滚动和日志实时显示逻辑。
- 不创建第二份设置数据库；配置数据库始终来自 `AppDataPaths.DatabasePath`。
- 失败修复遵循先最小复现、再根因定位、最后验证的调试门禁。
