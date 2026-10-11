## Context

Plugin System 的定位是扩展 Router 对请求、响应以及中间数据的处理能力。当前 Router 已有 Request/Response 两个生产扩展点，第一方 Credential Protection 插件占据两者并完成 fail closed 的数据安全处理。工具结果的 token 占用问题尚无落点：Credential Protection 的提案已明确把压缩排除在该 change 之外，并要求未来压缩实现于 Router Request Pipeline 的结构化处理阶段。

约束：

- 契约程序集只有 `Request` / `Response` 两种 `ExtensionKind`，新增 ToolResult 扩展点需要改动 Runtime 与执行链，收益不明确。
- 插件之间互不感知：压缩插件不得假设 Credential Protection 是否存在或先后顺序。
- 会话 JSONL 是内置助手自身的数据，插件不改本地持久化。

## Goals / Non-Goals

**Goals**

- 在不改动 Plugin 契约与 Runtime 的前提下，用现有 Request 扩展点完成工具结果压缩。
- 压缩策略对齐 rtk：智能过滤、分组、截断、去重，并针对 Agent 场景补充 JSON 结构化折叠。
- 保真优先：最近的工具结果、无法压缩的内容、凭据占位符都不被破坏。
- 可关闭、可观测、可预测：配置 Plugin-owned，失败放行，UI 只披露计数。

**Non-Goals**

- 不新增 ToolResult Pipeline 或 ExtensionKind。
- 不把压缩写回 `AgentSession` 或会话 JSONL。
- 不做模型参与的语义摘要或向量召回。
- 不修改 Provider 鉴权 Header、路由与重试逻辑。

## Decisions

### 决策 1：压缩挂在 Request Pipeline，而不是新增扩展点

工具结果在每一轮请求中都会随历史消息重新发送，Request Pipeline 是唯一能同时覆盖内置助手与外部 Agent Client、且无需新契约的边界。新增 ToolResult 扩展点要让 Runtime 感知 Agent 生命周期，与 Router-first 原则冲突。

代价：压缩发生在**请求构造之后**，模型看到的是压缩后的历史，而本地会话仍保留原文；这是有意取舍，与 rtk「输出到达上下文前过滤」的语义一致。

### 决策 2：失败放行（continue-on-error）

压缩是可降级的能力，不是数据安全边界。压缩失败时保留原文只会损失 token，不会造成泄露；若改为 fail closed 则会让整个 Router 因压缩器异常不可用。因此 Extension 声明 `continue-on-error`，异常时记录观测并返回 `Passed`。

### 决策 3：保真窗口保留最近 N 条原文

模型最可能继续引用最近的工具结果。默认保留末尾 2 条原文，只压缩更早的结果；`KeepRecentToolResults` 可配置到 0（全部压缩）或更大值。

### 决策 4：先结构化、后通用截断，收益不足则不改

执行顺序为：噪音清理 → JSON 折叠 / 命令输出过滤 → 去重 → 长行截断 → 头尾截断。结构化过滤先消耗掉最容易被压缩的部分，通用截断只处理剩余噪音。最后统一判定：压缩后不比原文短、或节省比例低于 `MinSavingsRatio`（默认 15%）时**原样返回**，避免为了压缩而压缩。

### 决策 5：JSON 折叠保持合法 JSON 并避免转义膨胀

Agent 的工具结果大量是 JSON。折叠只做数组截断、长字符串截断与超深层级折叠，输出仍是合法 JSON，模型仍可解析。折叠后用 `UnsafeRelaxedJsonEscaping` 紧凑输出，并对超过单行上限的结果渐进收紧（数组项数与字符串长度逐轮减半），避免后续长行截断把 JSON 切成半截。

请求正文重新序列化同样使用该编码器：宿主默认序列化会把中文转成 `\uXXXX`，重新序列化若不反转义会让体积反向膨胀，抵消压缩收益。

### 决策 6：凭据占位符是硬边界

`{{LOOMX_CREDENTIAL_<Base32>}}` 是 Credential Protection 的长期引用。任何截断若只保留占位符前缀，会让该引用不可恢复。所有截断入口统一走 `SafeHead`：当截断尾部存在未闭合的 `{{LOOMX_CREDENTIAL` 时，整体回退到占位符起点之前。

### 决策 7：配置与观测都最小化

配置是 Plugin-owned 的 `config.json`，读取失败回退默认值，不阻塞 Router。观测只累计请求数、压缩条数、原始/压缩字符数与异常数，UI 卡片只展示计数与比例，不展示任何工具结果内容。

### 决策 8：token 是估算值，字符是精确值

Router 侧拿不到目标模型的分词器，因此设置页把两类数字分开呈现：字符数为精确累计，token 数按「拉丁字符约 4 字符/token、中日韩与全角约 1.5 字符/token」估算，并在 UI 上标注「估算」。压缩前后用同一口径相减，节省趋势可信；绝对 token 数不声称与 Provider 计费一致。估算在压缩当时按真实文本完成并累计，不事后用字符数反推，避免丢失中英文构成比例。

### 决策 9：统计跨会话累计，落盘节流

统计写入插件自有目录的 `stats.json`，重启后由 Observability 载入续算，否则设置页只能看到单次运行的瞬时值。写入按 10 秒节流，且首次压缩结果立即落盘一次——为计数器引入逐请求 IO 不值得，但也不能让「刚压完就退出」丢掉全部累计。重置统计只清零计数，不动配置。

### 决策 10：插件文案由插件自带，展示信息并入 UI 贡献契约

插件的名称、简介、分类、启停说明与设置页文案放在插件自己的 `i18n/<culture>.json`，随插件负载分发（可直接改），默认文化 `en-US` 额外嵌入程序集兜底。查找与回落规则由契约程序集统一提供（`PluginTextCatalog`），插件不各写一份。

**展示信息不新增独立契约**，而是作为 `IPluginUiContributionProvider` 的默认接口方法 `GetDisplayInfo(PluginUiContext)`：默认实现返回 null，宿主回落到插件 id 与通用文案，纯后台插件无需实现。

理由有三层：

1. 把插件文案写进宿主 `Strings*.resx` 会让「新增一个插件」和「改一种语言」都变成宿主改动，插件不再自包含，第三方插件也无从提供自己的文案。
2. 展示信息与声明式 Contribution 同源——都由插件按 `PluginUiContext.CultureName` 自行解析、宿主只渲染不翻译。为同一件事再开一个接口，等于让宿主多记一条 Provider 类型、多写一处 try/catch、多维护一份失效信号，而插件要实现两个接口才能说清自己是谁。
3. 单个可选方法挂在已有契约上，比新增契约更容易演进：将来要加字段（例如作者、主页、兼容范围）只改 record，不改接口拓扑，也不必让所有插件同步实现。

代价：文案层从「强类型 record + 编译期检查」变成「JSON 键查找」，缺键在运行期才暴露。已用测试守住——凭据保护四语言逐字段非空、键集完全一致、停用语义等关键文案逐语言断言。

**凭据保护同步迁移**：原先它的设置页文案是 446 行 C# 字面量、名称与简介写在宿主 resx 里。本次一并抽到 `plugins/LoomX.CredentialProtection/i18n/*.json`（121 键 × 4 语言），`CredentialSettingsText` 的字段名与全部调用点保持不变，UI 层无感知。宿主 resx 里的 `plugins.credential.*` 与 `PluginsViewModel` 中的插件 id 特判一并删除，两个第一方插件从此走同一条链路。

### 决策 12：插件负载布局全工程统一，冲突只靠分目录解决

`PluginTextCatalog` 的查找顺序只有三步：程序集所在目录的 `i18n/<culture>.json`（正式运行时即 `plugins/<id>/i18n/`）→ 宿主目录的 `plugins/<id>/i18n/<culture>.json`（程序集被摊平时）→ 嵌入程序集的 `en-US`。

最初的做法是在每个候选目录下先找 `i18n/<程序集名>/<culture>.json` 再找 `i18n/<culture>.json`，并让测试工程把 Manifest 改名成 `plugin-manifests/<id>.json`。复核后推翻：这份「分层」只为一个场景存在——测试工程把两个插件编译进同一输出目录。它把运行时契约拆成了两套，代价是测试输出根目录长期躺着一份只属于某个插件的 `plugin.manifest.json`，而没有任何测试会发现；凭据保护那次就是这么被悄悄覆盖掉的。

正确的解法是让各工程复刻运行时布局，而不是让运行时多认一套路径：

- 插件负载**不再声明为 MSBuild `Content`**。`Content + CopyToOutputDirectory` 会经 ProjectReference 传给所有引用方，宿主与测试工程的输出根目录都会出现平铺副本；`ExcludeAssets="content"` 拦不住这条路径。插件工程改用自己的目标 `CopyPluginPayloadToOutput` 拷进自己输出目录。
- 宿主与测试工程再把整个插件目录拷进 `plugins/<id>/`，文件名与相对路径在三处完全一致。
- `PluginTextCatalog` 因此不需要任何按程序集名的特判，`For` 显式接收 `pluginId` 用于摊平场景下的回落。

`LoomX.Tests/Plugins/PluginPayloadLayoutTests.cs` 守住这条边界：输出里必须存在 `plugins/<id>/plugin.manifest.json` 与四语言文案，且根目录不得出现平铺的 `plugin.manifest.json` 或 `i18n/`。

### 决策 11：设置页走声明式 v2，以插件内当前值为准

设置页是 `detail-body` 的 interactive contribution，沿用宿主既有的声明式 UI 协议（节点树 + 命令回传），插件不引用任何 UI 框架。开关类命令以**插件内的当前值取反**，不以宿主回传的勾选值为准：宿主回传的是它渲染那一刻的值，用它当真值会在刷新竞态下把开关按反。

## Risks / Trade-offs

- **上下文漂移**：模型引用的历史行号或片段可能与压缩后的内容不一致。缓解：保留最近 N 条原文、保留头尾、标注省略量，让模型知道内容被裁剪过。
- **启发式误判**：命令输出过滤基于特征行占比与最小匹配数；误判时最坏结果是过度裁剪。缓解：命令过滤器要求最小命中数，且收益不足时整体回退原文。
- **与 Credential Protection 的顺序**：两者互不感知。压缩不改占位符语义（只保护其完整），因此顺序不影响安全边界；Pipeline 顺序仍由 Router 配置决定。
- **中文输出的体积**：反转义后请求正文对中文更省，但会改变上行字节表现（语义等价）。若上游对编码有强假设，可通过禁用该插件规避。
