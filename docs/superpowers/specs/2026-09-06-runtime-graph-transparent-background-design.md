# Runtime NodeGraph 透明背景调整

## 目标

让 Overview 中的 Runtime NodeGraph 融入 LoomX 现有浅色玻璃界面，去掉纯黑色画布背景，改用低透明度画布层，同时保留拓扑边界、节点层级和交互可读性。

## 方案

- 左上操作说明只保留“滚轮缩放 · 拖动平移”，右下“适应画布”按钮继续保留。
- 将节点性质水印的最小显示缩放从 `1.0` 调整为 `0.55`，缩放到较小级别后再隐藏。
- Overview 外层容器保持透明，`RuntimeGraphControl` 使用低透明度 `GraphCanvasBrush` 覆盖整个可用画布，保证空白区域也能接收平移和滚轮事件。
- Graph 绘制改用 LoomX 现有的文字、次要文字、边框和 Accent 资源，避免透明背景下白色文字失去对比度。
- 不新增图形依赖，不改变 Router 数据、布局和交互模型。

## 验证

- 更新 Overview 和 NodeGraph 契约测试。
- 运行完整 `LoomX.Tests`。
- 发布 Release 包并通过 UIA/截图检查透明背景、文字可读性和水印显示阈值。
