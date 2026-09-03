# Provider 模型同步动画与 Toast 设计

## 目标

优化 Provider 配置页“模型”Tab 的同步按钮：图标略微缩小；点击后顺时针旋转；同步请求结束后停止旋转；同步成功或失败都通过全局 `ToastService` 给出即时反馈。

## 方案

- `ProvidersViewModel` 增加 `IsModelSyncing` 和 `SyncIconAngle` 状态。
- 同步开始时启动 `DispatcherTimer`，以固定步长递增 `SyncIconAngle`，保证 Avalonia 绑定在 UI 线程更新；同步结束时停止定时器并将角度复位为 0。
- XAML 将同步图标设为 12x12 的 `Path`，使用现有 `icon-glyph` 描边样式，并绑定 `RotateTransform.Angle`。
- 保留再次点击时取消旧请求并开始新请求的现有行为。只有当前请求的完成路径可以停止动画；被新请求替换的旧取消不弹 Toast。

## Toast 规则

- 成功：提示发现数量和新增数量，使用 `ToastLevel.Success`。
- HTTP 非成功、响应无模型、响应格式错误或未处理异常：提示安全摘要，使用 `ToastLevel.Error`。
- Provider 未保存或 URL 无效：在请求开始前提示配置警告，使用 `ToastLevel.Warning`。
- 不在 Toast 中包含 API Key、请求正文、响应正文或异常详细内容。

## 验证

- 增加 ViewModel/XAML 契约测试，覆盖尺寸、旋转绑定和同步结果 Toast 入口。
- 构建桌面项目并运行相关测试，确认 XAML 和异步同步逻辑编译通过。
