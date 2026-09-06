---
comet_change: runtime-graph-model-node-two-line
role: technical-design
language: zh-CN
---

# 概览 NodeGraph Model 节点双行标签设计

## 目标

让概览页 nodegraph 的 Model 节点与网关 Combo 成员 cell 保持一致的信息层级：

- 第一行显示 model 名称，作为焦点信息；
- 第二行显示 provider 名称，使用次要文本样式作为补充信息；
- 收窄 Model 节点宽度，减少拓扑横向占用。

## 范围

只修改 `RuntimeGraphControl` 的 Model 节点绘制和 `RuntimeGraphLayoutOptions.ModelWidth` 默认值。继续复用现有 `RuntimeGraphNode.DisplayName` 与 `ProviderDisplayName`，不改变拓扑投影、节点 ID、连线、选择、缩放和平移行为。

## 视觉与布局

- Model 节点宽度从 260px 调整为 220px，高度从 50px 调整为 58px，以容纳两行文本并保持与其他节点的基本高度节奏；
- 第一行使用当前 Model 节点的主文本色和半粗字重；
- 第二行使用 `TextSecondaryBrush`，字号略小，表现为 Provider 名称；
- 两行均限制在节点内，沿用现有字符省略策略，避免长名称撑大布局；
- Model 类型水印继续保留在右下角，并为文本区域预留水印空间。

## 实现方案

将 Model 节点从单个拼接字符串改为专用双行绘制：

1. 根据节点 ID 找到完整的 Model 节点数据；
2. 第一行绘制 `DisplayName`；
3. 第二行绘制 `ProviderDisplayName`；
4. Endpoint 和 Combo 节点继续使用现有单行绘制路径。

不新增业务模型字段，也不引入 XAML 控件或新的布局抽象。

## 验证

- 更新或新增契约测试，确认 Model 节点使用双行标签、主次文本分别使用对应字段，并确认 Model 宽度为 220px；
- 运行 NodeGraph 相关测试和 Overview 契约测试；
- 构建桌面项目，确保 Avalonia 绘制 API 和布局尺寸没有编译回归。

## 非目标

- 不修改 Gateway 页面 Combo 成员 cell；
- 不修改 Provider/Model 数据源或排序；
- 不改变 Model 节点的点击选择和边高亮行为；
- 不处理与本次布局无关的未跟踪文件。
