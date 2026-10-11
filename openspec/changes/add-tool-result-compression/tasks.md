## 1. 插件骨架

- [x] 1.1 新建 `plugins/LoomX.ToolResultCompression` 项目并加入解决方案
- [x] 1.2 编写 `plugin.manifest.json`：request 扩展点、continue-on-error、card-body UI 贡献
- [x] 1.3 实现 Plugin-owned 配置存储（`config.json`，读取失败回退默认）

## 2. 压缩引擎

- [x] 2.1 噪音清理：ANSI 转义、回车、冗余空行与行尾空白
- [x] 2.2 去重：连续重复行折叠为 `×N`，非连续高频行折叠为 `共 N 次`
- [x] 2.3 截断：单行超长截断并标注省略字符数；超行数保留头尾并标注省略行数
- [x] 2.4 分组过滤：git status 按状态分组、git log 只留哈希/作者/标题
- [x] 2.5 分组过滤：git diff 折叠上下文、测试输出只留失败与精简 traceback
- [x] 2.6 分组过滤：长格式目录列表压缩为名称列表与计数
- [x] 2.7 JSON 结构化折叠：数组截断、长字符串截断、超深层级折叠，输出保持合法 JSON
- [x] 2.8 收益判定：无收益或低于 `MinSavingsRatio` 时原样返回
- [x] 2.9 凭据占位符保护：截断边界不得留下半截 `{{LOOMX_CREDENTIAL` 引用

## 3. 请求重写与插件入口

- [x] 3.1 协议适配：OpenAI Chat Completions 与 Anthropic `tool_result`
- [x] 3.2 协议适配：OpenAI Responses `function_call_output` 与 Gemini `functionResponse`
- [x] 3.3 保真窗口：末尾 `KeepRecentToolResults` 条结果保留原文
- [x] 3.4 请求重新序列化使用紧凑且非转义的 JSON 选项
- [x] 3.5 实现 Request Extension：失败放行原文并记录异常
- [x] 3.6 实现 card-body 观测卡片与 zh-Hans / zh-Hant / ja / en 文案

## 4. 宿主接入

- [x] 4.1 `LoomX.csproj` 新增插件 Build/Publish 负载拷贝目标（独立 OutputItemType）
- [x] 4.2 `LoomX.slnx` 与 `LoomX.Tests.csproj` 新增项目引用
- [x] 4.3 `PluginPublishContractTests` 扩展发布契约断言

## 5. partial 拆分（形散而神不散）

- [x] 5.1 `ToolResultCompressor` 拆为编排 / 噪音去重截断 / JSON 折叠 / Git / 命令输出五个 partial 文件
- [x] 5.2 `ToolResultRequestRewriter` 拆为重写编排与槽位定位两个 partial 文件
- [x] 5.3 插件入口拆为生命周期 / UI 快照 / 命令处理三个 partial 文件，Extension 独立成文件
- [x] 5.4 设置页拆为 `CompressionSettingsUi` / `CompressionSettingsText` / `CompressionSettingsCommands`
- [x] 5.5 卡片文案独立为 `ToolResultCompressionUiText`，配置与统计存储各自独立成文件
- [x] 5.6 单个文件控制在 ~250 行内，对外类型名保持不变

## 6. 节省统计与设置页

- [x] 6.1 `CompressionTokenEstimator`：按字符构成估算 token，CJK 与全角按更密口径
- [x] 6.2 重写器累计压缩前后的估算 token，随 report 回传
- [x] 6.3 `CompressionStatsStore`：`stats.json` 持久化累计值与最近记录，落盘 10 秒节流、首次立即写
- [x] 6.4 Observability 挂载存储并在初始化时载入历史累计值
- [x] 6.5 设置页「节省对比」：原始 / 压缩后 / 节省三行，字符精确、token 标注估算
- [x] 6.6 设置页策略区：启用开关、强度预设、保真窗口、收益阈值、三个策略开关、恢复默认
- [x] 6.7 设置页「最近压缩」列表与重置统计（只清计数）
- [x] 6.8 开关命令以插件内当前值为准取反，不信任宿主回传的勾选值
- [x] 6.9 设置页四语文案（zh-Hans / zh-Hant / ja / en）

## 6b. 插件自带本地化（宿主零改动）

- [x] 6b.1 `PluginDisplayInfo` 并入 `IPluginUiContributionProvider` 作为默认方法，不新增独立契约
- [x] 6b.2 `PluginRuntime.GetPluginDisplayInfo` 复用同一 Provider 与同一失效信号
- [x] 6b.3 `PluginsViewModel` 展示文案统一走 `PluginDisplayInfo`，删除凭据保护的 id 特判
- [x] 6b.4 契约程序集新增 `PluginTextCatalog`：查找顺序、文化回落、缺键可见、缓存，仅依赖 BCL
- [x] 6b.5 插件负载布局全工程统一为 `plugins/<id>/`：负载改由插件自有目标拷进插件输出目录，宿主与测试工程整体拷进 `plugins/<id>/`，`PluginTextCatalog` 不做按程序集名分层
- [x] 6b.6 压缩插件自带 `i18n/zh-CN|zh-TW|ja-JP|en-US.json`，随负载分发 + 默认文化嵌入兜底
- [x] 6b.7 删除插件私有 `CompressionLocalization`，改用 `PluginTextCatalog`
- [x] 6b.8 从宿主 `Strings*.resx` 移除全部插件专属文案，并加测试守住这条边界

## 6c. 凭据保护并入同一套本地化链路

- [x] 6c.1 446 行 C# 字面量抽为 `i18n/*.json`（121 键 × 4 语言），字段名与调用点保持不变
- [x] 6c.2 `CredentialSettingsText` / `CredentialProtectionUiText` 改为读 `PluginTextCatalog`
- [x] 6c.3 内建规则名与提示从 8 个 switch 表达式改为资源键，未登记规则不泄漏键名
- [x] 6c.4 凭据保护实现 `GetDisplayInfo`，名称/简介/分类/启停说明全部来自插件资源
- [x] 6c.5 删除宿主 `plugins.credential.*`（4 语言）与 `PluginsViewModel` 的插件 id 常量
- [x] 6c.6 开启 `InternalsVisibleTo`，测试逐字段验证四语言文案迁移未丢内容

## 7. 测试

- [x] 7.1 压缩器策略测试（各策略、占位符保护、收益不足回退）
- [x] 7.2 请求重写器协议与保真窗口测试
- [x] 7.3 插件、观测与 UI 快照测试
- [x] 7.4 经 `PluginRuntime` 动态加载并走 Request Pipeline 的集成测试
- [x] 7.5 设置页命令测试（开关、预设、非法输入、重置、跨重启累计）
- [x] 7.6 经 `PluginRuntime` 转发交互命令并落库的集成测试
- [x] 7.7 本地化归属测试：宿主资源零插件文案、四语言键集一致、跨插件文案不串、未提供文化回落
