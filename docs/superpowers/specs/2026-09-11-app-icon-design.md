# Loom-X 应用图标替换设计

## 目标

将用户提供的品牌图片应用到 Loom-X 的 Windows 桌面/exe 图标和主窗口左上角品牌图标，两个位置保持同一视觉源文件。

## 方案

- 使用用户提供的 PNG 作为唯一品牌源，按 alpha 可见边界裁掉过大的透明画布，并补少量透明安全边距后输出为正方形 `LoomX/Assets/icon-transparent.png`，保留圆角方形底和原始构图比例。
- 从同一裁剪后的源图生成包含 16、24、32、48、64、128、256 像素图像的 `LoomX/Assets/app.ico`，供 Windows 桌面快捷方式、任务栏和 exe 文件属性使用。
- 保持 `LoomX.csproj` 与 `MainWindow.axaml` 现有资源引用不变，避免引入新的运行时路径或 UI 行为。

## 验证

- 检查两个资源的内容与用户提供图片一致，并验证 ICO 包含多尺寸图像。
- 执行 `dotnet build LoomX.slnx --no-restore`，确认资源能够正常编译打包。

## 后续内部图标试用

应用内左上角图标可以单独替换 `icon-transparent.png`，不改变桌面/exe 图标 `app.ico`。内部图标沿用可见 alpha 边界裁剪规则，避免透明画布导致显示尺寸缩小。
