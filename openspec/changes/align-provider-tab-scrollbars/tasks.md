## 1. 契约测试

- [x] 1.1 在 `ProvidersViewContractTests` 中增加四个详情 Tab 共用滚动条定位样式的失败测试，并确认测试因样式尚未实现而失败。

## 2. 页面实现

- [x] 2.1 调整 `ProvidersView.axaml` 的详情容器右侧布局，为四个外层 `ScrollViewer` 应用统一 class。

## 3. 截图校准

- [x] 3.1 根据用户截图确认额外 8px Margin 是滚动条偏左的根因。
- [x] 3.2 先更新契约测试并确认 RED，再将共享样式改为 `Margin="0"` 并确认 GREEN。

## 4. 验证与交付

- [x] 4.1 运行 Provider 页面测试、完整测试、Release 构建和 OpenSpec 严格校验。
- [x] 4.2 重新发布到带可读时间戳的 `outputs` 目录并验证桌面端界面。
