# 修复概览页面板数据与文案不自动刷新

## 问题

概览页左下角网关面板中的本地化文案与数据不能及时刷新：切换界面语言后，面板标签已经是新语言，但网关状态、接口状态、最后检查和启动按钮仍是旧语言，且只有在面板上点击「刷新」按钮后才会更新。该按钮本身也没有必要保留。

## 根因

`OverviewViewModel` 由 `MainWindowViewModel` 构造一次并全生命周期复用，但 `OverviewView.OnDetachedFromVisualTree` 会在视图脱离可视树时调用 `(DataContext as OverviewViewModel)?.Dispose()`。

切换到其他页面（例如设置页改语言）时，主窗口的 `ContentControl Content="{Binding CurrentView}"` 会替换内容并让 `OverviewView` 脱离可视树，`Dispose` 随即解除 `LocaleService.CultureChanged`、`dataStore.ConfigurationChanged` 和 `gatewayService.StateChanged` 三类订阅。回到概览页时创建的是新的 View 实例并复用同一个 ViewModel，但该 ViewModel 已经不再监听任何事件，因此语言切换和配置变更都不会驱动它刷新；只有手动触发 `RefreshCommand` 走一遍 `RefreshAsync` 才会重新解析本地化文案。

## 修复目标

- View 不再拥有 ViewModel：删除 `OverviewView` 脱离可视树时的 `Dispose` 调用，概览页 ViewModel 生命周期由 `MainWindowViewModel` 统一管理。
- 语言切换、配置变更和网关状态变化在概览页不可见期间也能继续驱动面板刷新，回到页面即为最新数据。
- 移除面板上的手动「刷新」按钮，以及随之失效的 `OverviewViewModel.RefreshCommand` 公共属性和 `overview.refresh.button` 本地化词条。
- 增加契约回归测试，锁死「View 不释放长生命周期页面 ViewModel」与「概览页无手动刷新入口」两个约定。
