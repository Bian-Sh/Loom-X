# AI 助手决策订阅生命周期、AskUser 悬浮卡片与消息队列验证报告

- 日期：2026-09-20
- Change：`fix-assistant-decision-subscription-lifecycle`
- 结论：PASS

## 规格线索与生命周期结论

- 旧规格只要求 Assistant 在真实请求边界处理 pending decision，不要求页面挂载、供应商页切换或控制台导航时激活订阅。
- Browser Bridge 仍应在 AI 理解任务确实需要浏览器能力并按 Skill 申请租约后启动；AskUser 自身是通用 Human-in-the-loop 工具，不依赖 Skill、Bridge 或 Chrome。
- 当前实现仅在单次 Assistant 轮次进入 AgentLoop 前调用 `Activate()`，并在轮次完成、失败或取消时通过 `Deactivate()` 收敛；页面挂载和普通导航不会激活订阅。

## AskUser 悬浮卡片

- 原独立 `Window` 已替换为 `AskUserCard : UserControl`，由 `AssistantView` 的 `Popup` 锚定在 `inputCard` 正上方。
- Popup 使用 `ShouldUseOverlayLayer="True"`，因此卡片属于应用顶层窗口内部的 OverlayLayer，不创建第二个 HWND。
- 卡片无独立标题栏和右上角关闭按钮；`allow_cancel=true` 时底部显示低强调“取消”。
- 单选、多选、数字、单行文本、多行文本、逐题导航、跳过、校验和结果类型保持原契约。
- 默认产品链路通过 `PendingAskUser` 和 `Completion` 完成 Broker 提交或取消，不再调用 `ShowDialog`。

## 简版消息队列

- Assistant 运行或等待 AskUser 时，输入框和发送按钮仍可使用；发送内容进入当前 Session 队列。
- 队列项具有稳定 `Id`、`SessionId`、创建时间、状态和显示顺序，为后续编辑、排序、Steer、Stop-and-Send 和持久化保留扩展空间。
- 当前版本实现 Session 内 FIFO、未发送消息删除、失败/停止时暂停、Session 隔离和当前会话投影。
- AskUser 提交或取消本身不是出队边界；只有当前 Assistant 轮次完整结束并成功后才继续出队。
- 完整 Codex 风格体验已记录在 `docs/superpowers/specs/2026-09-20-assistant-message-queue-requirements.md`，建议后续以独立 change `enhance-assistant-message-queue` 推进。

## 生命周期测试稳定性

- 根因是 `AppBuilder.SetupWithoutStarting()` 把 Avalonia UI 线程绑定到初始化测试线程，而异步数据库准备后的 continuation 可能切换线程。
- 生命周期测试改为在初始化线程同步等待异步准备和 Broker 结果，使 UI 对象创建、显示、关闭和释放不跨线程，消除执行顺序依赖。

## 自动验证

- RED 证据：新增契约与队列测试最初因缺少 `PendingAskUser`、卡片文件、队列 API 和完成信号而编译失败。
- 定向测试：36/36 通过，覆盖 AskUser、队列、默认卡片链路、UI 契约与生命周期。
- 完整测试：`dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore`，1083/1083 通过。
- Release 构建：`dotnet build LoomX.slnx -c Release --no-restore`，0 error。
- OpenSpec：`openspec validate fix-assistant-decision-subscription-lifecycle --strict` 通过。
- 发布：`scripts/publish-desktop.ps1 -Configuration Release -OutputDirectory outputs/2026-09-20-223553-assistant-ask-user-queue` 通过。

## `cua-driver` 顶层窗口验收

- 使用本地 `cua-driver 0.28.2`，后台 UIA 和应用顶层窗口截图完成验收；未使用 Codex auth token 内核，也未抢占系统鼠标。
- 在真实 AI 请求中直接要求调用 `assistant.ask_user`，卡片成功出现在输入框正上方，且没有独立窗口标题栏或关闭按钮。
- AskUser 等待期间输入框仍可编辑和发送；第二条消息显示为“排队消息”，删除按钮可用，删除后队列卡片消失。
- CUA 截图：
  - `outputs/2026-09-20-223553-assistant-ask-user-queue/cua-askuser-card-12s.png`
  - `outputs/2026-09-20-223553-assistant-ask-user-queue/cua-queue-visible.png`
  - `outputs/2026-09-20-223553-assistant-ask-user-queue/cua-queue-deleted.png`
- UIA 树始终只包含一个顶层 `Window "Loom-X"`；AskUser 和队列均作为该窗口内元素出现。

## 已知警告

- NuGet 继续报告既有 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 `NU1903` 安全警告。
- 既有 `SettingsViewModel` CS8618、`AnthropicResponseMapper` CA2024 和测试 CS8602 警告仍存在，本次未改动。
- 透明主题下截图颜色受系统合成影响；本次只据截图验证布局、层级、可见性和交互，不据此武断判断主题色值。

## 安全与一致性

- 未新增 API Key、Authorization、请求正文或工具参数日志。
- 未改变数据库路径、Broker Claim 并发协议、Skill 工具协议或 Browser Bridge 租约协议。
- 实现、测试、proposal、design、delta spec、需求记录和验证报告一致，无未接受偏差。