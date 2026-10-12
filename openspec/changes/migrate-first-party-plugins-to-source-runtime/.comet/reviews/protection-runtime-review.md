# Protection Runtime 独立 standard 风险段审查

- 时间：2026-10-12 18:45:22 +08:00。轮次：首审。
- 工作树：[指定工作树](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge)。HEAD：`355f31d3cbd6e57b53cf35be18521a2fa19a4e3a`。
- 任务：`ddb0af83-e457-40da-940e-c5339e893511`、`4e3aeb40-0929-4a21-89fc-d5b21ad89279`、`9271a759-7110-47cf-99f6-51cb56e7b59b`。
- 审查边界：当前未提交的 Protection 公共契约、Scope、宿主保护实现、生产/Playground 组合、CredentialTokenStore 和初始化、测试接线及新增 PluginProtection 测试。
- 未实现功能、未提交/拉取/推送、未新建工作树、未清理、未派子代理；仅创建本报告目录并写本文件。现有 CodeGraph 只读查询，不重建索引；实际源码和 diff 优先。

## 结论

**PASS：本轮审查未发现三个指定任务内可复现的新安全或兼容缺陷。**

| 严重度 | 数量 | 结论 |
| --- | ---: | --- |
| CRITICAL | 0 | 无发现 |
| IMPORTANT | 0 | 无发现 |
| MINOR | 0 | 无发现 |

此结论不是整个源码迁移 change 的完成/发布验收结论。后续源码负载、manifest、动态 Runtime、发布布局与完整产品验收仍由主会话处理；没有将这些尚未完成的后续工作计为本风险段缺陷。

## 已读取的约束

- [工作树 AGENTS.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/AGENTS.md)：数据库统一路径、日志敏感信息边界、插件布局与凭据生命周期。
- [新 change 设计](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/design.md)、[任务](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/tasks.md)、[Protection 规范](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/plugin-host-bridge/spec.md)、[源码插件规范](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/specs/first-party-source-plugins/spec.md)。
- [Credential Protection 权威设计](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/design.md)、[凭据规范](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/specs/credential-protection/spec.md)、[Pipeline 规范](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-router-credential-protection/specs/plugin-runtime-pipeline/spec.md)。

确认已批准的边界为薄 Protection 两个异步字符串方法；不是宿主 Vault CRUD、自动 SQL 加密、跨平台密文迁移或同进程恶意代码安全沙箱。

## 实际源码核对位置与依据

1. **公共兼容性**：[PluginBridge.cs:9–17、146–156](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Plugin.Abstractions/PluginBridge.cs)。接口只新增 EncryptAsync/DecryptAsync 和 CancellationToken；旧 IPluginBridge 实现通过默认接口属性获得 unavailable，不返回原输入。默认调用返回稳定 CapabilityNotSupported，取消令牌返回取消。
2. **Scope 元数据、授权和退休**：[PluginBridgeScope.cs:30–54、64–74、94–121](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginBridgeScope.cs)。旧五参数构造保留，新参数经单独六参数构造接线；精确注册 protection/v1，拒绝未知版本和声明但无实现；先检查取消/退休/声明再调用后端；共享对象按引用去重释放，释放失败不透传底层异常。
3. **Windows 历史密文和安全失败**：[PluginProtectionBridge.cs:10–11、30–76](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs)。格式严格为 dpapi:v1:，Protect/Unprotect 均为 CurrentUser/null entropy；未使用配置保护的 dpapi: 回退逻辑。非 Windows 明确 CapabilityNotSupported；非密文、未知版本、Base64/DPAPI/UTF8 失败不原样返回。日志仅有插件标识、操作和异常类型，公开异常无底层 InnerException，临时字节缓冲在 finally 清零。
4. **实际宿主组合**：[PluginHostBridgeFactory.cs:16–25](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginHostBridgeFactory.cs) 与 [PlaygroundBridgeFactory.cs:18–23](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/PlaygroundBridgeFactory.cs)。两个组合点均按精确能力声明创建真实 PluginProtectionBridge，并使用 manifest.Id 绑定不可变上下文。此绑定不代表不同插件间 DPAPI 密码学隔离，符合批准的非沙箱约束。
5. **SQL/Vault 归属与事务**：[CredentialTokenStore.cs:10、20–25、48–81、103–109、125–145、183–184](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialTokenStore.cs)。文件名、表列、参数化 SQL、SHA256 UTF8 大写 hex、去重和唯一约束冲突后的回滚/释放/精确查询未改变。只有入库前 Protect、读取后 Unprotect 被替换为显式 await Protection；没有自行创建第二库或使用 plaintext fallback。
6. **初始化与失败传播**：[CredentialProtectionPlugin.cs:37–46、273–277、429–432](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialProtectionPlugin.cs)。初始化使用 context.Bridge.Storage/Protection，不另造后端。Protection 异常沿 Store/Engine 传播到既有 request/response fail-closed 分支，返回安全诊断和空 payload，而非放行原正文。
7. **测试接线**：[CredentialTestFactory.cs:24–30、61–66](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialTestFactory.cs) 接入真实宿主保护；[CredentialStorageMigrationTests.cs:11–16](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialStorageMigrationTests.cs) 检查双契约构造和插件不直接引用 ProtectedData；[CredentialStorageBridgeTests.cs:64–81、84–130、150–195、197–237](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialStorageBridgeTests.cs) 保留真实历史 Vault、哈希/密文不变、冲突顺序、取消、存储失败与损坏密文阻断的断言。
8. **新增测试**：[PluginProtectionContractTests.cs:10–42](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionContractTests.cs) 和 [PluginProtectionBackendTests.cs:21–96](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionBackendTests.cs) 的实际源码已读取，并独立执行全部 10 个用例，见下一节。

## 独立核验与证据限制

### 本轮新执行：纯内存源码核验 10/10

- 为遵守其它文件只读，未运行 MSBuild、dotnet test、CredentialStorageBridgeTests 等会产生 bin/obj、临时数据库或日志的流程。
- 在指定工作树启动 PowerShell 7.6.5/.NET 10.0.11，使用其 Roslyn API 读取当前 PluginBridge.cs、PluginBridgeScope.cs、PluginProtectionBridge.cs 及两个 PluginProtection 测试源码；补上项目的常用隐式 global using；引用现有 framework/xUnit/Logging.Abstractions/ProtectedData 程序集。
- CSharpCompilation.Emit 仅写 MemoryStream，再 Assembly.Load(byte[])；不写源码、DLL、测试结果或日志文件。直接反射执行全部 Fact/InlineData 用例，并等待返回 Task 完成。
- 结果：契约 2/2；后端历史密文往返 1/1；非密文/旧配置前缀/未知版本/损坏 Base64/损坏 DPAPI 五项 5/5；成功与失败 canary 日志 1/1；Scope 声明/取消/退休/默认 unavailable 1/1；合计 10/10，命令退出码 0。
- 另对本次内存编译的 Scope 反射确认旧五参数公共构造存在。上述是源码断言的直接执行，不冒称为重新执行原 MSBuild/VSTest 工程流水线。

### 已有工程证据：自行读取日志，不凭口头声称

- [checks/a380c9eb-09a2-4a46-9680-2d598bcb8105.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/checks/a380c9eb-09a2-4a46-9680-2d598bcb8105.log) 确有总计 10、通过 10、失败 0、跳过 0 的工程测试摘要。
- [evidence/credential-protection-green.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/credential-protection-green.log):30 确有总计 134、通过 134、失败 0、跳过 0 的摘要。本轮没有重跑这 134 项；其历史通过证据与当前源码核对分开陈述。
- [evidence/protection-final-green.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/protection-final-green.log):1–2 是筛选命令解析失败，不是通过证据，未将其作为 green 使用。已有真实 checks 日志和本轮内存测试足以独立支持本段结论，因此这不是代码缺陷条目。
- 本机为 Windows；非 Windows unavailable 分支只作源码核对，没有跨平台实跑。生产/Playground 组合点本轮为静态接线核对，没有启动完整产品或代替后续动态源码/发布验收。

## 定向复查上下文

本轮无需提出修复项。后续若用户要求同一段复查，以本报告、上述精确位置和以下 SHA256 为首审基线，优先复核修复影响的安全/兼容路径；不把其他会话的新源码迁移工作直接归入本段。

| 首审源码/工程文件 | SHA256 |
| --- | --- |
| [`LoomX.Plugin.Abstractions/PluginBridge.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Plugin.Abstractions/PluginBridge.cs) | `CDB35BCBD5500DD595CEE69CE1279B602B23D2F039ED3BE6AF0D9E1810DDF951` |
| [`LoomX.PluginHost/PluginBridgeScope.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginBridgeScope.cs) | `AAB6D66D6896F0D8F62A764B6FEA54E21113674885AAF393DEE484D33655E2C7` |
| [`LoomX.PluginHost/PluginProtectionBridge.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs) | `1AFFF0B568A1B83A990081282FCF350E73D00C8E3C79ED142A5133568F0D6D1B` |
| [`LoomX.PluginHost/LoomX.PluginHost.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/LoomX.PluginHost.csproj) | `2A7116F3FFC3F34629B2C860AA7E552C1CFBB043CACEC20B44BA516EE1CE79A2` |
| [`LoomX/Services/PluginHostBridgeFactory.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX/Services/PluginHostBridgeFactory.cs) | `EDE52ECCC83638A866CF75786CC718809BD60374C82CABB2C688E6C58FBD3380` |
| [`LoomX.PluginPlayground/PlaygroundBridgeFactory.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginPlayground/PlaygroundBridgeFactory.cs) | `5FF6256E8005E511709DA2C229546371C3223583FC439F643EA94EB15BC6F98B` |
| [`plugins/LoomX.CredentialProtection/CredentialTokenStore.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialTokenStore.cs) | `F97E2462BB734C39B8C347C4A791A1F26E4873111B9E5CAFAE10A4FA83807019` |
| [`plugins/LoomX.CredentialProtection/CredentialProtectionPlugin.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/CredentialProtectionPlugin.cs) | `18920AEE6D56AAABD59E31A61CEB58DF14D9243EA90ED5519C88EC519CC7FE72` |
| [`plugins/LoomX.CredentialProtection/LoomX.CredentialProtection.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/plugins/LoomX.CredentialProtection/LoomX.CredentialProtection.csproj) | `03B213DBA9071B11E5DB7DDE28FE076964CB89B57606417F9B4E745969E08916` |
| [`LoomX.Tests/Plugins/CredentialTestFactory.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialTestFactory.cs) | `522399EF220E30552F3F12BCF369ACC051AF99956EEA6DB0774F6CD9A5B7FABF` |
| [`LoomX.Tests/Plugins/CredentialStorageBridgeTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialStorageBridgeTests.cs) | `F10D8892594B02B7E9B2215B718DA05351D7325F360B655F887C6BD63A9943C8` |
| [`LoomX.Tests/Plugins/CredentialStorageMigrationTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialStorageMigrationTests.cs) | `13F05B3068A34414E8170DAB6A419087F525153AEA0E38D14D325F9589E24EB6` |
| [`LoomX.Tests/Plugins/PluginProtectionContractTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionContractTests.cs) | `04F2D309B05BB62120B7544390CEFCA0B577FCEAD64D260434BAB31FD3C2D8E4` |
| [`LoomX.Tests/Plugins/PluginProtectionBackendTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionBackendTests.cs) | `28026AAC15509912E019D711C6F3D06DED5E873FDDE35585C5195B4017E58B41` |
| [`LoomX.Tests/LoomX.Tests.csproj`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/LoomX.Tests.csproj) | `B9B069B2E2B280E46F6FC12DB0F16FAD2DFC3AA1CF92A0D03ED5B0EB335A25EE` |

## 首审收尾补核（主会话并行更新）

- 收尾时间：2026-10-12 18:46:25 +08:00。报告写入后的 git status 显示主会话新增了相关测试，已补读其实际源码/diff；上列十二个核心源码/测试文件的 SHA256 再次核对未变化，纯内存 10/10 核验仍对应当前保护代码。
- [PluginBridgeContractTests.cs:19、45](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginBridgeContractTests.cs)：新 Protection 纳入公共契约存在性与参数可传输约束。
- [PluginHostBridgeFactoryTests.cs:25–33](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginHostBridgeFactoryTests.cs)：生产组合夹具声明 protection/v1，并在 Windows 验证真实往返；没有以 unavailable 冒充可用保护。
- [CredentialProtectionBridgeFailureTests.cs:12–47](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialProtectionBridgeFailureTests.cs)：新增不可用加密时阻断请求且表中零记录、失败事务释放后可正常写入；不可用解密时阻断历史响应且恢复能力后原映射仍可读、没有第二库。这两项本轮只读审查，未实跑，未混入先前 134/134 或内存 10/10 计数。
- 上述补核未发现新增安全/兼容问题，首审结论仍为 PASS。源码负载、manifest 和发布布局的并行修改仍不纳入此风险段缺陷。

| 收尾新增核对文件 | SHA256 |
| --- | --- |
| [`LoomX.Tests/Plugins/PluginBridgeContractTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginBridgeContractTests.cs) | `56B3A6265BDA5DF47F8BF1C5815F15FC1A5AFABA0266F143DA106F15C6CF63FF` |
| [`LoomX.Tests/Plugins/PluginHostBridgeFactoryTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginHostBridgeFactoryTests.cs) | `96048C16796DA2B316962A7FD8BFEEA3E942C43E7C586F96ECC33DDEBBD8DEB7` |
| [`LoomX.Tests/Plugins/CredentialProtectionBridgeFailureTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/CredentialProtectionBridgeFailureTests.cs) | `5A9D1E918C43AE34BE745C23C6794AA8168B62D7CA5E4A540E49DD39CBC991BF` |

## Runtime 唯一一次定向修复复查：成功日志高频采样

- 复查时间：2026-10-12 19:09:31 +08:00；原独立 reviewer 定向复查，不重开首审。
- 原问题：**IMPORTANT（已修复）**。成功路径原来每次写 Information；CredentialTokenStore 逐 placeholder 解密导致高频逐引用日志，违反 AGENTS 的逐 token/无业务意义高频日志约束。此问题是频率/级别问题，不是凭据泄漏。
- 首审没有识别该项；保留首审原文和指纹作为历史记录。首审 PASS 不能被解读为这项日志规范当时不存在问题；以下结论覆盖这一项。
- 额度：Runtime 修复及原 reviewer 定向复查 **1/1 已使用**；Feedback 未触发修复，仍为 0/1。未追加业务实现或开启新一轮修复。

### 修复核对位置与结论

- [PluginProtectionBridge.cs:15、55–64、80–85](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs)：实例 completed 为 long，Interlocked.Increment 保证并发计数；加解密成功都在返回前、取消复查之后调用 LogCompletion；仅 count=1 或 64 的倍数记录 Debug。结构化字段只为 PluginId、Operation、CompletedCount，不包含明文、密文或参数。
- [同文件:33–42、66–76](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs)：取消、退休、null 和平台检查仍在原边界；平台 Warning、失败安全 Error 保留；公开异常不增加 InnerException；dpapi:v1:/CurrentUser/null entropy、严格 UTF8、返回规则与 finally 清零未改变。
- [PluginProtectionBackendTests.cs:75–84](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionBackendTests.cs)：一次 Encrypt + 128 次 Decrypt，断言 <=5 条日志；canary/错误/Scope/DPAPI 兼容用例仍在。回归本身未精确断言级别和采样位置，本轮另外用只读内存 probe 补核，不虚称已新增永久测试。

### 独立验证

- 自行读取 [protection-sampling-red.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/protection-sampling-red.log)：真实失败，日志数实际=129，1 失败/0 通过。
- 自行读取 [protection-sampling-green.log](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/migrate-first-party-plugins-to-source-runtime/.comet/evidence/protection-sampling-green.log)：11 通过/0 失败/0 跳过。
- 当前 PluginBridge、Scope、PluginProtectionBridge 及两份测试源码纯内存 Roslyn 编译并直接执行 Fact/InlineData/Task：**11/11 通过**，退出码 0；未运行 MSBuild/dotnet test，不写 bin/obj/cache/日志/数据库。
- 内存补核：1 次 Encrypt + 128 次 Decrypt = **3 条 Debug**，计数精确 **1、64、128**；恢复原值一致。
- 已取消 Decrypt 不增加成功日志；损坏密文仍增加 **1 条 Error**、返回 OperationFailed、无 InnerException；全部事件不含 canary 明文或密文。
- 1 次 Encrypt + 128 个 Task 并发 Decrypt = **3 条 Debug**；新实例首次 Encrypt 独立写首条采样。
- 邻近检查没有发现其它 IMPORTANT：未增加明文回退、未知密文透传、异常泄密、DPAPI 格式变化、授权/退休绕过或 SQL 归属改变。非 Windows Warning 仅源码核对，不冒称跨平台执行。

### 当前结论

**PASS（定向复查通过）**。IMPORTANT 已修复 1 项；当前未解决 CRITICAL=0、IMPORTANT=0、MINOR=0。
- 前次 outputs/source-plugin-migration-2026-10-12_18-57-37 是修复前产物，其包形状检查有效，但不能证明采样修复已进入最终产品。最终 Verify 需使用修复后重新打包的新目录；旧产物保留。
- 本次不是最终集成审查，不以 11/11 代替 source/payload 或全量/GUI 验收。

### 复查后源码指纹

| 文件 | SHA256 |
| --- | --- |
| [`LoomX.PluginHost/PluginProtectionBridge.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginProtectionBridge.cs) | `CD1BC43B3E518D4A44229BD55D2638AEFF95E3D16A9F3AC4DA19B37320E972FA` |
| [`LoomX.Tests/Plugins/PluginProtectionBackendTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionBackendTests.cs) | `4660A40119C96EA42616409BBAE773E1992B20A540B7E383BA501C440F2C8531` |
| [`LoomX.Plugin.Abstractions/PluginBridge.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Plugin.Abstractions/PluginBridge.cs) | `CDB35BCBD5500DD595CEE69CE1279B602B23D2F039ED3BE6AF0D9E1810DDF951` |
| [`LoomX.PluginHost/PluginBridgeScope.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/PluginBridgeScope.cs) | `AAB6D66D6896F0D8F62A764B6FEA54E21113674885AAF393DEE484D33655E2C7` |
| [`LoomX.Tests/Plugins/PluginProtectionContractTests.cs`](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/PluginProtectionContractTests.cs) | `04F2D309B05BB62120B7544390CEFCA0B577FCEAD64D260434BAB31FD3C2D8E4` |
