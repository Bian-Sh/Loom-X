# 第一方源码插件与 Protection Bridge：full Verify

- change：`migrate-first-party-plugins-to-source-runtime`
- 模式：full / autonomous / TDD / standard 独立审查。
- 唯一工作树：D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge。
- 基线：`355f31d3cbd6e57b53cf35be18521a2fa19a4e3a`；实现提交：`3184a24c4618b3a01ed4cfd2eb9781b5c06e5c3c`。
- 设计权威：Comet配置的change/design.md；计划：docs/superpowers/plans/2026-10-12-migrate-first-party-plugins-to-source-runtime.md。未另设重复的Superpowers设计文档。
- OpenSpec验证通过；tasks权威8/8，apply状态all_done；Build guard 14/14通过。

## 汇总

| 维度 | 状态 |
| --- | --- |
| 完整性 | 8/8任务，7项Requirement、14个Scenario均有实现和覆盖依据 |
| 正确性 | Windows历史密文与SQL/Token兼容、动态Pipeline与失效边界、源码包精确布局通过 |
| 一致性 | 与批准薄Protection和源码迁移设计一致；无新增后端、Vault CRUD、SQL自动加密或安全沙箱承诺 |
| 最终独立集成审查 | PASS：本阶段唯一最终standard独立审查；未解决CRITICAL/IMPORTANT/MINOR均为0 |
| 全仓库测试 | 未完成：全量UI Dispatcher阻塞，中止而非通过；本change完整相关测试通过 |

## 规格逐项映射

| Requirement / Scenario | 实现位置 | 检查与结论 |
| --- | --- | --- |
| 最小宿主能力：插件组合存储与保护 | PluginBridge.cs、PluginProtectionBridge.cs、CredentialTokenStore.cs | 公共契约只有两个ValueTask字符串方法，Storage显式SQL；ProtectionBackend往返、CredentialStorage与BridgeFailure测试PASS |
| 最小宿主能力：不受支持平台 | PluginProtectionBridge.Protect的平台分支 | 非Windows直接CapabilityNotSupported，源码独立审查确认无明文回退；当前Windows机器不能冒称实际执行Mac/Linux。能力不可用注入由静态CredentialEngine/扩展＋真实SQLite验证请求/响应阻断；动态损坏密文另由源码Runtime用例覆盖 |
| 授权与生命周期：旧Bridge | IPluginBridge默认Protection、PluginBridgeScope旧五参数构造 | PluginProtectionContract、PluginBridgeContract、既有反射构造测试PASS；旧实现可编译 |
| 授权与生命周期：未声明/退休 | PluginCapabilityCatalog与Scope.Check、CapabilityProxy | protection/v1精确版本、未知版本/未声明/退休拒绝；相关Scope及动态重载测试PASS |
| 授权与生命周期：取消 | Scope.Check与Protection.Protect | 已取消调用不执行后端、真实后端取消断言PASS |
| 失败不泄密：历史密文 | dpapi:v1:/CurrentUser/null entropy | DPAPI历史数据及动态Vault直接复用测试PASS；不涉及配置保护dpapi:回退 |
| 失败不泄密：非密文/损坏密文 | 严格前缀/Base64/DPAPI/UTF8和安全异常 | 未知/损坏/非密文拒绝，不返原值；公开异常无InnerException，敏感canary日志断言PASS；成功仅采样Debug |
| 源码Runtime：编译并注册两个插件 | 两manifest/GlobalUsings、既有SourcePluginCompiler/PluginRuntime | 实际csharp-source包动态编译、独立ALC、Request/Response扩展注册PASS；不以静态单测替代 |
| 源码Runtime：重载实例 | 动态Runtime配合Scope退休 | 历史Vault、禁用语义、旧Bridge退休、压缩/凭据配置跨重载测试PASS；原Runtime卸载/排空相关回归PASS |
| 负载一致：正式发布 | FirstPartySourcePayload.targets与三个消费者目标 | 新包17/27精确文件；manifest、显式源码、四语言；无旧DLL/deps/SQLite/crypto、无根平铺文件；布局与发布契约PASS |
| 凭据兼容：历史映射 | CredentialTokenStore的原文件/表/哈希/事务 | 历史Token和dpapi:v1:直接恢复，无第二库；唯一冲突完成失败事务后精确查询；StorageMigration/StorageBridge PASS |
| 凭据兼容：暂停主动保护 | 既有CredentialProtectionPlugin生命周期 | 普通禁用只停新检测，历史system/developer完整性指令与恢复继续；动态禁用/重载及Lifecycle测试PASS |
| 凭据兼容：存储或解密失败 | Bridge安全失败、Plugin请求响应失败策略 | 加密不可用不落明文、事务释放；解密不可用保留历史映射并阻断；真实生产普通流/SSE流跨5字节网络块及跨event、tool arguments与残缺EOF fail closed PASS |
| 压缩兼容：真实源码压缩 | ToolResultCompression原业务与源码包 | 动态请求折叠/保留最近结果、配置UI、四语言、continue-on-error与本地化回归PASS |

既有凭据归一化、完整性指令、卸载保留Vault/独立销毁确认未改算法或业务；其相关全集继续通过。源码可信代码披露在完整产品两卡片可见，Bridge/ALC不宣称OS沙箱。

## 实际执行与证据

证据以归档前后同一change的`.comet/`为根，归档后随change保留：

1. 直接默认测试输出：`dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter (Plugins|ProviderExecutionPipelineTests)`实际完整筛选，689/689、0失败、0跳过；`evidence/related-final.log`。
2. Comet执行680/680：`check run ... build --local -- pwsh -NoProfile -File .../verify-related.ps1`。`checks/a6a3e0e7-444e-453f-8e68-3704fa15d225.log`为实现提交后新证据，Build guard真实复用通过；早期`c956...`等保留历史。
3. 同用户正常直接运行全部9项链接夹具PASS，已纳入689全集，并有`direct-link-regression.log`。Comet子进程创建symlink/Junction均被权限拒绝，用公开CLI探针记录`checks/b23a899f-6426-4cae-879b-f461975b3eb3.log`；Runtime脚本明确排除9项，不降低安全断言、不宣称680等于全仓库。
4. 独立fresh artifacts Playground 85/85；`evidence/playground-source.log`。
5. 真实TDD：Protection契约/后端RED；动态源码旧assembly声明13/13 RED→13/13 GREEN；布局4项RED→18项GREEN；日志采样129事件RED→11项GREEN。流式测试首轮异常类型预期不符已按实际生产栈纠正，不冒称业务RED。
6. Windows自包含正式发布：`outputs/source-plugin-migration-2026-10-12_19-11-28/LoomX.exe`；`final-desktop-publish.log`、`final-published-layout-check.txt`。旧包保留，最终包含日志采样修复。
7. 新包完整MainWindow后台CUA：2插件/2启用/0诊断，两个详情可打开；只导航，不编辑真实配置、规则或开关、不调用外部Provider；`evidence/ui-validation.md`与安全总览`final-mainwindow-source-plugins.png`。透明主题截图不据此判断配色。
8. `git diff --cached --check`通过。存在既存编译/分析器与行尾警告，不宣称零警告。
9. Verify Runtime成功核验并复用相同cwd/命令/输入的有效测试证据a6a3（reused=true，tier=full），未冒称是一次新运行、未用手工record-check代替运行。另在本阶段直接默认输出重新执行完整相关全集689/689（0失败0跳过），`evidence/verify-direct-related.log`；修复后fresh artifacts再次Playground85/85，`evidence/playground-final.log`。`final-published-integrity.txt`逐文件SHA256确认两负载与仓库源码一致，发布PluginHost.dll与修复后Release构建一致。

## 全量与平台限制

- 全仓库默认输出尝试长期停滞，在ReleaseNotesContentViewModel.RunOnUiThread第172行同步等待Dispatcher，经构造49行、ReleaseHistoryViewModel93行进入xUnit上下文。两个生产文件本次均未改；只终止已核对本次testhost。退出1，中止前409通过，不能视为全量通过。
- 单独ReleaseHistoryViewModelTests加35s无dump超时，14/14通过。证据只能支持全量集成时观察到UI调度阻塞，不能说单类稳定复现，也未根治其初始化/跨测试交互。
- 详见`evidence/full-test-hang-summary.md`。dotnet_debug_mcp未配置，使用dotnet-dump底层栈；dump与原始栈可能有内存数据，仅本地ignored目录保留，不提交。
- 未实现、未实际执行Mac/Linux后端，不承诺Windows历史密文跨平台解；本次Windows产品验收不替代跨平台验收。

## 安全提交边界与审查

分段首审和唯一Runtime采样修复定向复查均PASS，Runtime修复额度1已用、Feedback修复额度0。最终集成审查只在Verify执行，不重复无限首审。真实设置树/截图、初始活动内容、dump/原始全栈、payload-artifacts、outputs、CodeGraph和既存LoomX.PluginPlayground/evidence/均不提交，不删除或清理。

## 最终结论

**PASS（限定本change相关范围）**。Runtime实际检查有效，最新完整相关测试689/689、Playground85/85，最新发布包及完整产品UI通过，唯一最终独立集成审查PASS且未解决CRITICAL/IMPORTANT/MINOR均为0。允许继续已授权归档与当前分支推送；不宣称全仓库全绿、跨平台实跑或零警告。最终审查见change的`.comet/reviews/final-integration-review.md`。

### 证据口径补充

- 已按最终reviewer核对纠正能力不可用注入的口径：两项BridgeFailure测试是静态CredentialEngine/扩展＋真实SQLite，不是动态Runtime注入。动态安全失败由损坏历史密文和生产流包装器测试另证。此项仅修正Verify报告，没有新增实现修复轮次。
- 新17项动态测试未独立移除外部英文再测第一方回落；英文嵌入/无Location回落由既有SourcePluginCompilerTests、SourcePluginCacheTests与实际动态资源命名审查支持，均在相关全集。
