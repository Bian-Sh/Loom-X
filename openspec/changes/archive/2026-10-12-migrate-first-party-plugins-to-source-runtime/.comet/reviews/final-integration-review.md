# Verify 唯一最终集成 standard 独立审查

- 审查日期：2026-10-12（Asia/Shanghai）；同一独立 reviewer，承接 Protection 首审、Runtime 唯一采样复查及 Source/Payload 首审，不开启新修复轮次。
- 唯一工作树：[add-source-plugin-runtime-and-bridge](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)。所有工程/Git/核对命令显式 workdir 此处。
- 完整代码基线：`355f31d3cbd6e57b53cf35be18521a2fa19a4e3a` → 实现提交 `3184a24c4618b3a01ed4cfd2eb9781b5c06e5c3c`。当前 HEAD 为实现提交；已核对真实提交 diff 的 34 个工程、源码与测试文件，并结合正式设计/spec、前段报告和最终证据审查集成边界。
- 核对时上述业务路径相对实现提交的工作区 diff 为空，`git diff --check <基线> <实现>` 退出 0。流程元数据与其它会话/本地证据原样保留。
- 本轮只写本报告：不改实现、不提交/推送/拉取、不清理、不创建工作树、不派代理、不并发 MSBuild/dotnet test。未读取真实设置详情/初始活动树、dump 或原始内存/全栈。未自行操作用户配置或调用 Provider。

## 最终结论

**PASS：在指定完整 diff、当前源码、最终发布包和明确的验证范围内，未发现尚未解决的可复现正确性、安全、兼容或集成缺陷。**

| 严重度 | 当前未解决数量 | 结论 |
| --- | ---: | --- |
| CRITICAL | 0 | 无发现 |
| IMPORTANT | 0 | 无新增未解决发现；历史高频成功日志问题已修复并进入最终包 |
| MINOR | 0 | 无需实现修复的发现 |

此 PASS 是本 change 唯一最终集成独立审查结论，不是“全仓库测试全部通过”、跨平台实跑通过、无编译警告或恶意插件安全沙箱的承诺。

## 权威材料及审查范围

已读取本工作树 AGENTS、正式 proposal/design/tasks、`plugin-host-bridge` 与 `first-party-source-plugins` 两 spec，以及 `add-router-credential-protection` 的权威设计/spec约束。实际代码优先，不以任务勾选或主会话声明代替源码核对。

- [Protection 段及唯一采样复查](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/reviews/protection-runtime-review.md)。保留首审漏识采样问题的历史事实，不把旧 PASS 解释成当时没有该问题。
- [Source/Payload 首审](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/reviews/source-payload-review.md)。本轮收敛其最终相关测试、最新修复后 publish及完整产品UI保留项，不重复全平台已归档实现审计。

## 实际代码与规格闭环

### 1. Protection 公共契约、兼容与能力生命周期

- [PluginBridge.cs:6–17、146–156](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Plugin.Abstractions/PluginBridge.cs)：公共 Protection 只有两个 ValueTask<string> 方法和 CancellationToken；默认接口属性返回 unavailable。旧只实现 Storage/Log/Notifications 的 Bridge 不必加成员，不返回调用输入。没有引入凭据 CRUD、SQL 自动加密或算法/密钥配置。
- [PluginBridgeScope.cs:5–15、30–54、64–74、94–121](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginBridgeScope.cs)：旧五参数公共元数据构造保留，新六参数构造追加 Protection；精确支持 protection/v1，未知版本和声明但缺实现均拒绝。取消、退休、声明检查先于后端调用。能力对象按引用去重释放，不销毁插件数据，释放异常不透传底层敏感消息。
- [PluginProtectionContractTests.cs:10–42](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionContractTests.cs) 和 [PluginProtectionBackendTests.cs:21–110](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionBackendTests.cs)：核对实际契约/旧实现、精确版本、已取消、未声明、退休、安全默认能力和后端测试，而不是仅看名称。

### 2. 历史 DPAPI、安全失败与日志采样

- [PluginProtectionBridge.cs:10–11、31–76](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs)：严格 dpapi:v1:、Windows CurrentUser/null entropy，兼容历史 Vault；与配置保护 dpapi: 分开。非 Windows明确安全失败；非密文、未知版本、Base64/DPAPI/严格UTF8失败均不原样返回、不猜测明文。公开异常无底层 InnerException；日志只保留安全类型/操作/插件标识。finally 清零字节缓冲。
- [同文件:15、55–64、80–85](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs)：成功在取消复查后采样 Debug，仅实例第1次及每64次，Interlocked计数；失败 Error与平台 Warning未被采样吞掉。当前源码指纹与唯一复查相同，未发现其它 IMPORTANT。
- 采样真实 RED 为129次成功调用产生129事件；GREEN11/11。本 reviewer 在前次唯一复查中已纯内存执行当前11用例，并补核顺序/并发129次均3条Debug（1/64/128）、取消不加成功事件、损坏密文安全Error与新实例首条采样。本轮没有冒称重新执行该内存探针；以一致源码指纹、最终相关测试及发布DLL指纹闭环。
- 这是同进程可信插件能力代理，不是不同插件的密码学隔离或恶意代码沙箱；未增加这类承诺。

### 3. 生产组合、SQL归属与历史业务

- [PluginHostBridgeFactory.cs:16–25](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginHostBridgeFactory.cs)、[PlaygroundBridgeFactory.cs:18–23](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/PlaygroundBridgeFactory.cs)：按 manifest.Id 与精确声明创建真实 Protection 后端，不是仅测试接线。
- [LoomXHost.cs:135–149](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomXHost.cs)：正式 Runtime 发现输出 plugins、使用 AppDataPaths 数据根、SourceCompiler及宿主Factory，并接入 ProviderExecutionPipeline。没有改变设置库路径或另造第二份 Vault。
- [CredentialTokenStore.cs:10、20–25、48–81、103–109、127–150、183–184](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialTokenStore.cs) 与 [CredentialProtectionPlugin.cs:37–46](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialProtectionPlugin.cs)：插件保有 credential-tokens.db、原表列、参数化SQL、SHA256 UTF8大写hex、长期Token、冲突回滚/释放后精确查询及销毁流程；唯一业务改动是Storage+Protection组合与入库前/读取后显式await。未引入明文回退。
- 确定性解析、受限归一化、system/developer完整性指令、外围语法保留、普通禁用后的历史恢复、卸载保留Vault及独立销毁确认沿用权威实现；此次diff未改这些业务算法或Runtime排空逻辑。相关全集覆盖既有回归，新增动态测试补核历史库、禁用、重载与退休，不把“旧Scope被拒绝”单项当作所有生命周期场景的替代。

### 4. Source完整性、引用隔离、英文资源与负载

- 两 manifest 删除 assembly/plugin_type，使用 csharp-source/entry_type 与显式顶层sources；凭据12个、压缩22个，均含GlobalUsings。独立逐集合比对其sources与各插件目录顶层.cs全集，完全一致，无重复或遗漏。凭据仅声明storage+protection。
- 两静态项目禁用ImplicitUsings，凭据移除ProtectedData引用；宿主PluginHost承担该依赖。没有新增插件私有SQLite/crypto/宿主私有编译引用。既有 [SourcePluginReferences.targets:4–7](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/SourcePluginReferences.targets) 仍固定为net10.0引用和公共契约，不改为宿主/插件运行目录扫描。
- [FirstPartySourcePayload.targets:1–49](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/FirstPartySourcePayload.targets)：逐文件精确名单；[Host csproj:72–88](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomX.csproj)、[Tests csproj:38–46](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj)、[Playground csproj:26–42](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/LoomX.PluginPlayground.csproj) 共享清单。正式Build/Publish无递归bin/obj复制，不声明插件Content，不靠ExcludeAssets误挡传播。
- [SourcePluginCompiler.cs:129–135](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/SourcePluginCompiler.cs) 使用动态程序集名嵌入英文；[PluginTextCatalog.cs:185–189](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Plugin.Abstractions/PluginTextCatalog.cs) 按同名读取。两静态项目也保留EmbeddedResource。没有把插件文案移入宿主资源。
- 新17项第一方动态测试未单独移除外部英文再测试回落；嵌入/无Location英文回落由既有 SourcePluginCompilerTests:35、SourcePluginCacheTests:298–319及本次真实资源命名核对支持。无遗漏的实现要求，不虚称新增第一方专属fallback用例。

### 5. 动态行为与旧测试夹具迁移

- [FirstPartySourcePluginRuntimeTests.cs:65–85、415–423、526–569](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs)：只暂存实际测试输出镜像；通过正式SourceCompiler/Runtime、独立可回收ALC、LoomX.Source.*名称及cache Location证明非静态伪验收。运行注册两插件的Request/Response扩展及正确失败策略。
- 同文件:88–203覆盖脱敏、指令不污染原请求、外围字符保留、合成历史Vault/哈希/密文不变、损坏密文阻断、普通禁用及重载退休；:205–301覆盖真实动态压缩/最近结果保留、配置/规则跨重载和四语言UI；:303–368使用生产SsePipelineRestoringStream/PipelineRestoringStream、五字节网络块、content/tool arguments跨SSE event及残缺EOF fail closed。异常期待与生产InvalidOperationException边界一致。
- [FirstPartySourceTestPayload.cs:9–52](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourceTestPayload.cs) 只用实际镜像，拒绝rooted/越界/bin/obj。补审helper、PluginRuntimeTests、ToolResultCompressionPluginTests、ToolResultCompressionLocalizationTests四文件：不再复制静态DLL配源码manifest，已接Compiler及所需Storage/Protection。最终689相关全集包含这些旧夹具回归，弥补前段仅有21/21汇总的证据限制。
- [CredentialProtectionBridgeFailureTests.cs:12–47](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialProtectionBridgeFailureTests.cs)：两项能力不可用注入使用静态CredentialEngine/扩展与真实SQLite，证明失败事务不落明文、能力恢复后同库可用、历史映射保留与请求/响应阻断。它们不是动态Runtime注入测试；动态损坏密文阻断另由上述真实源码用例覆盖。Verify映射应保持此证据口径，不能以“真实动态能力不可用注入”扩大描述。

## 最终证据核验及限制

| 证据 | 本 reviewer 实际核对结果与边界 |
| --- | --- |
| [verify-direct-related.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/verify-direct-related.log) | 实现提交后直接默认输出记录689通过、0失败、0跳过；是Plugins与ProviderExecutionPipeline完整相关集合，不是全仓库。最新17项动态/流式和两项Bridge失败均属于该筛选范围。 |
| [a6a3检查日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/checks/a6a3e0e7-444e-453f-8e68-3704fa15d225.log) | 实际680通过、0失败、0跳过；脚本显式排除9项链接夹具，Comet子进程权限拒绝有公开probe。直接689覆盖全部9项，不降低安全断言。Verify复用有效a6a3证据的reused=true属于流程报告记载，不能冒称又跑了一次680。 |
| [playground-final.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/playground-final.log) | 修复后fresh artifacts日志85项检查/85通过/0失败；包含真实源码/历史Vault/Pipeline/重开/退休与敏感哨兵摘要。 |
| [全量中止安全摘要](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/full-test-hang-summary.md) | 全仓库未完成，终止前409通过不能作为全量PASS。观察到ReleaseNotesContentViewModel的Dispatcher同步等待；两个相关生产文件不在本diff。ReleaseHistory隔离14/14通过，因此不能宣称该类单独稳定死锁或已彻底定位根因。未读取dump/原始全栈。 |
| [完整产品UI安全摘要](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/ui-validation.md)及[最新总览截图](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/final-mainwindow-source-plugins.png) | 核对修复后完整MainWindow后台CUA证据及APP专属总览：2插件/2启用/0诊断，两源码可信边界可见。两个详情打开由安全摘要记载，不读取真实设置详情；UI不代替合成业务安全测试，不从透明主题截图推断配色。 |

本轮没有再运行上述dotnet测试或GUI，明确区分“独立读取实际运行证据”和“本轮独立新执行文件/指纹核验”。旧13项GREEN、16/17失败文件、早期26失败及旧筛选失败日志保留历史，不按文件名green认定成功，也不拿旧包证明最终采样修复。

### 本轮独立新执行：最终发布包精确集合及SHA256比对

实际读取 [最终发布目录](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/outputs/source-plugin-migration-2026-10-12_19-11-28)，逐文件比较manifest.sources+manifest+四文化、仓库源文件和共享targets名单：

- credential：12源码+manifest+4语言=17文件；compression：22源码+manifest+4语言=27文件；两个实际目录递归全集与预期集合完全相等。
- 全部44个文件逐一SHA256与仓库一致；因此实际插件目录无旧业务DLL/deps、SQLite托管/原生或crypto依赖。发布根无平铺plugin.manifest.json/i18n。
- 发布PluginHost.dll与当前修复后Release构建DLL的SHA256均为 `4CB86ABF016178A7708D14E01BAF1E2FECB82C3591C1E66DBA3D8DA6F222D858`，不是修复前旧包；与 [final-published-integrity.txt](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/final-published-integrity.txt) 记载一致。

### 平台、覆盖和额度边界

- 当前Windows环境验证DPAPI历史兼容；本轮未实际在Mac/Linux执行，非Windows分支按源码确认安全失败，不承诺Windows密文跨平台可解。
- 全仓库测试仍未完成；此限制如实保留，不因为未改UI而宣称全量通过，也不凭安全摘要把阻塞直接归罪于本change。
- 第三方恶意插件、真实用户Vault/Provider生产请求不是本次验证范围。旧Vault兼容用合成历史表及同用户DPAPI证明，未读取用户真实凭据。
- Runtime修复及原reviewer唯一复查 **1/1已使用**（历史IMPORTANT采样问题已关闭）；Feedback **0/1**。本报告不开启任何新修复轮次。

## 最终指纹

- 基线tree：`29aedcba2d27ec662a807dfaadd5e194b4307215`；实现tree：`c1b4dbac4d8310c63566553cebf5bb938c45f12f`。
- 以下SHA256本轮从实际文件重新计算；核心源码与段审/复查基线一致。

| 文件（绝对链接） | SHA256 |
| --- | --- |
| [PluginBridge.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Plugin.Abstractions/PluginBridge.cs) | `CDB35BCBD5500DD595CEE69CE1279B602B23D2F039ED3BE6AF0D9E1810DDF951` |
| [PluginBridgeScope.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginBridgeScope.cs) | `AAB6D66D6896F0D8F62A764B6FEA54E21113674885AAF393DEE484D33655E2C7` |
| [PluginProtectionBridge.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs) | `CD1BC43B3E518D4A44229BD55D2638AEFF95E3D16A9F3AC4DA19B37320E972FA` |
| [PluginHostBridgeFactory.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginHostBridgeFactory.cs) | `EDE52ECCC83638A866CF75786CC718809BD60374C82CABB2C688E6C58FBD3380` |
| [CredentialTokenStore.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialTokenStore.cs) | `F97E2462BB734C39B8C347C4A791A1F26E4873111B9E5CAFAE10A4FA83807019` |
| [凭据manifest](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/plugin.manifest.json) | `13BD28B60A78273E1B9A290521D00B2E1B34EE8FB65714FE8B59F62AE248BEB0` |
| [压缩manifest](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.ToolResultCompression/plugin.manifest.json) | `91566F61425525749C6B8FD65FB7A5166445BE019AE5EB87E6EF7FDE326BFBD4` |
| [FirstPartySourcePayload.targets](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/FirstPartySourcePayload.targets) | `F5751AF2DCEFD610B61096A5BD9179E2D2782B5EF71C865D9CE03836E862CC2B` |
| [FirstPartySourcePluginRuntimeTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourcePluginRuntimeTests.cs) | `BEC19874D3A8C9024F09244D2CD5CC9DCC52A0B70A0DA0823C5F413092A2384E` |
| [FirstPartySourceTestPayload.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/FirstPartySourceTestPayload.cs) | `15210EF85B4364C27887D2CDC4C6D7B44F41070D4033362922F9323338FE993F` |
| [PluginProtectionBackendTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionBackendTests.cs) | `4660A40119C96EA42616409BBAE773E1992B20A540B7E383BA501C440F2C8531` |
| [CredentialProtectionBridgeFailureTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialProtectionBridgeFailureTests.cs) | `5A9D1E918C43AE34BE745C23C6794AA8168B62D7CA5E4A540E49DD39CBC991BF` |
| [Verify直接相关全集日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/verify-direct-related.log) | `09E900304B0F04F6936592EB67BD813037E5DC9A0BD24CF7C8DE4B871D37D08A` |
| [Playground最终日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/playground-final.log) | `3519850F432A2C2486D410942A085CB2B2050E8233122802A37DCDCBD608F44D` |
| [a6a3日志](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/checks/a6a3e0e7-444e-453f-8e68-3704fa15d225.log) | `369873E9D218099BD73053BED3201E0304D3D1B06135237A0B370753B65C4B23` |
| [最终发布完整性摘要](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/final-published-integrity.txt) | `545A690D8637FC967351A05DFA41EE93572BA0ACBC9730052FE19440B6A67D81` |

**交付结论：PASS；没有待修CRITICAL/IMPORTANT/MINOR。本报告保留所有全量、平台与证据口径限制，不替代主会话的Verify流程记录。**
