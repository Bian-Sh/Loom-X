# 活动记录 SQLite 排序兼容设计

## 问题

SQLite 提供程序不支持将 `DateTimeOffset` 表达式直接用于 `ORDER BY`。活动页查询和后台活动存储都按 `CreatedAt` 排序，因此活动加载会抛出 `NotSupportedException`。

## 目标

- 活动查询不再对 `DateTimeOffset` 执行数据库端排序。
- 活动页仍按真实创建时间倒序展示；同一时间按记录 `Id` 倒序稳定排序。
- 后台保留最近写入的 50,000 条记录，避免依赖不受支持的时间排序。
- 通过 SQLite 实际查询回归测试覆盖该异常。

## 方案

过滤条件继续在 SQLite 中执行。数据库端改为按自增 `Id` 倒序，并在数据库端执行数量限制；结果 materialize 后在客户端按 `CreatedAt` 倒序、`Id` 倒序排序。`Id` 与写入顺序一致，可保证限制操作高效；客户端二次排序保留外部输入时间可能乱序时的展示语义。

涉及位置：

- `OllamaHub.Desktop/Services/ActivityQueryService.cs`：桌面活动查询。
- `OllamaHub/Activity/ActivityStore.cs`：服务端查询及溢出清理。
- `OllamaHub.Tests`：新增 SQLite 查询回归测试，验证查询成功、时间排序和限制行为。

## 错误处理与日志

沿用现有异常传播和 `ILogger` 记录方式，不记录活动正文、用户 prompt 或敏感请求头。排序修复不改变现有日志边界。

## 验证

运行 `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj`，并确认新增测试在 SQLite 提供程序下通过。
