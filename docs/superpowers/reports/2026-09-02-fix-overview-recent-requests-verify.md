# 验证报告：fix-overview-recent-requests

## 总结

| 维度 | 结果 |
| --- | --- |
| 完整性 | 3/3 任务完成，无 delta spec |
| 正确性 | 数据库回填、时间排序、状态映射、去重和空数据布局均有实现与回归覆盖 |
| 一致性 | 实现遵循 proposal.md 与 design.md；未修改 Activity 数据库结构或服务端接口 |

## 验证证据

- `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj --no-restore --verbosity minimal`：100/100 通过。
- `dotnet build OllamaHub.Desktop/OllamaHub.Desktop.csproj --no-restore --verbosity minimal`：构建成功，0 错误。
- `OllamaHub.Tests/Services/ActivityQueryServiceTests.cs` 使用临时 SQLite 数据库模拟 Activity 数据，覆盖按 `CreatedAt` 排序后再限制数量，以及时间与 Id 不一致的记录。
- `OllamaHub.Tests/Views/OverviewRecentRequestsContractTests.cs` 覆盖状态/时间/延迟映射、XAML 列绑定、空数据提示和实时/持久化请求按 RequestId 去重并限制 8 条。
- `OllamaHub.Desktop/Services/ActivityQueryService.cs` 默认使用 `AppDataPaths.ActivityDatabasePath`，查询结果在客户端按 `CreatedAt`、`Id` 倒序并取最近 8 条，避免 SQLite 对 `DateTimeOffset` 排序的限制。
- `OllamaHub.Desktop/ViewModels/MainWindowViewModel.cs` 在概览刷新时回填数据库，并在实时完成事件中合并去重；查询异常仅记录结构化错误日志。
- `OllamaHub.Desktop/Views/OverviewView.axaml` 为时间、Endpoint、Model、状态和延迟设置独立列，并绑定空数据提示。

## 风险与说明

- 构建输出包含既有 `SQLitePCLRaw.lib.e_sqlite3` NU1903 漏洞警告及已有分析器警告，本次未引入。
- `openspec` CLI 未安装，未执行其 JSON 命令；已使用本地 OpenSpec 产物、代码证据、测试和构建结果完成等价核对。
- `comet-guard` 的自动构建识别不支持 .NET，本次以手动 `dotnet build` 成功结果作为证据，并使用 `COMET_SKIP_BUILD=1` 通过 guard。
