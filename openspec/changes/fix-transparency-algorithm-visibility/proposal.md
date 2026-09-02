# 磨砂算法视觉差异修复

## 问题

设置页切换 Acrylic、Blur、Mica 时，部分算法在 Windows 上显示效果相同或不明显。当前 Blur 的回退链会优先落到 AcrylicBlur，导致 Blur 与 Acrylic 无法区分；Mica 自带较厚的系统底色，又被窗口背景画刷再次覆盖，透明感不明显。

## 根因

`MainWindow.BuildTransparencyLevels` 为 Blur 和 Mica 包含其他模糊材质作为回退。Avalonia 在当前平台上不支持 Gaussian Blur 时，Blur 请求实际使用 AcrylicBlur。窗口根背景继续使用相同的高不透明度遮罩，使 Mica 的系统材质被压低。

## 修复目标

- 所选算法只回退到 `Transparent`，避免不同算法静默合并为同一个原生材质。
- 为 Mica 和 Blur 使用更合适的窗口底层遮罩强度，让切换结果可见。
- 保留透明开关、透明度和磨砂程度的现有绑定、保存协议与跨平台回退行为。
