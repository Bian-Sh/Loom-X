# 修复方案

在 `MainWindow.ApplyAppearance` 中固定使用 Acrylic 的窗口材质和背景 Alpha 计算。页面内部的玻璃层继续使用现有磨砂程度计算，避免改变内容层级。

将材质优先级固定为 `AcrylicBlur -> Transparent`。`TransparencyBackgroundFallback` 仍使用动态窗口背景画刷，透明关闭时仍生成不透明副本。旧版调用方传入的 Blur/Mica 值会被忽略，配置保存时归一为 Acrylic。

新增契约测试覆盖设置页不再出现算法控件、窗口只使用 Acrylic 回退，以及旧算法值不会改变运行时材质。
