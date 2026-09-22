# 活动页顶部控件与观察者刷新设计

## 目标

活动页沿用主窗口顶栏的“请求活动”标题，删除页面内容区重复的“最近请求”标题、副标题、“过去 7 天”、“清除筛选”和“刷新”控件。列表内部现有搜索、状态和协议筛选保持不变。

活动数据不使用定时轮询。网关记录请求摘要时通过 `ActivityStore.ActivityEnqueued` 事件通知观察者，桌面活动 ViewModel 订阅事件，将新记录按当前筛选条件直接插入列表；页面首次打开时仍读取 SQLite 快照。

## 数据流

`ActivityMiddleware` → `IActivityStore.TryEnqueue` → `ActivityStore.ActivityEnqueued` → `GatewayProcessService.ActivityEnqueued` → `ActivityViewModel` → Avalonia UI。

事件在活动入队成功后触发，避免批量持久化延迟影响实时界面。ViewModel 在 UI 线程处理事件、保留最多 500 条可见活动，并同步更新统计摘要；页面离开时解除订阅。

## 兼容性与边界

- 不改变活动 SQLite 表结构、查询接口或列表筛选条件。
- 若网关由外部进程提供，桌面端无法获得同进程事件，页面仍可显示首次 SQLite 快照；嵌入式网关路径支持实时推送。
- 事件通知异常不得改变已入队活动的处理结果；当前订阅处理仅投递 UI 线程操作。

## 验证

- 删除顶部 Header 行后，统计卡片和工作区保持自适应布局。
- 单元测试验证活动入队触发观察者事件。
- 运行 `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj` 和 `dotnet build OllamaHub.Desktop/OllamaHub.Desktop.csproj`。
- 使用 `scripts/publish-desktop.ps1 -Configuration Release` 生成带时间戳的 Windows 发布包。
