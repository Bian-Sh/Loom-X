## Context

主窗口侧栏底部目前由三行静态文本组成，版本标签未绑定实际版本，状态圆点始终使用成功色。`GatewayProcessService` 已提供状态枚举和 `StateChanged` 事件，`AppVersion` 已提供统一版本标签，可直接复用而无需新增状态源。

## Goals / Non-Goals

**Goals:**

- 让侧栏底部只保留实际版本号和状态圆点。
- 让圆点及提示随网关状态实时更新，并在语言切换后刷新本地化状态文本。
- 保持实现集中在现有主窗口与主窗口 ViewModel 中。

**Non-Goals:**

- 不改变网关启停、健康检查或错误处理行为。
- 不新增点击操作、状态面板、动画或新的颜色资源。
- 不调整设置页和概览页已有版本/状态展示。

## Decisions

- `MainWindowViewModel` 直接暴露 `AppVersion.Label`、状态提示和四个互斥状态布尔值。相比引入值转换器或新的状态模型，这能复用现有绑定方式，并保持改动范围最小。
- XAML 使用四个重叠圆点，通过互斥可见性选择现有 `SuccessBrush`、`WarningBrush`、`DangerBrush` 和 `TextTertiaryBrush`。这样无需从 ViewModel 暴露 Avalonia 画刷，也不新增视觉 token。
- 主 ViewModel 订阅 `GatewayProcessService.StateChanged`，统一触发状态提示及布尔属性通知；释放时解除订阅。状态提示复用现有 `overview.gateway.status.*` 本地化资源，失败提示继续包含服务提供的安全错误摘要。
- 在现有主窗口契约测试中锁定紧凑布局和状态绑定，在 ViewModel 契约测试中锁定事件订阅与释放，避免为私有状态 setter 引入测试专用生产接口。

## Risks / Trade-offs

- [Risk] 颜色不是唯一的状态信息 → 通过圆点 ToolTip 和自动化名称提供本地化状态文本。
- [Risk] 多个重叠圆点的可见性配置错误会同时显示 → 使用互斥状态属性并通过源码契约测试锁定四种绑定。