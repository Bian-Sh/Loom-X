# align-provider-tab-scrollbars 验证报告

## 摘要

| 维度 | 状态 |
|---|---|
| 完整性 | 15/15 任务完成 |
| 正确性 | ScrollContent 保持原位；四个 Tab 的原生 ScrollBar 均位于红线位置；API Key 输入框与相邻输入框右边缘一致 |
| 一致性 | 实现、契约测试、proposal、design 和 delta spec 一致 |

结论：最终方案通过自动化、Release 构建、UIA 几何读取和缩小窗口后的实际可见性验证；用户已确认目标效果正确。

## 用户反馈与最终根因

- 用户要求只移动 ScrollBar，不能把 ScrollContent 右边缘一起向右移动。
- 把模板 ScrollBar 用 RenderTransform 平移出父级布局边界存在裁剪或层级遮盖风险。
- Avalonia Fluent ScrollViewer 模板将内容呈现器和纵向 ScrollBar 分层；ScrollViewer.Padding 只作用于内容呈现器。

## 最终实现

- `ScrollViewer.provider-tab-scroll` 使用 `Margin="0"`，让原生 ScrollBar 保持在模板自身层级和红线位置。
- 同一 style 使用 `Padding="0,0,8,0"`，只把 ScrollContent 右边缘向左保留 8px。
- 不对 `PART_VerticalScrollBar` 设置 RenderTransform 或额外 ZIndex。
- 左侧 Provider 列表与测试响应文本框的嵌套滚动条未修改。
- API Key 行使用单层 Grid 覆盖眼睛按钮，并保留 8px 右 Margin，避免长密钥内容把输入框扩展到滚动条预留区域。

## TDD 与自动验证

- RED：契约测试要求“Margin=0 + 右 Padding=8 + 无 ScrollBar Transform”时，旧 Transform 方案按预期失败。
- GREEN：切换为内容 Padding 后目标测试通过。
- API Key RED → GREEN：契约测试先要求覆盖布局和 8px 右侧保留并按预期失败，随后实现通过。
- Provider 页面契约测试：30/30 通过。
- Release 全量串行测试：1042/1042 通过，0 失败，0 跳过。
- Release 构建：0 错误；存在 2 个既有 `NU1903` 警告。
- OpenSpec 严格校验：通过。

## 最终发布包实机验证

- 发布目录：`outputs/20260920-025929`。
- `LoomX.exe` 版本：`0.12.6+d9e5d0f39305bcf61da013b3d9dbd50265c84476`。
- `LoomX.exe` SHA-256：`561FC6D5F8B670132BE6DF1B1FBFF7CD769CF4983AA872DEBF6621FB27EEF49B`。
- 发布目录只包含一个 `LoomX.exe`。
- 最终截图：`outputs/20260920-025929/provider-final.png`。

| Tab | TabRight | ScrollViewer/ScrollBar Right | ScrollBar inset |
|---|---:|---:|---:|
| 基础 | 1173 | 1161 | 12 |
| 高级 | 1173 | 1161 | 12 |
| 模型 | 1173 | 1161 | 12 |
| 测试 | 1173 | 1161 | 12 |

- 基础 Tab 内容控件右边缘保持 `20px` inset。
- 最终包 UIA 读数：显示名称、Base URL、API Key 输入框的 `Right` 均为 `1205`，API Key 不再多延伸 8px。
- 窗口缩小后，纵向 ScrollBar 实际显示在内容右侧、红线位置，未被内容或父级层级遮盖。

## 分支状态

- 滚动条修正提交：`eab36ec`。
- API Key 边界修正提交：`d9e5d0f`。
- 当前变更位于隔离分支 `codex/provider-scrollbar-overlay-position`，待安全整合到存在其他会话未提交修改的本地 `master`。
- 未推送远端，未执行归档。
