# 修复方案

1. `OverviewView.axaml.cs` 删除 `DetachedFromVisualTree` 订阅和 `OnDetachedFromVisualTree` 处理程序。View 只负责自身可视树内的事件（数据上下文变化、附加到可视树后重适配图谱），不触碰 ViewModel 生命周期。
2. ViewModel 生命周期归属保持唯一：`MainWindowViewModel` 持有 `overviewViewModel` 字段，并在 `MainWindowViewModel.Dispose` 中调用 `overviewViewModel.Dispose()` 解除订阅。修复后 `Dispose` 不再会被提前调用，因此 `CultureChanged`、`ConfigurationChanged`、`StateChanged`、`TelemetryPublished` 四类订阅在概览页不可见期间依然有效。
3. `OverviewView.axaml` 删除网关面板中的手动刷新按钮，`Row=4` 仅保留 `GatewayActionLabel` / `ToggleGatewayCommand` 的启停按钮，并通过 `HorizontalAlignment="Left"` 保持原有内容尺寸对齐。
4. `OverviewViewModel` 删除 `public ICommand RefreshCommand` 属性与构造函数中的 `RefreshCommand = new AsyncCommand(RefreshAsync);` 赋值。`RefreshAsync` 保留为私有刷新入口，由构造函数、配置变更、网关状态变化、启停操作和遥测完成事件调用；`refreshInProgress` 重入保护不变。
5. 清理随按钮一起失效的 `overview.refresh.button` 词条：`Strings.resx`、`Strings.zh-TW.resx`、`Strings.ja-JP.resx`、`Strings.en-US.resx` 四个资源文件与 `scripts/gen_locale_resx.py` 的日文词典同步删除，避免生成脚本重新写出孤儿词条。
6. 回归测试：
   - `MainWindowNavigationContractTests.OverviewViewDoesNotDisposeTheLongLivedPageViewModel` 断言视图代码中不再出现 `Dispose()` 和 `DetachedFromVisualTree`，且 `MainWindowViewModel` 仍负责释放 `overviewViewModel`。
   - `MainWindowNavigationContractTests.OverviewRefreshesViaPushEventsInsteadOfManualTrigger` 按顶层类型切分 `OverviewViewModel`，断言四类订阅齐全、`CultureChanged` 在 `Dispose` 中对称解除，且不存在 `RefreshCommand`。
   - `OverviewGraphContractTests.OverviewLayoutUsesExpandedGraphAndSingleGatewayToggle` 由断言包含刷新按钮改为断言不再包含 `overview.refresh.button` 与 `RefreshCommand`。

不修改资源键集合语义、数据库结构、网关服务接口或既有本地化刷新机制；`OverviewCultureRefreshIsMarshaledToUiThread` 所依赖的 UI 线程派发逻辑保持不变。
