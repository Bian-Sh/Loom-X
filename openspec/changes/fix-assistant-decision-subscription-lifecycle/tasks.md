## 1. 通用 AskUser 契约

- [x] 1.1 先补充失败测试，证明用户明确要求测试 AskUser 时系统提示允许直接调用且不要求 Skill、Bridge 或 Chrome
- [x] 1.2 更新 Assistant 系统提示、工具描述和规格测试，使 AskUser 成为通用 Human-in-the-loop 工具

## 2. Approval Card 状态模型

- [x] 2.1 先为当前字段、步骤导航、跳过、必填限制、值保留和最终提交补充失败测试
- [x] 2.2 实现 AskUser 状态 ViewModel 的逐题分页状态与字段清空/当前页验证能力

## 3. 第一版 Approval Card 视图

- [x] 3.1 先更新 XAML/代码后置契约测试，覆盖紧凑卡片、步骤导航、关闭、Skip、Continue/Submit 和四类字段模板
- [x] 3.2 完成第一版独立 Window Approval Card，并接通 Broker 提交/取消
- [x] 3.3 补齐中英日繁体本地化，并验证透明/非透明主题下不使用固定网页配色

## 4. 第一轮集成与交付

- [x] 4.1 运行 AskUser、AssistantService、Broker 和生命周期定向测试，修复回归
- [x] 4.2 运行 OpenSpec strict validate、Release build 和完整测试
- [x] 4.3 发布第一版桌面包并通过用户截图确认五类字段真实提交结果

## 5. 输入框悬浮卡片与简版消息队列增量

- [ ] 5.1 先补充失败测试，要求 AskUser 不再创建独立 Window，而是由 AssistantView 暴露输入框上方悬浮卡片；卡片无标题栏和右上角关闭按钮
- [ ] 5.2 先补充失败测试，覆盖运行中发送入队、顺序、删除、当前轮次结束后出队、失败暂停和 SessionId 隔离
- [ ] 5.3 将 AskUser 视图改为应用内悬浮卡片，并实现兼容后续 Codex 风格扩展的简版会话消息队列
- [ ] 5.4 修复 Avalonia 生命周期测试的 UI 线程顺序依赖，重新运行定向测试、完整测试、发布与 `cua-driver` 顶层窗口验收
