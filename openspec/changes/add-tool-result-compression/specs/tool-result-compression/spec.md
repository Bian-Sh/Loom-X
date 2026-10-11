## Purpose

定义 Tool Result Compression Router 插件的能力：在 Router 请求正文发送给外部 Provider 前压缩历史工具调用结果，削减输入 token，同时保持最近结果与凭据引用的完整性。内置 AI 助手和外部 Agent Client 均作为 Router 客户复用该能力。

## ADDED Requirements

### Requirement: 工具结果识别

插件 SHALL 在 Router 请求正文中识别工具结果槽位，覆盖 OpenAI Chat Completions 的 `role: tool` / `role: function`、OpenAI Responses 的 `input[].function_call_output`、Anthropic 的 `content[].tool_result` 以及 Gemini 的 `contents[].parts[].functionResponse`；协议未知时 SHALL 同时尝试上述形态。插件 MUST NOT 修改 system、user 或 assistant 内容。

#### Scenario: OpenAI 工具消息被识别并压缩

- **WHEN** 请求正文 `messages` 中存在 `role: tool` 且其 `content` 为超过阈值的重复日志
- **THEN** 该条工具结果被压缩，其余消息保持原样

#### Scenario: 对话内容不受影响

- **WHEN** 请求正文只包含 assistant 与 user 内容
- **THEN** 请求正文原样通过，插件不报告修改

### Requirement: 保真窗口

插件 SHALL 保留请求末尾 `KeepRecentToolResults`（默认 2）条工具结果原文，只压缩更早的结果；配置 MUST 允许设为 0 以压缩全部结果。

#### Scenario: 最近两条结果保留原文

- **WHEN** 请求正文含 3 条可压缩工具结果且保真窗口为 2
- **THEN** 仅第 1 条被压缩，第 2、3 条保持原文

### Requirement: 压缩策略

插件 SHALL 依次应用智能过滤（ANSI 转义、回车、冗余空行、行尾空白）、结构化折叠（JSON 与 git/测试/目录命令输出）、去重（连续重复行记为 `×N`、非连续高频行记为 `共 N 次`）与截断（超长行标注省略字符数、超行数保留头尾并标注省略行数）。

#### Scenario: 重复日志被折叠为计数

- **WHEN** 工具结果含 200 行相同日志
- **THEN** 输出折叠为单行并标注出现次数

#### Scenario: 大量 JSON 数组被折叠且保持合法

- **WHEN** 工具结果是含 200 项的 JSON 数组
- **THEN** 输出仅保留前若干项并标注省略项数，且输出仍可被 JSON 解析器解析

#### Scenario: 测试输出只保留失败

- **WHEN** 工具结果是含大量通过用例与少量失败用例的测试输出
- **THEN** 通过用例折叠为计数，失败用例与精简 traceback 保留

### Requirement: 收益判定与最小改动

当压缩后长度不小于原文，或节省比例低于 `MinSavingsRatio` 时，插件 SHALL 原样返回原文；短于 `MinChars` 的结果 MUST NOT 被压缩。

#### Scenario: 无法压缩的内容原样通过

- **WHEN** 工具结果由互不重复且不长于单行上限的行组成
- **THEN** 插件报告未修改并保留原文

### Requirement: 凭据占位符完整性

任何截断 MUST NOT 留下不完整的 `{{LOOMX_CREDENTIAL_<Base32>}}` 引用；当截断边界落在未闭合的占位符内部时，截断 SHALL 回退到该占位符起点之前。

#### Scenario: 占位符跨越截断边界

- **WHEN** 长行截断边界落在 `{{LOOMX_CREDENTIAL_...}}` 中间
- **THEN** 输出不包含该占位符的任何片段

#### Scenario: 完整占位符保留

- **WHEN** 占位符完整落在截断边界之内
- **THEN** 输出保留完整占位符

### Requirement: 失败放行与可关闭

压缩 SHALL 声明 `continue-on-error`：处理异常或协议无法解析时 MUST 放行原文且不得阻断 Router 发送。插件 MUST 支持通过 Plugin-owned 配置或 Runtime 启停关闭；关闭时请求正文 MUST 原样通过。

#### Scenario: 压缩器异常时放行原文

- **WHEN** 请求正文不是合法 JSON 或压缩过程抛出异常
- **THEN** 请求正文原样发送给 Provider，并记录一次异常

#### Scenario: 插件被禁用

- **WHEN** 插件配置 `enabled` 为 false
- **THEN** 请求正文原样通过且不产生压缩计数

### Requirement: 观测披露最小化

插件 SHALL 只披露已压缩请求数、压缩条数、原始与压缩字符数、节省比例与异常数；观测与 UI MUST NOT 包含工具结果内容、模型输入或任何凭据值。

#### Scenario: 卡片展示压缩收益

- **WHEN** 宿主请求插件 `card-body` 快照
- **THEN** 插件返回声明式节点，只包含计数、字符数与比例，且快照通过宿主结构校验

### Requirement: 节省的 token 对比

插件 SHALL 分别累计压缩前后的字符数与估算 token 数，并在设置页以「原始 / 压缩后 / 节省」三行并排展示。字符数 SHALL 为精确值，token 数 MUST 标注为估算，且 MUST NOT 声称与 Provider 计费口径一致。

#### Scenario: 设置页给出压缩前后的 token 对比

- **WHEN** 宿主请求插件 `detail-body` 快照且已有压缩记录
- **THEN** 快照包含原始 token 与压缩后 token 两列，并给出节省量与节省比例

#### Scenario: 无记录时给出空态

- **WHEN** 还没有任何压缩记录
- **THEN** 节省对比区展示零值与空态提示，不得报错或省略该区域

### Requirement: 统计跨会话累计

插件 SHALL 把累计统计写入插件自有数据目录并在初始化时载入；重置统计 SHALL 只清零计数，MUST NOT 改动配置或压缩行为。

#### Scenario: 重启后统计续算

- **WHEN** 插件在同一数据目录上重新初始化
- **THEN** 累计请求数、节省字符数与节省 token 与重启前一致

#### Scenario: 重置统计不影响配置

- **WHEN** 用户触发重置统计
- **THEN** 计数归零，保真窗口与收益阈值等配置保持原值

### Requirement: 插件自带本地化

插件 SHALL 自带本地化资源（`i18n/<culture>.json`，随插件负载分发，默认文化额外嵌入程序集兜底），并 SHALL 通过 `IPluginUiContributionProvider.GetDisplayInfo` 按宿主传入的文化返回名称、简介、分类与启停说明。宿主资源文件 MUST NOT 包含任何插件专属文案。

展示信息 SHALL NOT 由独立契约承载：它 MUST 是 UI 贡献 Provider 的默认接口方法，默认返回 null，由宿主回落到插件 id 与通用文案。

资源查找与文化回落 SHALL 由契约程序集统一实现（`PluginTextCatalog`），插件 MUST NOT 各写一份实现。

#### Scenario: 宿主索取展示信息

- **WHEN** 宿主以 `zh-CN` 请求插件展示信息
- **THEN** 插件返回中文名称与启停说明，宿主不查自己的资源文件

#### Scenario: 未实现展示信息的插件仍可加载

- **WHEN** 某插件只贡献 UI 而未覆盖 `GetDisplayInfo`
- **THEN** 宿主使用默认实现返回 null，回落到插件 id 与宿主通用文案，不报错

#### Scenario: 多个插件的资源同名但各占一个目录

- **WHEN** 两个插件的 `plugin.manifest.json` 与 `i18n/<culture>.json` 文件名相同
- **THEN** 每个插件只出现在自己的 `plugins/<id>/` 下，互不覆盖；宿主、插件工程与测试工程的输出目录都不得出现平铺副本

#### Scenario: 未知文化回落到默认文化

- **WHEN** 宿主请求一个插件未提供的文化
- **THEN** 插件返回嵌入的默认文化文案，不得返回空白或键名

#### Scenario: 宿主资源不含插件文案

- **WHEN** 检查宿主资源文件
- **THEN** 其中不存在任何插件的名称或插件专属键

### Requirement: 设置页策略控制

插件 SHALL 提供声明式设置页，至少支持：启用/停用压缩、强度预设（保守 / 标准 / 激进）、保真窗口条数、收益阈值百分比、JSON 折叠 / 重复行折叠 / 命令输出过滤三个开关、恢复默认与重置统计。所有改动 MUST 由插件落库，UI MUST NOT 直接改状态。

#### Scenario: 切换强度预设

- **WHEN** 用户在设置页选择「激进」
- **THEN** 插件按激进预设写入配置，且档位回读为该预设

#### Scenario: 手动改数值后档位显示为自定义

- **WHEN** 用户手动修改了保真窗口或收益阈值
- **THEN** 强度档位不再声称属于任何预设，展示为自定义

#### Scenario: 非法输入被拒绝

- **WHEN** 保真窗口或收益阈值填入非数字
- **THEN** 插件返回失败提示，配置保持原值
