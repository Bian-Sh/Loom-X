# Source/Payload 独立 standard 风险段审查

- 时间：2026-10-12 19:05:58 +08:00；同一独立 reviewer，本段首审。HEAD：`355f31d3cbd6e57b53cf35be18521a2fa19a4e3a`。
- 唯一工作树：[add-source-plugin-runtime-and-bridge](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)。全部工程、Git、核对命令显式使用该 workdir。
- 任务：`26e1c0e4-e1b1-443d-a969-6253a8fd4da9`、`154ae18d-77ae-4e44-85d3-b85cd711dd82`、`a2b28ecd-5ac4-47ce-b016-934575f8a876`。
- 范围：两个第一方 source manifest、显式源码/引用/英文资源、共享精确负载及消费者组合、动态行为测试、旧测试夹具迁移。不是 Verify 唯一最终集成审查，不重复全平台已归档实现审计。
- 操作限制：未改业务、未构建/运行 dotnet test、未提交/拉取/清理、未新建工作树、未派子代理；仅新建本报告。纯内存 Roslyn 核验不写任何构建文件。前次 protection-runtime-review.md 保留不变。

## 结论

**PASS：本轮未发现当前 source/payload 风险段内可复现的新安全、兼容或包交付代码缺陷。**

| 严重度 | 数量 | 结论 |
| --- | ---: | --- |
| CRITICAL | 0 | 无发现 |
| IMPORTANT | 0 | 无发现 |
| MINOR | 0 | 无发现 |

这不是“全量已绿”或“最终交付通过”的声明。旧失败日志、已修夹具、当前待复跑证据分别记载，不将未完成验收作为业务缺陷，也不把失败文件名当作通过依据。未触发任何 Runtime/Feedback 修复建议，因此本轮未消耗各自最多一次的定向修复复查额度。

## 已读取约束

读取工作树 [AGENTS.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/AGENTS.md)，本 change 的 [proposal](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/proposal.md)、[design](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/design.md)、[tasks](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/tasks.md)、[first-party-source-plugins spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/first-party-source-plugins/spec.md)、[Protection spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/plugin-host-bridge/spec.md)。沿用前次已读取的 add-router-credential-protection 权威设计约束：原库/长期 token、system/developer 完整性指令、确定性恢复、跨网络块与 SSE、普通禁用仍恢复、卸载保留 Vault 及独立销毁确认。', '',
'## 实际代码核对与精确位置', '',

- [Credential manifest:5–24、29](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/plugin.manifest.json)：csharp-source、正确入口、12 个显式顶层源码，storage.sqlite/v1 + protection/v1；移除 assembly/plugin_type。
- [Compression manifest:5–34](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.ToolResultCompression/plugin.manifest.json)：csharp-source、正确入口、22 个显式顶层源码；原业务 capabilities/extension/UI 未重写。
- 两包 [GlobalUsings.cs:1–8](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/GlobalUsings.cs) / [GlobalUsings.cs:1–8](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.ToolResultCompression/GlobalUsings.cs) 包含静态 SDK 原先提供的基础命名空间；两个 csproj:5 均禁用 ImplicitUsings。凭据 [csproj:15–20](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/LoomX.CredentialProtection.csproj) 和压缩 [csproj:11–16](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.ToolResultCompression/LoomX.ToolResultCompression.csproj) 只依赖公共契约，无插件私有 SQLite/ProtectedData 编译包。
- 独立只读脚本逐一比较：manifest.sources == 插件目录所有顶层 *.cs，且无遗漏/多余项；共享清单 == sources + manifest + 四语言。结果：凭据 12 个源码/17 个负载文件；压缩 22 个源码/27 个负载文件。

### 2. 精确负载、隔离及发布 — PASS
- [FirstPartySourcePayload.targets:4–20、21–47](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/FirstPartySourcePayload.targets)：逐文件显式 Include 和 PackageRelativePath，不使用 Content、通配符、bin/obj 递归；源码项、四语言与 manifest 形状完全一致。
- [Host csproj:72–87](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomX.csproj)、[Tests csproj:38–46](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj)、[Playground csproj:26–41](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/LoomX.PluginPlayground.csproj) 共用该表，目的地分别为各插件 plugins/<id>/；Host/Playground Build 和 Publish 都只 Copy 精确项，不复制静态业务 DLL/deps/crypto/native 依赖。静态项目仅留作开发与单测，不是产品动态负载。
- [PluginPayloadLayoutTests.cs:102–159](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginPayloadLayoutTests.cs) 对实际镜像的完整文件集合、逐文件字节、共享清单与所有顶层源码作断言；[PluginPublishContractTests.cs:73–135](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginPublishContractTests.cs) 守住插件程序集不引用宿主实现、消费者无 bin 递归、插件显式负载与英文资源。
- 本轮直接读取绿色侧车 Host/Tests 的两个包，逐文件集合+SHA256 与当前仓库一致，根目录没有平铺 plugin.manifest.json/i18n。侧车没有 Playground 输出，未虚称已经实跑该输出；其接线作源码检查。
- 用户补充正式发布后，本轮只读检查 [outputs/source-plugin-migration-2026-10-12_18-57-37](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/source-plugin-migration-2026-10-12_18-57-37) 中真实文件：凭据严格 17 项、压缩严格 27 项，全部字节匹配当前源文件，插件目录无额外文件/二进制依赖，发布根无平铺 manifest/i18n。此为本段包形状核对，不代替最终产品集成验收。

### 3. 英文资源及生产组合 — PASS
- [SourcePluginCompiler.cs:122–135](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/SourcePluginCompiler.cs) 已有正式路径直接使用 sources、不注入 SDK using，固定公共引用集，并将 i18n/en-US.json 嵌入为动态 AssemblyName.i18n.en-US.json。两包均提供精确英文项，未另创资源命名特判。
- [PluginTextCatalog.cs:134–136、164–171、183–192](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Plugin.Abstractions/PluginTextCatalog.cs) 从程序集目录/host plugins/<id> 查文案，英文回落按实际程序集名解析。静态项目自己的 EmbeddedResource LogicalName 仍匹配原静态名；动态名由正式编译器处理。
- [LoomXHost.cs:135–149](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomXHost.cs) 实际生产 Runtime 使用输出 plugins、AppDataPaths 插件数据根、SourceCompiler 与 PluginHostBridgeFactory，ProviderExecutionPipeline 接入该 Runtime；不直接实例化静态插件。
- [PluginHostBridgeFactory.cs:16–25](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginHostBridgeFactory.cs) 和 [PlaygroundBridgeFactory.cs:19–23](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/PlaygroundBridgeFactory.cs) 已接真实 storage/protection。Playground 自测消费实际输出凭据包，保持历史 Vault 夹具；此程序还有演示 source 插件，不误把它认作压缩插件验收。

### 4. 动态测试、Vault 与生命周期 — PASS（证据计数限定见后文）
- [FirstPartySourcePluginRuntimeTests.cs:526–569](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs)：RuntimePackage 只从 AppContext.BaseDirectory/plugins/<id> 暂存显式 sources 和四语言，无仓库源码回退、静态 DLL 暂存或用户真实数据读取。使用正式 SourcePluginCompiler、PluginRuntime、真实 SQLite/Protection Bridge。
- [同文件:65–85、400–423](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs)：运行种类、扩展注册、公共接口类型、不同于 Default 的 collectible ALC、LoomX.Source 名称及 cache 下程序集 Location 都有断言。不是静态副本伪证明。
- [同文件:88–203](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs)：请求脱敏/完整性指令/外围字符、合成历史 Vault 与 hash/ciphertext 不变、损坏密文阻断、普通禁用仍历史恢复、重载退休旧 Bridge 及 token 复用。合成数据路径在新临时根，不动用户 Vault。
- [同文件:205–301](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs)：动态压缩保留最近结果、config.json 插件目录持久化及重载生效、凭据 rules.json 新前缀跨重载参与真实请求、两插件四语言 display/UI。
- [同文件:303–368](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs)：直接使用生产 SsePipelineRestoringStream/PipelineRestoringStream，TinyChunkStream 限制五字节，分别覆盖 content/tool arguments 跨 SSE event、普通响应跨网络块及残缺 token EOF 阻断。当前 :357 期待 InvalidOperationException，与生产 [ProviderExecutionPipeline.cs:511–515](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/ProviderExecutionPipeline.cs) 一致，未为配合测试改安全实现。
- 两插件业务 diff 仍仅含前次批准的 TokenStore/初始化保护移交；压缩业务、旧库路径/表列/哈希、placeholder/恢复算法、插件普通启停/卸载语义没有因源码化被重写。

## 补审：helper + 三个旧测试类

1. [FirstPartySourceTestPayload.cs:9–52](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourceTestPayload.cs)：只读真实测试输出，解析 manifest 并确认 csharp-source；仅暂存 sources/manifest/四语言，不复制静态 DLL/deps，也不回退仓库。Resolve 拒绝 rooted、越界和 bin/obj 路径。
2. [PluginRuntimeTests.cs:33–47](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginRuntimeTests.cs)：旧 StageCredentialProtectionPlugin 改用 helper；启用 SourceCompiler 和按 manifest 绑定的真实 Storage/Protection。ALC/公共接口及原 Pipeline 行为断言仍保留，没有为了迁移删除失败保护断言。
3. [ToolResultCompressionPluginTests.cs:155–180、208–220](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/ToolResultCompressionPluginTests.cs)：两个 Runtime 入口改用实际镜像源码和 SourceCompiler；压缩和交互配置持久化仍真实走 Runtime。静态类型只用于原单测/常量，不再用于动态负载复制。
4. [ToolResultCompressionLocalizationTests.cs:50–68、132–168](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/ToolResultCompressionLocalizationTests.cs)：Runtime 配置真实 compiler；本地化负载断言转向共享显式清单及镜像文件，不要求已删除递归 glob。暂存由 helper 负责，旧文案/UI 内容断言未削弱。
- 当前四文件未发现仍存在的 source-manifest/DLL 接线不匹配。旧失败可解释到此前旧夹具，但不据此宣称本轮已执行其定向测试。

## 独立只读核验与实际证据

### 本轮新执行的只读核验
- 集合/字节校验：两个 manifest、顶层源码、共享 targets；绿色侧车 Host/Tests 镜像；新正式 publish 镜像。命令退出码均为 0。
- 使用 PowerShell 自带 Roslyn，仅读取实际侧车源码和 net10.0/10.0.9 framework references + 构建的 LoomX.Plugin.Abstractions；不加隐式 global using，不引用 Host/SQLite/ProtectedData。两包分别在 MemoryStream 编译成功，实际程序集引用仅为 System.* + 公共契约；无 SQLite、ProtectedData、LoomX.PluginHost、LoomX 宿主引用。
- 按正式编译器已有 ResourceDescription 命名构造英文资源，并从内存程序集取出验证与包内 en-US.json 字节相同。这个补核验证 manifest/reference/resource 可用性，不冒称重新执行正式 SourcePluginCompiler 缓存流水线。
- 没有并发 dotnet build/test，没有写 bin/obj、暂存包、cache 或日志。

### 自行读取的证据与不能混淆的事实
- [first-party-source-red.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/first-party-source-red.log)：旧 assembly 负载 13 失败/0 通过；[first-party-source-green.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/first-party-source-green.log)：实际侧车源码动态测试 13 通过/0 失败。已读摘要与栈，不仅依赖主会话口头声称。
- [first-party-source-stream-green.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/first-party-source-stream-green.log) 实际是补流测试初轮 16 通过/1 失败：期待 InvalidDataException、实际 InvalidOperationException；当前断言已修正。此文件不是 17/17 通过证据。
- [checks/55bf8bfe-88e3-4cc9-9d1f-634b2c307d49.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/checks/55bf8bfe-88e3-4cc9-9d1f-634b2c307d49.log)：Plugins 旧独立 artifacts 输出结果 668 通过/26 失败/总计 694。含旧 Runtime/Compression 夹具、View 固定目录假设、链接创建失败等，不能标为全集通过。
- 用户报告修正后四文件相关 21/21 两次通过；[legacy-fixture-checkpoint.json](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/legacy-fixture-checkpoint.json) 记录了 21/21 状态。当前允许目录中没有定位到这两次完整独立原始日志，因此只记为用户补充 + 流程记录，不虚称已独立读取/复跑 21 项；最终 Verify 宜收齐准确命令/两次输出以支持证据链。
- [direct-link-regression.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/direct-link-regression.log) 有 9/9 通过摘要；[checks/b23a899f-6426-4cae-879b-f461975b3eb3.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/checks/b23a899f-6426-4cae-879b-f461975b3eb3.log) 有同一 Comet 环境 FILE_SYMLINK_FAIL/DIR_SYMLINK_FAIL/拒绝访问/JUNCTION_EXIT=1。未把环境的创建权限拒绝当作安全代码缺陷，也未修改安全代码或重审已归档路径边界平台。
- [desktop-publish.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/desktop-publish.log)、[published-layout-check.txt](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/published-layout-check.txt) 已产生；本轮已独立比对真实发布包 17/27 精确文件。
- 主会话报告完整 MainWindow 后台产品 CUA 中两源码插件、两启用、零诊断及可打开两详情；本段不独立操作 GUI、不以截图推断透明主题颜色、不将这份段审扩为最终集成审查。

## 规格覆盖状态与最终 Verify 保留项（非本段缺陷）

- 当前源码+动态测试覆盖：显式源码/入口/引用，真实编译与注册，历史 Vault、损坏密文、普通禁用、重载退休、压缩配置/本地化、跨网络块/SSE 和 EOF fail closed。负载清单及实际发布 17/27 已核对。
- 英文嵌入/丢外部资源回落由原 SourcePluginCompilerTests:35 与 SourcePluginCacheTests:298–319 泛化测试守护；新增第一方 17 项没有独立“移除外部英文文件后的 fallback”用例。本轮资源/命名源码及内存字节核验支持其兼容，没有发现资源实现缺失；不是重复审计平台的理由。
- 本轮第一方动态重载测试证明旧 Protection Scope 被拒绝，但没有另建“活跃请求排空时重载/卸载”的第一方专属用例；这段没有修改已归档 Runtime 排空实现。卸载保留 Vault/独立销毁确认仍沿用原权威实现与既有回归，不能把新的源码重载一项当作所有生命周期验收的替代。
- 最终集成需要收敛当前 17 项（含异常期待修正）、四文件 21 项两遍原始证据、默认路径 Plugins/相关全量结果及平台权限差异说明；结合正式 publish 和完整产品 CUA 证据再作唯一最终审查。本段没有假定这些已经全部完成。

## 复查基线

保留前次 Protection 段报告和本报告；以下记录当前文件 SHA256，便于后续只对差异定向复核，不重新打开已归档平台审计。

| 文件 | SHA256 |
| --- | --- |
| [`FirstPartySourcePayload.targets`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/FirstPartySourcePayload.targets) | `F5751AF2DCEFD610B61096A5BD9179E2D2782B5EF71C865D9CE03836E862CC2B` |
| [`LoomX/LoomX.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomX.csproj) | `160BD6F747875968BB1C69908CC0EFFD6DD8DBEF6F93792533C0DC9377FEF2EA` |
| [`LoomX.Tests/LoomX.Tests.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj) | `B9B069B2E2B280E46F6FC12DB0F16FAD2DFC3AA1CF92A0D03ED5B0EB335A25EE` |
| [`LoomX.PluginPlayground/LoomX.PluginPlayground.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/LoomX.PluginPlayground.csproj) | `77A975628805576DA8218220C69FDF6EF7039943F222E9ACF662DBE5D4864A24` |
| [`LoomX.PluginPlayground/Program.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/Program.cs) | `F66FEB7123E3E1CE64354BE9488D7946C5197ECBB55220104996883EFE3ACEA1` |
| [`plugins/LoomX.CredentialProtection/plugin.manifest.json`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/plugin.manifest.json) | `13BD28B60A78273E1B9A290521D00B2E1B34EE8FB65714FE8B59F62AE248BEB0` |
| [`plugins/LoomX.CredentialProtection/LoomX.CredentialProtection.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/LoomX.CredentialProtection.csproj) | `03B213DBA9071B11E5DB7DDE28FE076964CB89B57606417F9B4E745969E08916` |
| [`plugins/LoomX.CredentialProtection/GlobalUsings.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/GlobalUsings.cs) | `3047CDBE30742395889C950377B47CF8F84CDFB96AB7E38233BA35901D9D7220` |
| [`plugins/LoomX.ToolResultCompression/plugin.manifest.json`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.ToolResultCompression/plugin.manifest.json) | `91566F61425525749C6B8FD65FB7A5166445BE019AE5EB87E6EF7FDE326BFBD4` |
| [`plugins/LoomX.ToolResultCompression/LoomX.ToolResultCompression.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.ToolResultCompression/LoomX.ToolResultCompression.csproj) | `290FBF40E698CFC6EB7ABAAC92FF0F57BDA052750C44466AED16627177F2C4ED` |
| [`plugins/LoomX.ToolResultCompression/GlobalUsings.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.ToolResultCompression/GlobalUsings.cs) | `3047CDBE30742395889C950377B47CF8F84CDFB96AB7E38233BA35901D9D7220` |
| [`LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs) | `A81836BC1E50B4019AFC2B120C0DC4312FC43DB8F1537E533CBC423AB20501D3` |
| [`LoomX.Tests/Plugins/PluginPayloadLayoutTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginPayloadLayoutTests.cs) | `8892B22045B3C7AB7EF2D8746A6FE1E15BEFE3657332E626CA1402547CC216DA` |
| [`LoomX.Tests/Plugins/PluginPublishContractTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginPublishContractTests.cs) | `4F24B1700BBE121428500BE68A30220DA12FDF0B9460F9550FA463018B2A19A9` |
| [`LoomX.Tests/Plugins/FirstPartySourceTestPayload.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourceTestPayload.cs) | `15210EF85B4364C27887D2CDC4C6D7B44F41070D4033362922F9323338FE993F` |
| [`LoomX.Tests/Plugins/PluginRuntimeTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginRuntimeTests.cs) | `31934475D3AB7D644FCA7F1AE43E9A01C1D189CD14AB72D78768EF09E92E9B94` |
| [`LoomX.Tests/Plugins/ToolResultCompressionPluginTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/ToolResultCompressionPluginTests.cs) | `238C8BAFAC11A5DA3BFF6343E9AD4116929FD21C5AEB4478BC48974224A6D889` |
| [`LoomX.Tests/Plugins/ToolResultCompressionLocalizationTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/ToolResultCompressionLocalizationTests.cs) | `DA3D9FE32DFADDF4FE9C23890907B2DDAACE469519EC30D716E7650A6DFADA1B` |

## 收尾状态：Runtime 采样修复后的边界

- 时间：2026-10-12 19:09:31 +08:00。本 source/payload 段首审仍为 PASS；四份旧夹具补审已完成。
- 原 reviewer 已在 [protection-runtime-review.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/reviews/protection-runtime-review.md) 追加高频日志 IMPORTANT 的一次修复复查：当前 11/11 独立内存执行及顺序/并发采样、失败日志、取消、敏感哨兵补核通过；Runtime 额度已用 1/1，不重开 source/payload 首审。
- 本报告先前未消耗 Runtime 修复额度的描述是该段写入时的历史状态，由本条更新为 1/1 已使用。
- 已核对的旧发布目录仍是 source 包形状证据，保留不删；但采样修复发生在该发布之后，最终发布/GUI及唯一最终集成审查必须引用重新打包的新产物与最终证据，不得用旧产物证明新宿主修复已发布。
