# 任务

- [x] 移除 `OverviewView` 脱离可视树时对长生命周期 ViewModel 的 `Dispose` 调用，保证订阅不被提前解除。
- [x] 删除概览页手动刷新按钮、`OverviewViewModel.RefreshCommand` 及 `overview.refresh.button` 本地化词条（含日文生成词典）。
- [x] 增加契约回归测试，并运行 `dotnet build` 与 `dotnet test`（253 通过 / 0 失败）。
