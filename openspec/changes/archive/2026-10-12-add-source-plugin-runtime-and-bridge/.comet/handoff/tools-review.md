# 开发工具与 Skill：standard 独立首审

- 日期：2026-10-12（Asia/Shanghai）。
- 范围：task 6.1–6.6，以及 LoomXHost 中对应生产组合根绑定；只读审源与运行验证。
- 结论：**本包首审局部通过；本范围未发现 CRITICAL / IMPORTANT 必修缺陷。** 有 1 项不阻塞的 MINOR 测试证明力建议。**不是整个 change 或 FeedbackBridge 的验收通过。**
- 本次是 tools/Skill 首审；未发生产品修复，后续至多 1 轮修复复查额度未使用。未修改全局或其他包 reviewRounds。
- 未修改产品源码、现有测试源码、Skill、design/spec/tasks；未提交、推送、勾选任务、刷新 Comet 状态、更新索引或拉取分支。只新建本报告和独立运行验证证据。

## 外部依赖边界：FeedbackBridge 保持阻塞

权威现状见 [feedback-review.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/feedback-review.md)：3.1/3.2 的唯一 standard 修复复查已使用，仍存在短 Basic/Bearer 凭据漏检，已停止受影响工作并等待用户授权。本次**不执行该漏检的新探针、不复查其修复、不忽略或关闭该问题、不换 reviewer 重置轮次**。

- test 实际使用 PluginHostBridgeFactory，Skill 又描述 logging/v1 与 notifications/v1，所以工具/Skill 的局部正确性不能转化为这些反馈能力已满足敏感信息拦截要求。
- 本次 Bridge 调用使用合成非敏感摘要，证明接线与正常契约可运行；不构成短凭据过滤守卫的 GREEN。
- 既有最终 Serilog quoting/escaping 与预渲染 512 长度预算的 MINOR，以及 Skill 中预算层级的披露，应由原反馈包在授权后处理；不把它重复创建为本包新问题或声称已修复。
- 不以本报告允许生产敏感 Logging/Toast 放行；整个 change 的联调/验收仍受上述外部阻塞约束。SourceCompiler、Runtime 内部实现、凭据迁移也由各自独立审查负责。

## 权威输入与方法

已阅读工作树 [AGENTS.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/AGENTS.md)、[proposal.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/proposal.md)、[design.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md)、[tasks.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/tasks.md) 和三个 delta spec。以实际源码为优先：重点对齐 design §11/13、plugin-development-skill 全部要求、source-plugin-runtime 的诊断/可信代码边界，以及 plugin-host-bridge 的能力/数据归属要求。

工程已有 CodeGraph，但只读 query PluginDevelopmentService 没有命中，本包新增文件未被现有索引覆盖；因此逐文件与引用交叉审查，未为审查写索引。共享工作树原有大量其他会话修改全部保留。没有使用 git pull、reset、clean、stash 或任何清理其他产物的操作。

## 逐 task 核对

| task | 本包结论 | 审源与实际证据 |
| --- | --- | --- |
| 6.1 稳定六工具与共享应用服务 | 通过 | [PluginDevelopmentTools.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Assistant/PluginDevelopmentTools.cs) 的固定集合为 create、validate、compile、test、reload、diagnostics，均带 loomx.plugin. 前缀。统一 JSON 使用 snake_case；成功与 SafeFail 都保留结构化 PluginDiagnostic。validate/diagnostics 为 Read，其他为 Write；安全参数投影仅 operation，不输出 id、测试正文或其他参数。未知属性、非法类型、过量用例拒绝；取消继续传播。已有工具契约/完整调用链测试在本次重新构建后通过。 |
| 6.2 create 模板 | 通过 | [PluginDevelopmentService.cs:64–90](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentService.cs) 与 [PluginDevelopmentTemplate.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentTemplate.cs) 创建单 id 包目录、合法清单、两份显式 sources、入口/自测试、四语言 i18n，不要求 csproj。已有包拒绝覆盖，文件使用 CreateNew；模板每段路径仍交给生产 resolver。非法 id、入口、版本拒绝且不写包；Demo.PluginTests 同名通过 partial 生成，实测编译与 test 成功。 |
| 6.3 共用生产验证/编译/诊断 | 通过 | [PluginDevelopmentComposition.cs:12–25](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentComposition.cs) 直接绑定 PluginPackageValidation.ValidateAsync / ResolvePackageDirectory / ReadPackageFileAsync 三个公开生产入口；服务不维护平行的 Manifest/能力/文件规则。多错误包一次报告两个缺失 source、不支持能力、禁止 SQLite 依赖。compile 使用同一注册的 SourcePluginCompiler；diagnostics 读取最近安全结果，首次仅 validate，不隐式编译；修改文件后须显式刷新。只返回缓存身份/状态，不回传绝对产物路径。敏感错误字面量、#line 伪造的外部绝对路径及非法 source 路径均有本次实际脱敏探针。 |
| 6.4 隔离 test | 通过，外部反馈依赖仍阻塞 | [PluginDevelopmentService.cs:118–128、156–259](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentService.cs) 用生产安全读取入口复制显式文件到本次临时包；实际启动 PluginRuntime、Pipeline 和 PluginHostBridgeFactory，使用真实 SQLite Bridge 的临时数据根，不接生产 Provider 客户端、核心配置服务、生产 Vault 或桌面 Toast 实例。检查入口/Extension 数量、声明式 UI id/slot 和运行时忽略贡献的诊断；执行用例与公开静态 ValueTask 自测试。现有 Request/UI/SQLite 测试以及新增 Response Pipeline 探针均通过，生产数据根保持未创建、原 Runtime 没有被安装。明确标识 trusted-in-process-temporary-data-not-os-sandbox；不声称限制任意可信 C# 的直接文件/网络访问。 |
| 6.5 生产 reload | 通过 | [PluginDevelopmentService.cs:130–143](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginDevelopmentService.cs) 只对注入 Runtime 中已安装 id 调用 ReloadAsync，不用开发包安装或替代生产路径。原测试验证编译失败保留 1、修复生效 2；独立探针增加同 id 的坏开发包不能影响有效安装包，以及初始化抛含敏感路径的异常时仍保留旧实例并安全输出。原子注册与在途租约正确性不在本包重审；仅核对工具调用生产入口和可观察结果。 |
| 6.6 版本化 Skill 与原样可执行示例 | 通过，外部反馈依赖仍阻塞 | [SKILL.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Skills/plugins/development-v1/SKILL.md) / [manifest.json](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Skills/plugins/development-v1/manifest.json) 一致声明 Skill 1.0.0、Plugin API major 1、runtime-v1；runtime-v1 是 Skill 元数据而非新增 Manifest 字段。公共 SDK、Manifest、Pipeline、UI v2、三类 Bridge、参数化 SQL、事务/释放、禁止依赖、安全与授权、AI 修复循环齐备。现有测试原样提取文档中的 Manifest 和两段 C#，按文字要求填写四语言字典，实际 create→validate→compile→临时 Runtime 加载→Pipeline + SQLite 自测试，准确计 2 项。版本不兼容在执行/写入前拒绝。发布构建输出的 SkillStore 可直接发现本 Skill；源码与两个独立输出中的 SKILL.md SHA256 完全一致。反馈拦截/最终长度预算不在本次放行。 |

### 授权与 trust_code 的准确含义

- test/reload 不传 trust_code 或传 false 时，返回 LX_TRUST_REQUIRED，不执行插件代码；本次还用入口写文件哨兵验证 validate、diagnostics、compile 和未授权 test/reload 均未执行入口。
- Schema、工具说明、Skill 要求用户确认可信进程内 C#，未授权时使用现有 assistant.ask_user，不允许模型自行推定授权；没有新增第二套审批框架。
- 风险标记进入既有 ToolRegistry/AgentLoop 授权路径。既有 AskEachTime 与 AutoApprove 模式继续按原语义工作，工具没有额外不可绕过的人工确认票据。**trust_code 是调用方的显式信任声明，不是能验证真实人工批准的凭证，也不是安全沙箱。** 本报告认可 design 所要求的“沿用既有授权与披露”，不把它说成硬件/操作系统级授权隔离。

## 生产组合根绑定

[LoomXHost.cs:102–104、129、135–143](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/LoomXHost.cs) 实际调用 RegisterServices 与 RegisterTools，并把 DI 中 SourcePluginCompiler 同一实例传给生产 PluginRuntimeOptions.SourceCompiler。服务解析同一个 Runtime、编译器、生产 Validator 三个入口；六工具注册到助手使用的同一 ToolRegistry。SkillStore 仍使用安装目录 Skills，现有 LoomX.csproj 复制规则覆盖新 plugins/development-v1 负载。

独立探针不是仅两次 GetRequiredService 的同对象自比较：它按生产注册形状创建 Runtime，捕获真正传入 Runtime 的编译器，并与应用服务实际持有的编译器/Runtime 比较；执行 create/compile，并从构建产物安装布局读取 Skill。

**验证限度：**没有调用完整 LoomXHost.CreateAsync，因为它会初始化实际用户配置数据库；没有启动生产 GUI 或真实 Provider，没有宣称窗口级工具展示验收、全 change 发布验收或无 SDK 发布子进程验收。这里“工具可见”指实际 ToolRegistry 及安装布局 SkillStore 可发现；宿主调用点由只读审源确认。无 SDK 编译器本体与 Runtime 生命周期由各自包审查承担，本报告不重复审其内部算法。

## 缺陷与最小回归

### CRITICAL / IMPORTANT：0

未发现本范围已证实的必修缺陷，**没有需要产品代码修复的最小回归**。不得为无缺陷首审虚构 RED 或开启一轮修复复查。

### MINOR T-M1：现有组合根测试未直接证明 Runtime/服务编译器相同

位置：[PluginDevelopmentCompositionTests.cs:19–25](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginDevelopmentCompositionTests.cs)。测试注入 fixture.Runtime 后，对同一个 DI SourcePluginCompiler 查询两次执行 Assert.Same，只能证明 Singleton，不能证明生产 Runtime 和开发服务确实共用该编译器，也不能单独发现 LoomXHost 忘记接入工具的问题。源码接线本次正确，且独立更强探针通过，故**不是生产缺陷、非必修、不阻塞首审**。

最小建议：把本次“组合根的Runtime与应用服务共用实际编译器并可发现发布Skill”探针思路纳入正式回归：在 ServiceCollection 中用注册的编译器构造 Runtime，再比较服务实际持有对象，并验证六工具与构建输出 SkillStore。不要把同一个 GetRequiredService 调用自比较当作全接线证据；本次不修改现有测试。

建议保留的定向回归：默认 trust 拒绝且不执行入口；非执行工具无入口副作用；多个独立验证错误；源码敏感字面量/#line 路径隐藏；Request/Response/UI/SQLite 临时测试；同 id 开发坏包不参与 reload；编译/初始化失败保留旧安装实例；Skill 原样代码执行与版本拒绝。已有正式测试和本次证据探针已覆盖这些场景。

## 历史 RED / GREEN 原始核验

读取原始 TRX 的 counters、失败名称和 ErrorInfo，而非只信交付摘要。详情已另存 [TRX核验汇总.json](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/tools-review-20261012/TRX%E6%A0%B8%E9%AA%8C%E6%B1%87%E6%80%BB.json)。

| 历史证据 | 实际结果与采信边界 |
| --- | --- |
| development-tools/CONTRACT-RED.trx | 0 通过 / 2 失败；稳定工具集合为空、Skill 发现为空，均为真实断言失败。 |
| development-tools/BEHAVIOR-RED.trx | 2 通过 / 13 失败；其中 12 项行为/契约断言失败；reload 项是临时夹具清理映射 DLL 时 UnauthorizedAccessException，不能把全部 13 项都说成独立产品行为 RED。 |
| development-tools/BOUNDARY-RED.trx | 3 通过 / 1 失败；PluginTests 同名导致真实编译错误。 |
| development-tools/REGISTRATION-RED.trx | 0 通过 / 1 失败；未声明 Extension 被 Runtime 忽略，旧 test 错误报告成功。 |
| development-tools/CAPABILITY-SUMMARY-RED.trx | 0 通过 / 1 失败；未协商能力原值经 HostCapabilities 摘要回显。 |
| development-tools/FINAL-GREEN.trx | 22 / 22 通过；是历史开发工具集 GREEN，不含后来增加的组合根测试。 |
| tools/COMPOSITION-RED.trx | 0 通过 / 1 失败；生产组合根类型不存在，Assert.NotNull 真实失败。 |
| tools/COMPOSITION-AND-CREDENTIAL-GREEN.trx | 6 / 6 通过；实际只有 1 项组合根测试，加 5 项 PluginRuntimeTests，不是 6 项开发工具测试，更不是短 Basic/Bearer 反馈守卫 GREEN。 |

BASELINE-RED.log 是构建阻断，不计行为 RED；各 ATTEMPT 原始结果仍保留，不能以文件名含 GREEN 就宣称全通过。历史官方 SKILL-VALIDATE.log 内容为 Skill is valid，属于已有元数据验证；本次原样示例执行与发布布局检查是新的运行证据。

## 本次新运行证据

证据目录：[tools-review-20261012](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/tools-review-20261012)。

| 证据 | 新鲜结果 |
| --- | --- |
| TOOLS-REVIEW-GREEN.trx | 正常还原/构建共享 LoomX.Tests 源码到本次独立 artifacts，开发工具测试 **23 / 23 通过**，0 失败、0 跳过。 |
| PRODUCTION-VALIDATOR.trx | 复用本次刚构建的独立 artifacts，而非共享旧 DLL；仅三个生产 Validator 公开入口相关测试 **10 / 10 通过**。 |
| TOOLS-INDEPENDENT-PROBES.trx | 独立证据工程正常构建，**9 / 9 通过**：真实 DI 对象绑定/发布 Skill；#line 脱敏；3 个非法 source 路径；非执行工具入口哨兵；开发包与安装包 reload 区分；初始化失败保留；Response Pipeline 隔离。 |

最新三组共 **42 / 42 通过**，不累计先前保存的 8 项探针快照，也不把它描述为全仓测试。审查输入与 SHA256 记录在审源输入快照.json；正常构建与运行原始输出分别在构建与测试.log、生产Validator验证.log、独立探针.log。

自建探针第一版存在 JavaScript→C# 字符串转义构建错误，已仅修正本次探针并保留“独立探针-首次构建错误-非RED.log”；没有运行测试、不是产品 RED，也不是产品修复复查。

### 可重放命令

以下路径均以本次共享工作树的绝对路径运行；E 为上述新证据目录的绝对路径。

~~~powershell
Set-Location 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge'
$E = 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/tools-review-20261012'
dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path "$E/artifacts" --filter 'FullyQualifiedName~PluginDevelopment' --logger 'trx;LogFileName=TOOLS-REVIEW-GREEN.trx' --results-directory "$E"
# 这一 --no-build 只使用上一命令正常重建的本次独立 artifacts。
dotnet test LoomX.Tests/LoomX.Tests.csproj --artifacts-path "$E/artifacts" --no-build --no-restore --filter 'FullyQualifiedName~PluginPackageValidationTests' --logger 'trx;LogFileName=PRODUCTION-VALIDATOR.trx' --results-directory "$E"
dotnet test "$E/ToolsReview.Probes.csproj" --artifacts-path "$E/probe-artifacts" --logger 'trx;LogFileName=TOOLS-INDEPENDENT-PROBES.trx' --results-directory "$E"
~~~

所有新构建的 bin/obj 都在自有 artifacts / probe-artifacts 中，没有在其他 testhost 使用的共享 bin 上重建，也没有用旧 DLL 冒充源码验证。项目原有非本包构建 warning 不作本包新增缺陷。未打包或清理已有 outputs，未启动应用、真实 SDK 缺失验证子进程或反馈复查。

## 移交

- 本包 task 6.1–6.6 局部首审通过，源码未修；正式勾选/集成由主会话依据全包依赖决定。
- T-M1 可作为后续测试加强建议，不消耗本包修复复查额度。
- 本包后续若确有修复，最多 1 轮定向复查；不得顺带加入 FeedbackBridge 3.1/3.2 的新复查。对反馈的追加修复/定向复查必须先取得用户授权，原 reviewRounds=1 保持不变。

## 共享会话新增产物（未处理）

审查开始/结束状态对比出现下列非本任务创建的未跟踪文件，已保留；内容与归属未纳入本包审查，不应与本次测试证据混合采信：

- [PluginPlayground/file-manifest.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/file-manifest.md)
- [PluginPlayground/handoff.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/handoff.md)
- [credential-review.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/credential-review.md)

已审源输入最终 SHA256 未出现内容漂移（只读边界核验.json）。上述新增项仅说明共享目录仍有并行会话活动，不据此认定污染；不删除、移动、覆盖或替其他会话提交。
