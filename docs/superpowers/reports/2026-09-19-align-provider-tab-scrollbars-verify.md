# align-provider-tab-scrollbars 验证报告

## 当前状态

- 用户根据截图否决了原 20px 方案，Change 已从 `archive` 回退到 `build`。
- 原验证结论已失效，不执行归档。
- 已完成页面修正和自动化验证；重新发布后的实机界面复核仍待执行。

## 用户反馈与根因

- 用户截图中的现有滚动条位于约 `x=548..553`，目标红线位于约 `x=559..564`。
- 原实现给 `ScrollViewer.provider-tab-scroll` 增加了 8 DIP 右侧 Margin。当前显示缩放下，这一补偿对应约 10 个物理像素，使滚动条明显位于红线左侧。
- Avalonia `TabControl` 模板本身已经提供右侧内容内缩，因此额外 8px 属于重复补偿。

## 修正实现

- 四个详情 Tab 继续共用 `provider-tab-scroll` class。
- 共享样式由 `Margin="0,0,8,0"` 改为 `Margin="0"`，使滚动条向右回到模板默认位置。
- 左侧 Provider 列表和测试响应文本框的嵌套滚动条未修改。

## TDD 证据

- RED：先把契约测试改为要求 `Margin="0"`，目标测试按预期失败于旧的 8px Margin。
- GREEN：页面样式改为 `Margin="0"` 后，同一目标测试通过。
- Provider 页面契约测试：30/30 通过。

## 自动验证证据

- Release 全量串行测试：1042/1042 通过，0 失败，0 跳过（`RunConfiguration.MaxCpuCount=1`）。
- Release 构建：0 错误；存在 2 个既有 `NU1903` 警告。
- OpenSpec 严格校验：通过。
- 修正后的临时发布目录：`outputs/20260920-020420`。

## 待完成

- 当前已有另一个 LoomX 单例进程运行，新的发布包启动后会把激活请求转交给旧实例，因此尚不能把新包与旧包混淆后截图。
- 为避免关闭其他会话启动的实例，本轮未擅自停止该进程。关闭现有 LoomX 后，需要从最终发布目录启动并重新截图确认滚动条与红线位置。

## 分支处理

- 当前仍在本地 `master`，未推送远端。
- 不归档，Comet 当前保持 `phase: build`。
