## 1. 契约测试

- [x] 1.1 增加四个详情 Tab 共用滚动条定位样式的契约测试。

## 2. 初始页面实现

- [x] 2.1 为四个外层 `ScrollViewer` 应用统一 class。

## 3. 第一轮截图校准

- [x] 3.1 根据截图发现原 8px 整体 Margin 使滚动条偏左。
- [x] 3.2 将整体 Margin 改为 0，并完成自动化与实机验证。

## 4. 第二轮需求纠正

- [x] 4.1 根据用户反馈确认不能移动 ScrollContent，只能移动 ScrollBar。
- [x] 4.2 先验证“保留内容 Margin、平移模板 ScrollBar”的方案，并识别其父级边界外渲染风险。
- [x] 4.3 根据用户的层级遮盖提示，改为让 ScrollViewer 保持全宽，并使用右侧 Padding 仅内缩 ScrollContent。
- [x] 4.4 更新契约测试：要求 `Margin="0"`、`Padding="0,0,8,0"`，并禁止模板 ScrollBar TranslateTransform。
- [x] 4.5 确认目标契约测试 RED → GREEN，桌面项目 Release 构建通过。
- [x] 4.6 发布隔离验证包，确认 ScrollViewer/原生滚动条 inset 为 12px，内容控件 inset 为 20px。

## 5. 最终验证与交付

- [x] 5.1 运行 Provider 页面测试、完整测试、Release 构建和 OpenSpec 严格校验。
- [ ] 5.2 提交修正并生成基于最终提交的发布包。
