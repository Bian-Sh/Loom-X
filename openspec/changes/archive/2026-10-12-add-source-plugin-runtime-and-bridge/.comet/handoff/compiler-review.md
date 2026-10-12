# 源码编译器 standard 独立首审报告

- 日期：2026-10-12（Asia/Shanghai）。
- 轮次：standard 首审；前一次未成功启动的 502 并发错误不计审查轮次。最多保留后续一轮修复复查。
- **结论：暂不通过工作包整体验收。CRITICAL 0，IMPORTANT 1，MINOR 2（后两项为证据/测试覆盖问题）。4.4、4.5 被 IMPORTANT 阻塞。**
- 本次只新增本报告。源码、共享测试、任务 checkbox、共享 Comet 状态均未修改；未 pull、fetch、提交或推送。独立构建/探针/日志全部位于 [本次独立产物目录](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d)。没有清理其他会话产物。

## 1. 范围与基准

权威基准：[AGENTS.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/AGENTS.md)、[正式 design.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/design.md) 第 10、11 节及相关风险、[source-plugin-runtime spec](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/specs/source-plugin-runtime/spec.md)、[正式 tasks.md](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/tasks.md) 4.1–4.5、4.10。实际代码优先于交付说明。

审阅文件：PluginHost 的 SourcePluginCompiler、SourcePluginInputs、SourcePluginPaths、SourcePluginReferences、SourcePluginCache、SourcePluginReferences.targets、PluginHost.csproj 对应 diff；SourcePluginCompilerTests、SourcePluginCacheTests；compiler/evidence README、专属测试入口、原始 RED/GREEN 日志、发布探针源码及私有无 SDK 运行日志。新文件尚未跟踪，因此直接阅读完整新文件，不把空 git diff 视为无修改。只读使用 git status/diff 识别现场与 csproj 改动。

不审生产 Runtime/RuntimeLifecycle、在途请求、PackageValidation、开发工具、SQLite、Credential。ManifestParser/PluginTextCatalog 仅用于确认编译输入和现有文案协议，不对它们作范围外审查。运行四个 RuntimeIntegration 回归只用于复核交付测试总数，不代表已验收 Runtime 工作包。

只读查询了独立复制的 CodeGraph 数据库：索引只有早期 SourcePluginCompiler/Tests，没有 Inputs/Paths/References/Cache，且编译器符号行号属于旧版。因此未更新共享索引，后续以真实文件为准。[CodeGraph 查询结果](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/codegraph-query.json)。

范围内 9 个源码/工程/测试文件审查前后 SHA256 一致：[开始指纹](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/source-hashes-before.csv)、[复核指纹](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/source-hashes-final.csv)。这是范围内快照校验，不宣称其他会话或 Abstractions/Runtime 未发生变化。

## 2. 必修缺陷

### IMPORTANT I-1：插件 id 与缓存根控制文件同名，可永久阻断其他插件编译

**位置：** [SourcePluginCache.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/SourcePluginCache.cs) 第 12、22–27、45–59 行；[SourcePluginInputs.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/SourcePluginInputs.cs) 第 18–27 行。影响任务 4.4 的插件隔离与 4.5 的缓存资源/故障隔离。

**原因：** Parent 直接采用 `root/<pluginId>`，而根目录又把 `.cache-auth-key`、`.cache-key.lock` 用作内部文件。两个名字都能通过当前 Manifest 及 SourcePluginPaths 的 id 校验。首次使用新缓存根时，LockAsync 先将同名路径创建为插件目录；AuthenticationKeyAsync 再把同一路径当作控制文件打开，于是返回 LX_CACHE_FAILED。目录持久保留，后续不同 id 的普通插件在同根仍失败。

这不是可执行恶意插件绕过沙箱的问题：没有执行插件代码，没有修改认证密钥，没有制造 reparse point，也没有磁盘故障；一个目前被接受的 Manifest 名称即可触发。新安装/新缓存根是明确触发条件；若控制文件已经存在，相撞插件通常直接失败而不新增污染，但该 id 仍不可正常编译。

**亲自复现：** 独立夹具以 .cache-auth-key、.cache-key.lock 分别作 id；源码 ILoomXPlugin.Id 同步改成相同值，四语言与 Manifest 齐全。随后使用同一 cacheRoot 编译普通 demo 插件，恢复其源码 Id=demo。两例普通插件均 Success=false，Stage=Cache，Code=LX_CACHE_FAILED。重跑仍相同。

[最终复现 TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/results/boundary-probes-verified.trx)；[最终复现日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/boundary-probes-verified.log)；[独立最小回归源码](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/probes/CompilerReviewTests.cs) 的 ValidPluginId_DoesNotPoisonOtherPlugins。

**最小回归要求：**

1. Theory 覆盖上述两个 id；Windows 再加大小写变体。使用全新缓存根。
2. 若设计选择明确保留这些 id：必须在任何创建缓存目录/控制文件前返回安全 Manifest 诊断，且后续 demo 编译成功。若继续允许：该插件自身及后续 demo 都必须成功。
3. 普通插件第二次编译必须 CacheHit=true；不可出现控制文件被变成目录的状态。
4. 保留原有不同插件隔离、HMAC、篡改重编译、失败候选清理及并发用例。

**最小修复方向：** 明确隔离控制文件与插件 id 的命名空间，或在输入边界预留并拒绝控制文件名；不要只捕获更多 IOException，因为当前错误已经安全分类，但持久跨插件污染仍在。若处理已污染的根，须明确恢复策略，不得静默递归删除任意插件目录/旧代际。实现者自行修复，本审未改源码。

## 3. MINOR：证据与测试缺口

### MINOR M-1：原 compiler 工作包没有可核对的逐用例 TRX 与阶段快照

[原 README](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/compiler/README.md) 和原始 stdout 日志确实存在。原 17-final-compiler-green.log 记录 44/44，16-final-integration-green.log 记录 120/120；但 compiler 目录递归未找到 TRX，其他工作包 TRX 也没有 compiler/cache 用例，不能冒充本包原始记录。

RED 日志含真实失败用例与断言/异常，能证明失败曾运行；GREEN 日志含对应汇总。但缺少各阶段完整命令、退出码和测试/源码指纹，不能从日志单独证明每一阶段测试一定先于实现，也不能用首审新 TRX 回填“原始 TDD TRX”。

**整改/补证：** 不伪造旧 TRX；如实标记原记录缺失。修复轮次保存新回归 RED→GREEN 的完整命令、退出码、TRX、源码指纹与明确过滤范围。本次已独立复核现在的 44 个测试，不因此声称补齐过去的证据。

### MINOR M-2：第三方引用用例名称强于实际设置

[SourcePluginCompilerTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/SourcePluginCompilerTests.cs) 第 90–98 行的 ThirdPartyReference_IsNotAvailableEvenIfHostHasLoadedIt，只在源码中引用 Microsoft.Data.Sqlite；用例没有显式加载该第三方程序集，也未把一个有效第三方 DLL 放到插件目录。独立 Compiler.Tests 项目只引用 PluginHost，不能由标题证明“宿主已加载”这个前提。

**最小回归：** 用可确认的有效第三方 DLL，断言已加载的程序集身份，把 DLL 同时放入插件包，源码引用其真实公开类型；仍应收到 Compile 阶段的安全缺失引用诊断。可同时放入一个声明该依赖的 .csproj，确认不会扩展白名单。当前实现从内嵌资源构造引用、没有宿主/插件 DLL 扫描或恢复调用，静态检查支持 4.3；此项是防回归覆盖不足，不是已证实的越权引用漏洞。

## 4. 实际运行与原证据核对

### 本次独立执行

| 项目 | 实际结果 | 证据 |
|---|---|---|
| 从当前源码独立构建后过滤 Compiler/Cache | **44/44 GREEN**；Compiler 21、Cache 23；退出 0 | [TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/results/compiler-review.trx)、[日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/targeted-test.log) |
| 同一独立构建的 Compiler.Tests 全部交叉测试 | **121/121 GREEN**；Compiler 21、Cache 23、Manifest 73、RuntimeIntegration 4；退出 0 | [TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/results/compiler-cross-regression.trx)、[日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/cross-regression.log) |
| 独立边界探针最终版 | **8 项，6 GREEN、2 RED**；两例 RED 都是 I-1；退出 1 | [TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/results/boundary-probes-verified.trx) |
| 从当前源码重新 Release publish NoSdkProbe | 退出 0，输出在独立 no-sdk-app | [发布日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/publish.log) |
| 私有运行时 SDK 清单与真实执行 | 清单空；退出 0；FirstCacheHit=false、PortablePdb=true、CacheHit=true、实例化成功、SdkDirectoryExists=false、ReferencesEmbedded=169 | [SDK 清单](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/no-sdk-list-sdks.log)、[执行日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/no-sdk-run.log) |
| 四个独立私有无 SDK 子进程，同 package/cacheRoot | 四个退出 0，同一 key，恰好一份编译、三份命中 | [多进程原始结果](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/process-concurrency.json)、[子进程入口源码](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-review-20261012-4e80b15e247d4e9ca9ae56138f4e7f8d/process-probe/Program.cs) |

定向测试的关键命令（所有路径替换为上述独立目录；未写共享 bin/obj）：

~~~powershell
dotnet test <compiler-evidence/Compiler.Tests.csproj> --artifacts-path <独立根/artifacts> --filter 'FullyQualifiedName~SourcePluginCompilerTests|FullyQualifiedName~SourcePluginCacheTests' --logger 'trx;LogFileName=compiler-review.trx' --results-directory <独立根/results> --nologo
~~~

交叉测试随后以同一 artifacts --no-build --no-restore 执行，不是完整 LoomX.Tests。私有运行时使用交付中的 sdkless-dotnet，只读复用其 dotnet.exe/Runtime；子进程 DOTNET_ROOT、DOTNET_ROOT_X64、PATH 指向私有根，DOTNET_MULTILEVEL_LOOKUP=0。没有运行会覆盖原日志的 run-sdkless-probe.ps1，也没有把共享探针运行目录作为新输出。

**120 与 121 的区别：** 原日志确实是 120，本次独立构建的实际枚举为 121。没有原 TRX/源码快照，不能精确追溯原少了哪个用例，不判定原计数造假；报告分别保留，不将当前结果改写成 120。

独立边界探针通过的 6 项：等待编译锁期间取消并成功重试；认证文件被独占造成 IO 失败时返回安全 Cache 诊断并保留旧 DLL、无候选残留；unsafe 与畸形 UTF-8 被拒绝；缓存命中的 warning 仍使用物理相对行列和安全消息；四语言从实际缓存程序集路径按既有 Catalog 读取；固定引用逐字节与共享契约身份匹配。

第一版自建四语言夹具的断言之后，因 LoadFromAssemblyPath 的 ALC 尚未完成回收而清理失败；仅修正独立夹具，通过不可内联 helper 完成加载/卸载，再 GC 后清理。原失败日志保留；它不是产品缺陷，也不计首审轮次。最终八项结果已排除此干扰。

### 原始 TDD 与发布记录

| 原阶段 | 原始结果核对 |
|---|---|
| 01-red → 02-green | 最小真实插件编译用例，1 FAIL → 1 PASS；旧源码快照缺失，不能进一步核对当时每一行实现 |
| 03-safety-red → 04-safety-green | 11 FAIL + 4 PASS → 15 PASS；包含缺失文件/危险路径原始异常 |
| 05-cache-red → 06-cache-green | 新缓存阶段 9 FAIL + 7 PASS；累计 GREEN 31 PASS；README 的累计数不是这份 RED 的总数 |
| 07-reference-red → 08-reference-green | 4 FAIL + 39 PASS → 43 PASS |
| 15-common-identity-red → 16/17 GREEN | Roslyn Common 身份断言真实失败；后续累计交叉 120、专属 44 通过 |
| 09/10/11 发布与无 SDK | 发布成功、SDK 清单空、探针退出 0；本审又独立重建并亲自执行，未只信 stdout |
| 13-host-source-plugin-green.log | 实际是范围外构建错误；README 已披露；**不计 GREEN，不计完整主工程回归** |

## 5. 契约核验

- **固定引用与契约身份：** targets 内嵌固定 Microsoft.NETCore.App.Ref 10.0.9/net10.0 的 167 个 DLL，再加唯一 Abstractions 引用及摘要清单；不是当前 SDK/TPA 快照。References 校验版本头、总条数、文件名重复/必要引用、SHA256；缺失/篡改返回 LX_REFERENCES_INVALID，没有 SDK 搜索或网络补齐。独立探针逐字节比较全部 167 个 DLL，并检查内嵌契约 PE 的程序集名称/版本/公钥与运行中的共享契约一致；实例化可赋值为当前 ILoomXPlugin。身份为 LoomX.Plugin.Abstractions, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null。最近探针的引用清单摘要为 343D3B6E37A5D3E35EB610D662CCAEC96E1610197708D5761BB734F838BB3FC3。该摘要属于该次构建，包含实际契约引用字节，不要求跨不同构建产物恒定。引用包版本 10.0.9 与私有执行 Runtime 10.0.11 是不同角色，不混淆。
- **Roslyn 选项：** Roslyn CSharp 5.3.0；CSharp14、Regular、DocumentationMode.Parse；DLL、Release 默认、nullable enable、deterministic、unsafe=false、concurrentBuild=false、portable PDB。内部 WarningsAsErrors 变体影响真实编译及缓存键。CompilerIdentity 包含 CSharp 与 Common 的 FullName/MVID；原 Common 缺失已补。独立用例及原同输入重编译用例支持确定性，不声称不同引用字节/不同构建环境必定 byte-identical。
- **输入边界：** 只读取显式 sources，规范相对路径与去重；拒绝绝对/UNC/设备/盘符相对/穿越/控制字符/保留设备名，逐段 NoLinks，在读取前后复查。完整实际 Manifest 解析结果必须与调用快照一致，规范化完整 JSON（含未知字段）；拒绝重复属性；严格 UTF-8。Manifest、源码和四语言使用相同安全读取路径。持续恶意同用户竞态不是此实现的 OS 沙箱保证。I-1 是另一个未经隔离的缓存名称边界，不能被上述路径检查消除。
- **缓存身份：** 完整规范 Manifest、源码规范相对路径/字节、四语言路径/字节、实际引用摘要（涵盖契约引用内容）、两个 Roslyn 组件身份和生效设置参与键；文件顺序依 sources 声明。未知 Manifest 字段、文案、路径、契约字节、编译选项变化有测试；不同插件目录隔离的普通情形通过，但 I-1 破坏控制命名空间隔离。
- **完整性与并发：** cache.json 及 HMAC-SHA256 签名认证，命中逐一检查 DLL、PDB、Manifest、四语言摘要与期望文件集合；安全重写缓存 diagnostic message。候选同根、签名后再目录 rename；无效旧代际隔离保留，失败候选不替换旧版本。同进程原测试及本次四个实际进程均通过。HMAC 密钥位于同用户可访问的本地根，不声称抵御控制宿主/密钥的同用户攻击者。
- **诊断安全：** Roslyn 错误/警告只保留 ID、阶段、严重性、物理 GetLineSpan 相对路径及 1-based 行列，消息使用受控模板，不回显源码字面量/#error/#warning；故意不使用 GetMappedLineSpan。本审补测缓存 warning 与 #line 外部路径不会进入返回诊断。这里验证的是诊断/相关日志，不声称源码作者写入 #line 的内容不能出现在调试符号文档信息中。
- **本地化及发布：** 四语言必须为有效文本字典，参与缓存身份；最终负载位于 <hash>/plugins/<id>/，manifest 名称与 i18n/<culture>.json 保持原形；en-US 嵌入名与 PluginTextCatalog 既有协议一致，无新查找特判。实际四文化读取及原 en-US 代际/嵌入回落测试通过。csproj 只增加 Roslyn PackageReference、固定 PackageDownload 与 target Import，编译引用是 EmbeddedResource，不是传播的 Content。
- **取消及资源异常：** 原预取消测试与本审等待编译锁期间取消均传播 OperationCanceledException；本审普通文件独占 IO 失败安全分类、保留旧产物，无候选残留。没有复现 Emit 中途取消、密钥首次写入中断、磁盘满/权限切换/进程崩溃，不能把这些记作通过。

## 6. 逐任务验收建议

| 任务 | 首审决定 | 理由 |
|---|---|---|
| 4.1 | **可验收（编译器范围）** | 固定选项、可加载 DLL/portable PDB、重新发布后的私有无 SDK 真实执行通过 |
| 4.2 | **可验收（显式源码/包路径条目）** | 未声明文件不参与，缺失文件安全诊断，危险路径及既有 junction 读取前拒绝；不延伸为持续竞态沙箱承诺 |
| 4.3 | **可验收；补 M-2 回归** | 内嵌受控引用构造、没有任意 DLL/SDK/NuGet 搜索；固定引用字节/契约身份与真实使用通过 |
| 4.4 | **不验收** | 键完整性普通情形通过，但 I-1 能从有效插件 id 持久破坏其他插件缓存隔离 |
| 4.5 | **不验收** | 普通候选/HMAC/原子发布/并发通过，但控制文件命名碰撞造成持久共享根故障，须先修 I-1 |
| 4.10 | **可验收（发布引用/文案条目）** | 实际重新发布与无 SDK 编译、缺失引用安全诊断、四语言实际缓存读取与嵌入 en-US/文案代际测试通过；工作包整体仍受 4.4/4.5 阻塞 |

这是审查建议，未代协调方勾选任务。

## 7. 修复复查入口与保留风险

后续只保留 **一轮 standard 修复复查**。必需输入：I-1 的最小修复、上述两例及大小写/正常重试回归、真实 RED→GREEN 日志/TRX/命令/指纹、重新执行 44 个专属测试；若引用/负载构建变化，再执行独立发布与无 SDK 探针。M-1 必须如实说明历史证据不可恢复，M-2 建议一并补齐，避免后续再扩大轮次。

不要求改 Runtime、PackageValidation、工具或 Credential；这些仍由各自工作包独立审查。不要把本报告变成整个 change 已通过的证明。

仍未覆盖：完整桌面成品发布、single-file/trimmed、GUI、完整 LoomX.Tests、电源/进程中断、磁盘满、首次认证密钥部分写入、持续恶意路径竞争与额外文件的垃圾回收。根密钥直接创建写入以及损坏代际两步 rename 的崩溃恢复是后续故障注入风险，不因普通测试 GREEN 被夸大为断电事务承诺；本审没有确定性复现这些风险，不另列已证实 IMPORTANT。
