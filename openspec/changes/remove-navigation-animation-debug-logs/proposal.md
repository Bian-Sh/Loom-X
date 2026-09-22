## Why

左侧导航每次切换都会在用户可见的控制台中连续输出请求、开始和完成三条动画诊断日志。这些临时调试信息频率高且不承载业务状态，已无继续展示的价值。

## What Changes

- 删除左侧导航选中框切换请求、动画开始和动画完成三条 `Information` 日志。
- 保留导航选中状态、动画调度和其他窗口级业务日志。
- 增加源码契约检查，防止这些临时调试日志被重新引入。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

无。本次仅清理实现层诊断输出，不改变产品行为或现有规格要求。

## Impact

影响 `LoomX/MainWindow.axaml.cs` 的导航动画日志和对应导航契约测试；不涉及 API、数据库、依赖或配置变更。
