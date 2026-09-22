# Runtime NodeGraph 操作区布局设计

## 目标

优化 Overview 中 Runtime NodeGraph 的操作区域，降低画布顶部视觉噪声，并保证鼠标落在节点上时仍可缩放和移动视野。

## 交互与布局

- 左上角只保留一行透明的操作说明：滚轮缩放、按住拖动平移、Fit 重置视野。
- 右上角移除 GraphStatus、缩放按钮和原 Fit 按钮，不绘制额外背景。
- Endpoint 选择器移动到画布底部居中，使用透明背景、无边框、下划线文字的 hyperlink 样式。
- Fit 按钮移动到画布右下角，继续调用当前活动 Graph 的 `FitToView`。
- 操作说明层不参与命中测试；只有 Endpoint 链接和 Fit 按钮参与命中测试，其余画布区域由 `RuntimeGraphControl` 接收输入。

## 平移与缩放契约

- 滚轮在空白区域和任意节点区域都缩放，并以光标位置作为缩放锚点。
- 中键拖动直接平移。
- 左键在节点上短按仍然选择节点；左键拖动超过 4px 后转为平移，避免节点阻断视野移动。
- 左键在空白区域拖动继续平移，短按清除选择。

## 范围与验证

本次只修改 Overview 的 XAML 布局、Graph 控件的指针手势和相关测试，不改变 Router 数据、Graph Projection、Layout 或 Telemetry。验证包括 NodeGraph 定向测试、完整测试以及发布实例的 Fit/放大/节点悬停滚轮截图检查；验证实例完成后关闭。
