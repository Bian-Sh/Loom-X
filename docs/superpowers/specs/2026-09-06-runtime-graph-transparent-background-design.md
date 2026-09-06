# Runtime NodeGraph 透明背景调整

## 目标

让 Overview 中的 Runtime NodeGraph 融入 LoomX 现有浅色玻璃界面，去掉纯黑色画布背景，让画布空白区使用共享 Surface 层级，同时保留拓扑边界、节点层级和交互可读性。

## 方案

- 左上操作说明只保留“滚轮缩放 · 拖动平移”，右下“适应画布”按钮继续保留。
- 将节点性质水印的最小显示缩放调整为 `0.45`，使用共享次级文字颜色、`10–15` 的字号范围和半粗体显示；水印固定放在 NodeName 下方的独立底部空间，避免覆盖标题，缩放到更小级别后再隐藏。
- Overview 外层容器保持透明并使用共享 `BorderBrush`；`RuntimeGraphControl` 使用共享 `SurfaceSubtleBrush` 覆盖整个可用画布，保证空白区域也能接收平移和滚轮事件，同时与页面下方的 Surface 卡片保持同一视觉层级。
- Graph 图元使用 LoomX 现有的 `SurfaceBrush`、`SurfaceSubtleBrush`、`SurfaceMutedBrush`、`AccentSoftBrush`、文字和边框资源，不再对半透明边框资源二次降低 Alpha，保证 Surface 画布上节点、Provider 分组和文字可辨识。
- 适应画布按钮与节点选中态统一使用共享 Surface、Border 和 Accent 资源，不保留独立深色 Graph 视觉。
- 不新增图形依赖，不改变 Router 数据、布局和交互模型。

## 验证

- 更新 Overview 和 NodeGraph 契约测试。
- 运行完整 `LoomX.Tests`。
- 发布 Release 包并通过 UIA/截图检查 Surface 画布、节点和文字可读性、Provider Header 圆角、水印显示阈值，以及缩放和平移交互。
