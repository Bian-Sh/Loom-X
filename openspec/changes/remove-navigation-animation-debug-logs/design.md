## Context

`MainWindow` 的导航选中框动画由 `NavigationViewModel_OnPropertyChanged`、`AnimateNavigationSelection` 和计时器回调共同驱动。当前三个动画边界分别写入 `Information` 日志，控制台因此被高频的临时诊断信息占用；同一类日志没有被其他模块消费。

## Goals / Non-Goals

**Goals:**

- 删除导航选中框动画的三条临时 `Information` 日志。
- 保持导航事件订阅、Dispatcher 调度、动画计时和位置更新逻辑不变。
- 用现有导航契约测试锁定日志不再出现。

**Non-Goals:**

- 不调整窗口激活、透明外观或其他业务日志。
- 不改变日志基础设施、日志级别策略或控制台页面结构。

## Decisions

- 直接移除三处 `logger.LogInformation` 调用。它们没有副作用，也没有调用方依赖返回值，因此这是最小且可审查的实现；改为 `Debug` 仍会继续进入诊断管道，不符合“无需展示”的目标。
- 在现有 `MainWindowNavigationContractTests` 的动画合同测试中增加负向源码断言。该测试已读取同一 code-behind 并校验动画结构，无需新增测试夹具或依赖。

## Risks / Trade-offs

- [Risk] 后续排查动画问题时缺少逐次切换日志 → Mitigation：保留动画代码的可测试合同，以及窗口激活、外观应用等有意义的日志；需要临时诊断时再按事件边界局部启用。
