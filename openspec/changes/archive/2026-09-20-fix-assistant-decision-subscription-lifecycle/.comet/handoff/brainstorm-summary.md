# Brainstorm Summary

- Change: fix-assistant-decision-subscription-lifecycle
- Date: 2026-09-20

## 确认的技术方案

用户确认将 AskUser 改为不依赖 Skill、Browser Bridge、Chrome 或资料通道的通用 Human-in-the-loop 工具，并将现有大型 Dialog 重做为参考 Beautiful UI Approval Card 的紧凑逐题交互。每个现有 UserDecisionField 对应一页，支持步骤导航、可选字段跳过、继续/提交、允许取消时关闭、键盘操作和单选自动前进；最终仍使用现有 Broker Submit/Cancel 契约。

## 关键取舍与风险

- selection 字段继续返回 option id，不混入自由文本；“Something else”由独立 text 字段表达。
- 单选自动前进放在 View 层短延迟执行，分页与验证规则保持在 ViewModel 中。
- 高度和页面动画为渐进增强，业务正确性不依赖动画。
- 普通步骤不默认强制询问，但这不是禁止模型或用户主动使用 AskUser。

## 测试策略

先写失败测试覆盖通用工具提示、分页状态、前后导航、跳过、必填限制、值保留、提交/取消和 XAML 契约；随后实现最小代码，运行定向测试、完整串行测试、Release build、OpenSpec validate，并重新发布桌面包做实际交互验证。

## Spec Patch

更新 assistant-user-decisions：明确 AskUser 的通用独立性，并新增 Approval Card 分页、导航、跳过、单选自动前进、取消和主题协调场景。
