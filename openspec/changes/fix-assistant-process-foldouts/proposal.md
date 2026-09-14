# 修复 AI 助手过程折叠层级与完成时机

## 问题

现有 AI 助手把每个步骤或首个文本增量都当成 `finish_content`，导致同一轮对话产生多个“已完成”过程块；“思考”和“工具调用”也没有各自的子 foldout，箭头位置、颜色及运行中文案与预期不符。

## 根因

`AssistantViewModel.Project` 在 `StepStarted`、`TextDelta` 和任意 `MessageCompleted` 上结束当前过程组，把本应覆盖整轮的父 foldout 错误地缩短为单步骤生命周期。视图只把过程子项渲染为静态标签和正文，无法表达子 foldout 的折叠预览。

## 目标

1. 每轮对话只使用一个过程父 foldout，汇总该轮全部思考与工具调用，并与最终内容处于消息流同级。
2. 最终内容完成前显示“处理中”，只有无工具调用的最终 assistant 消息完整输出后才显示“已完成”并默认折叠。
3. 思考与工具调用各自成为子 foldout：折叠时显示最新内容，展开时显示标签和完整内容。
4. 所有 foldout 箭头位于标题后方并使用白色。

## 规格说明

本次修复收紧已有 AI 助手设计的 UI 投影语义，不新增 OpenSpec capability，因此不创建 delta spec。
