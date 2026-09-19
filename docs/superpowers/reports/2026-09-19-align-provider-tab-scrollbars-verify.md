# align-provider-tab-scrollbars 验证报告

## 摘要

| 维度 | 状态 |
|---|---|
| 完整性 | 6/6 任务完成；用户截图校准已纳入实现 |
| 正确性 | 四个 Tab 的主滚动区域均对齐截图参考线 |
| 一致性 | 实现、契约测试、proposal、design 和 delta spec 一致 |

结论：修正后的实现通过自动化与最终发布包实机验证；本报告替代此前被用户否决的 20px 方案结论。

## 用户反馈与根因

- 用户截图中的旧滚动条位于约 `x=548..553`，目标红线位于约 `x=559..564`。
- 原实现给 `ScrollViewer.provider-tab-scroll` 增加了 8 DIP 右侧 Margin；当前显示缩放下，这一补偿对应约 10 个物理像素，使滚动条明显位于红线左侧。
- Avalonia `TabControl` 模板本身已有右侧内容内缩，因此额外 8px 是重复补偿。

## 修正实现

- 四个详情 Tab 继续共用 `provider-tab-scroll` class。
- 共享样式由 `Margin="0,0,8,0"` 改为 `Margin="0"`，滚动条向右回到模板默认位置。
- 左侧 Provider 列表和测试响应文本框的嵌套滚动条未修改。

## TDD 证据

- RED：先把契约测试改为要求 `Margin="0"`，目标测试按预期失败于旧的 8px Margin。
- GREEN：页面样式改为 `Margin="0"` 后，同一目标测试通过。
- Provider 页面契约测试：30/30 通过。

## 自动验证证据

- Release 全量串行测试：1042/1042 通过，0 失败，0 跳过（`RunConfiguration.MaxCpuCount=1`）。
- Release 构建：0 错误；存在 2 个既有 `NU1903` 警告。
- OpenSpec 严格校验：通过。

## 最终发布包实机验证

- 发布目录：`outputs/20260920-020557`。
- `LoomX.exe` 版本：`0.12.6+8e173f60ef7c7a79fef654ced155d40dab07f12b`。
- `LoomX.exe` SHA-256：`58C780D98DFCCCA7201B4BB992ADA03A7D6EBBA4B5F49026D5E73B33DA27372C`。
- 发布目录仅包含一个可执行文件 `LoomX.exe`。
- 最终截图：`outputs/20260920-020557/provider-scrollbar-corrected.png`。
- UIA 几何读数：

| Tab | TabRight | MainScrollRight | 右侧间距 |
|---|---:|---:|---:|
| 基础 | 1303 | 1291 | 12 |
| 高级 | 1303 | 1291 | 12 |
| 模型 | 1303 | 1291 | 12 |
| 测试 | 1303 | 1291 | 12 |

四个 Tab 的主滚动区域右边缘均位于 `TabControl` 右边缘向左 12px，与用户截图中红线相对面板边缘的位置一致。测试响应文本框的嵌套滚动区域未计入上述主滚动区域测量。

## 分支处理

- 当前位于本地 `master`，未推送远端。
- 本轮只停止了从最终发布目录启动的验证进程 PID 28764。
