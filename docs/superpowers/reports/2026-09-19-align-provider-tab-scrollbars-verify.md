# align-provider-tab-scrollbars 验证报告

## 摘要

| 维度 | 状态 |
|---|---|
| 完整性 | 11/11 任务完成 |
| 正确性 | ScrollContent 保持原位；四个 Tab 的原生 ScrollBar 均位于红线位置 |
| 一致性 | 实现、契约测试、proposal、design 和 delta spec 一致 |

结论：最终方案通过自动化、Release 构建和缩小窗口后的实际滚动条可见性验证。

## 用户反馈与最终根因

- 用户要求只移动 ScrollBar，不能把 ScrollContent 右边缘一起向右移动。
- 把模板 ScrollBar 用 RenderTransform 平移出父级布局边界存在裁剪或层级遮盖风险。
- Avalonia Fluent ScrollViewer 模板将内容呈现器和纵向 ScrollBar 分层；ScrollViewer.Padding 只作用于内容呈现器。

## 最终实现

- `ScrollViewer.provider-tab-scroll` 使用 `Margin="0"`，让原生 ScrollBar 保持在模板自身层级和红线位置。
- 同一 style 使用 `Padding="0,0,8,0"`，只把 ScrollContent 右边缘向左保留 8px。
- 不对 `PART_VerticalScrollBar` 设置 RenderTransform 或额外 ZIndex。
- 左侧 Provider 列表与测试响应文本框的嵌套滚动条未修改。

## TDD 与自动验证

- RED：契约测试要求“Margin=0 + 右 Padding=8 + 无 ScrollBar Transform”时，旧 Transform 方案按预期失败。
- GREEN：切换为内容 Padding 后目标测试通过。
- Provider 页面契约测试：30/30 通过。
- Release 全量串行测试：1042/1042 通过，0 失败，0 跳过。
- Release 构建：0 错误；存在 2 个既有 `NU1903` 警告。
- OpenSpec 严格校验：通过。

## 最终发布包实机验证

- 发布目录：`outputs/20260920-024115`。
- `LoomX.exe` 版本：`0.12.6+eab36ecc2eda2a5814333f4eb752a346f297a4d8`。
- `LoomX.exe` SHA-256：`120E169B4FB016860D34083ED4204426B91D4D6B0BC240FD0607014D24D8E62A`。
- 发布目录只包含一个 `LoomX.exe`。
- 最终截图：`outputs/20260920-024115/provider-scrollbar-native-layer.png`。

| Tab | TabRight | ScrollViewer/ScrollBar Right | ScrollBar inset |
|---|---:|---:|---:|
| 基础 | 1173 | 1161 | 12 |
| 高级 | 1173 | 1161 | 12 |
| 模型 | 1173 | 1161 | 12 |
| 测试 | 1173 | 1161 | 12 |

- 基础 Tab 内容控件右边缘：`1153`，内容 inset 保持 `20px`。
- 窗口缩小到 `1180×600` 后，纵向 ScrollBar 实际显示在内容右侧、红线位置，未被内容或父级层级遮盖。

## 分支状态

- 修正提交：`eab36ec`。
- 当前变更位于隔离分支 `codex/provider-scrollbar-overlay-position`，待安全整合到存在其他会话未提交修改的本地 `master`。
- 未推送远端，未执行归档。
