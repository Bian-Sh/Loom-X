# 修复方案

在 `MainWindow.ApplyAppearance` 中，将窗口根背景的 Alpha 计算与材质算法关联：Acrylic 保持现有基线，Blur 使用较轻遮罩，Mica 使用更轻遮罩以露出系统材质纹理。页面内部的玻璃层继续使用现有磨砂程度计算，避免改变内容层级。

将材质优先级收敛为“所选材质 -> Transparent”三组独立列表。这样平台不支持 Blur 时不会再借用 AcrylicBlur，平台不支持 Mica 时也会明确退回真正透明的窗口。`TransparencyBackgroundFallback` 仍使用动态窗口背景画刷，透明关闭时仍生成不透明副本。

新增契约测试覆盖三组材质列表和算法遮罩因子，确保后续修改不会让不同算法再次落到同一回退材质。
