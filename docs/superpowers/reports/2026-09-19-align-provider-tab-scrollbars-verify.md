# align-provider-tab-scrollbars 验证报告

## 当前状态

- 用户确认真实需求是只移动 ScrollBar，不能移动 ScrollContent。
- 用户进一步指出把 ScrollBar 平移出父级边界可能因层级关系被遮盖。
- Change 已回退到 `build`；此前两轮验证结论均失效，不执行归档。

## 最终布局方案

- `ScrollViewer.provider-tab-scroll` 使用 `Margin="0"`，原生 ScrollBar 保持在模板内部和红线位置。
- 使用 `Padding="0,0,8,0"` 仅内缩 `PART_ScrollContentPresenter` 的内容。
- 不使用 ScrollBar `RenderTransform`，不存在平移到父级边界外的裁剪或层级遮盖。

## TDD 证据

- RED：契约测试要求“Margin=0 + 右 Padding=8 + 无 ScrollBar Transform”时，旧 Transform 方案按预期失败。
- GREEN：切换为内容 Padding 后目标契约测试通过。

## 自动验证

- Provider 页面契约测试：30/30 通过。
- Release 全量串行测试：1042/1042 通过，0 失败，0 跳过。
- Release 构建：0 错误，存在 2 个既有 `NU1903` 警告。
- OpenSpec 严格校验：通过。

## 初步实机证据

- 隔离验证包：`outputs/20260920-023540`。
- 初步截图：`outputs/20260920-023540/provider-scrollbar-native-layer.png`。
- UIA 几何：`TabRight=1407`、`ScrollViewerRight=1395`、`ContentControlRight=1387`。
- 原生 ScrollBar 右侧 inset 为 12px，内容右侧 inset 保持 20px。

## 待完成

- 提交修正并基于最终提交重新发布。
