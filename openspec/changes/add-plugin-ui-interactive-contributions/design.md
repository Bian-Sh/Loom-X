# 设计：插件声明式 UI 交互回传

## 决策 1：协议版本升级而非新增平行协议

`PluginUiContribution.CurrentSchemaVersion` 由 1 提升到 2，同时保留 `MinimumSchemaVersion = 1`。
v1 快照继续渲染，只是不得包含交互节点（Validator 返回 `INTERACTIVE_NODE_REQUIRES_V2` 并隔离）。

理由：避免宿主同时维护两套渲染路径；版本字段本就是宿主判断能否理解语义的唯一依据。

## 决策 2：插件保持无状态，字段值由宿主在命令里打包回传

插件每次 `GetUiContributions` 都重新生成整棵节点树（声明值即当前状态）。
宿主在渲染时为本次快照维护一张 `fieldId → 当前值` 的表，命令触发时合并：

1. 表单字段值（键为 FieldId）；
2. 节点自带的 `Arguments`（键冲突时节点参数优先，插件不得依赖冲突行为）。

命令执行后插件自行 `UiInvalidated`，宿主重新拉取快照。这样插件不需要持有控件引用、不需要知道 Avalonia，
宿主也不需要理解字段的业务含义。

## 决策 3：开关/下拉支持「值变化即回传」

`PluginUiToggleFieldNode` / `PluginUiChoiceFieldNode` 允许携带 `CommandId`。
规则启停这类操作不应再要求用户点一次「保存」，值变化时宿主立即回传命令。
文本字段不支持即时回传（避免每次按键触发一次命令与整树重建），仍由相邻按钮提交。

## 决策 4：interactive 必须在 Manifest 声明

仅有插件实现 `IPluginUiCommandHandler` 不足以接收命令：Manifest 对应 Contribution 必须声明 `interactive: true`。
未声明时 Runtime 返回 `Unhandled` 并记录诊断。这让「哪些插件 UI 能反过来驱动插件」成为可审计的声明，而不是隐式能力。

## 决策 5：安全边界

- 命令参数与字段值在 Runtime 侧限制数量（≤32）与单值长度（≤4096），超限直接拒绝。
- 插件返回的 `Message` 会 Toast 给用户，因此规范明确要求它只能是安全摘要，
  不得包含凭据、密钥、请求/响应正文。maskit 插件只回显「已添加规则」这类文案，不回显规则值。
- 命令处理器抛异常时只记录 `COMMAND_FAILURE` 诊断，不影响同一插件的 Pipeline 能力。
- maskit 的命中原文只存在进程内存映射表里，规则文件只保存规则本身（对齐「凭据原文不落库」）。

## 决策 6：节点上限

交互列表会显著放大节点数，因此 `MaxNodes` 由 128 提升到 512、`MaxDepth` 由 12 提升到 14；
maskit 侧再对列表做分页（一次最多渲染 24 条并给出总数提示），两侧共同保证不会突破宿主校验。

## 已知约束

插件运行在独立 ALC，插件程序集里的静态字段与宿主/测试进程不共享。
插件状态只能通过命令返回值、UI 快照与 Runtime 诊断来观察——测试必须按这个口径编写。
