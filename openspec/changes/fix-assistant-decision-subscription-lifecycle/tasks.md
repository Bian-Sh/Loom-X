## 1. 通用 AskUser 契约

- [x] 1.1 先补充失败测试，证明用户明确要求测试 AskUser 时系统提示允许直接调用且不要求 Skill、Bridge 或 Chrome
- [x] 1.2 更新 Assistant 系统提示、工具描述和规格测试，使 AskUser 成为通用 Human-in-the-loop 工具

## 2. Approval Card 状态模型

- [ ] 2.1 先为当前字段、步骤导航、跳过、必填限制、值保留和最终提交补充失败测试
- [ ] 2.2 实现 AskUserDialogViewModel 的逐题分页状态与字段清空/当前页验证能力

## 3. Approval Card 视图

- [ ] 3.1 先更新 XAML/代码后置契约测试，覆盖紧凑卡片、步骤导航、关闭、Skip、Continue/Submit 和四类字段模板
- [ ] 3.2 重做 AskUserDialog XAML 与代码后置，接通导航、单选自动前进、键盘行为、主题资源和 Broker 提交/取消
- [ ] 3.3 补齐中英日繁体本地化，并验证透明/非透明主题下不使用固定网页配色

## 4. 集成与交付

- [ ] 4.1 运行 AskUser、AssistantService、Broker 和生命周期定向测试，修复回归
- [ ] 4.2 运行 OpenSpec strict validate、Release build 和完整串行测试
- [ ] 4.3 重新发布桌面包到带可读时间的 outputs 目录，并完成 Approval Card 桌面交互验收

