# Compiler standard 唯一一轮定向复查

- 日期：2026-10-12（Asia/Shanghai）。
- 轮次：承接 [原首审报告](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/handoff/compiler-review.md) 的 **standard 唯一一轮修复复查**；不是第二次首审、不重置轮次。前一次未启动成功的 502 不计轮次。
- 范围：只覆盖 Compiler 首审 I-1/M-1/M-2 修复、迁移/锁顺序改变引入的风险，以及相应回归证据。
- **结论：通过本轮定向复查。未发现新的 CRITICAL / IMPORTANT / MINOR 必修项；I-1、M-2 已关闭，M-1 的补证与如实披露要求已满足，但历史 TRX 缺口仍存在。4.1–4.5、4.10 在本 Compiler 工作包范围内均可验收。**
- 本轮预算已经使用完，不自行开启第二轮 Compiler standard 修复复查。

## 1. 边界与独立验证方法

本轮没有修改任何生产源码、共享测试、工程、任务 checkbox 或 Comet 流程状态；没有提交、push、pull/fetch。只新增本报告与 [独立验证产物](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69)，原首审报告保留不动。没有清理其他会话文件。未新增或改写测试代码；沿用实际修复测试与首审时保留的独立 I-1 复现，正常重编当前源码。

**反馈 Bridge 原 I-2 仍待用户额外授权，本轮没有重审、关闭或替它验收。** Runtime/RuntimeLifecycle、新 PackageValidation、工具、Credential 均不属于本轮；ManifestParser 只读核对既有 id 规则，交叉工程里的 RuntimeIntegration 只作回归计数，不据此验收 Runtime。

共享 Tests 有在运行的 testhost，所有构建均使用新独立 artifacts 根。专属测试、原复现、多进程 worker、进程测试及交叉测试都通过 ProjectReference 正常构建 Host 当前源码；本轮没有使用 --no-build/旧 Host DLL，也没有写共享 bin/obj 或覆盖实现者的证据。

权威基准继续沿用原审已读的 AGENTS、正式 design/source-plugin-runtime spec/tasks。本轮复核 design 第 10 节的缓存身份/布局/原子发布契约，不扩大需求。

## 2. 实际修复与源码稳定性

相对首审独立保存的 9 文件指纹，**恰好 4 个文件变化**：SourcePluginCache.cs、SourcePluginCompiler.cs、SourcePluginCacheTests.cs、SourcePluginCompilerTests.cs；Inputs、Paths、References、References.targets、PluginHost.csproj 5 个不变。不是仅相信交付自报，已逐项用实际文件 SHA256 对照。[与首审的比较](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/first-review-comparison.csv)。

当前内容也与交付 source-final.csv 一致；交付记录的 12 文件指纹无差异，其中范围外文件只比摘要、不审源码。[交付指纹](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/compiler/standard-fix-20261012-060715/source-final.csv)。复查开始/结束的 9 个范围内文件全部一致：[开始指纹](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/source-start.csv)、[结束指纹](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/source-end.csv)。

四个修复文件仍未跟踪，Git 的提交基线无法提供它们的修复前 patch；已直接读取全部相关源码，并对照首审实际读过的实现及当时指纹，不把空 git diff 当成无变化。PluginHost.csproj 的实际 diff 仍只有原来的 Roslyn、固定引用包及 targets 导入，不是本轮新增改动；范围内 tracked csproj 的 diff --check 通过，未为 LF/CRLF 提示转码。

### I-1：关闭

**实际生产差异：**

- [SourcePluginCache.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/SourcePluginCache.cs) 第 45–81 行：根控制文件改为 .cache..auth-key / .cache..key.lock；先验证/创建根、取得根认证锁，再完成旧控制文件迁移与认证密钥读取。
- [SourcePluginCompiler.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.PluginHost/SourcePluginCompiler.cs) 第 70–73 行：AuthenticationKeyAsync 改到 LockAsync 之前；取得认证 key 后再创建/锁插件目录。
- 公共 CompileAsync、构造方法、不可变结果及 root/<id>/<key>/plugins/<id> 产物布局未变。缓存键/摘要/HMAC 元数据格式和候选发布规则没有改动。

**命名空间论证：** ManifestParser 原来就禁止 id 包含连续两个点；这不是本轮加严合法 id。SourcePluginInputs 仍须解析实际 Manifest 并检查有效性/调用快照后，才进入缓存阶段。因此新控制名及 .cache..legacy-* 保留名不可能是合法插件 id；旧两个名字和 Windows 大小写变体仍可作为合法 id 编译。路径工具允许内部普通文件名含连续点，并不把它解析成 '..' 目录段。

**迁移与锁顺序：** 新根锁把当前版本的所有 key 读取/迁移串行化；方法返回时根锁已释放，随后才等待插件锁，没有在持有插件锁时再获取根锁的反向顺序。旧普通 32 字节 key 在新 key 不存在时移动到新名称，原字节不变，旧 HMAC 可继续验证；旧空 lock 文件移动保留；旧同名目录不读取、不删除、不移动。新 key 已存在时，旧 key 另名保留而不覆盖当前 key。

**亲自验证：**

- 修复后的 4 个合法 id/大小写用例、旧污染目录哨兵保留、旧 key/旧 lock 迁移后真实旧签名命中全部通过。
- 首审原来的两个独立复现未修改断言，重新构建当前 Host 后由 RED 转为 **2/2 GREEN**：[原复现复查 TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/results/first-review-repro-followup.trx)、[日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/first-review-repro-followup.log)。
- 原同进程并发/篡改/候选失败保留回归继续通过；实际多进程与迁移新增分支见后文。

### M-2：关闭

[SourcePluginCompilerTests.cs](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/LoomX.Tests/Plugins/SourcePluginCompilerTests.cs) 中原 ThirdPartyReference 用例现已：

1. 使用真实 xunit.assert，断言名称，Assembly.Load 后为同一实例，并确认 AppDomain 已加载该实例。
2. 复制真实有效 DLL 到插件包并检查程序集身份一致。
3. 放入声明依赖的 .csproj；实际源码 using Xunit 并调用 Assert.True。
4. 仍断言 Compile 阶段 CS0246，返回诊断不包含该 DLL 绝对路径。

本轮独立 50 个测试中该用例通过。References/targets 未被扩大，插件目录 DLL、宿主已加载 DLL 和 .csproj 仍不会扩展编译白名单。该覆盖补强在修复前的本轮 RED 组就已经通过，不把它说成“生产越权漏洞已修复”。

### M-1：补证/披露要求满足，历史缺口保留

已读取实现者本轮原始日志、TRX、命令/退出码和源码指纹，不仅依赖 README 的 GREEN 汇总：

| 阶段 | 原始结果 | 退出码 |
|---|---|---|
| 定向 RED | 7 项：6 FAIL、1 PASS | 1 |
| 同一组 GREEN | 7/7 PASS | 0 |
| 多进程 RED | 1 FAIL；6 worker 返回缓存失败 | 1 |
| 多进程 GREEN | 1/1 PASS，实际 6 worker + 3 重启 | 0 |
| Compiler/Cache regression | 50/50 PASS | 0 |
| crosscheck | 127/127 PASS | 0 |

[修复交付 README 与原始链接](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/compiler/standard-fix-20261012-060715/README.md)；[本审从原 TRX/命令提取的汇总](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/original-fix-evidence-summary.json)。

额外核对：RED/GREEN 的 7 个用例名称集合完全一致，两个测试源文件摘要一致；RED 的 Compiler/Cache 生产源码摘要与首审独立留存摘要一致，GREEN 才改变这两个生产文件。两次记录的 Host 哈希不同，重编日志均显示真实 ProjectReference 构建；没有 --no-build。时间顺序也一致：2026-10-12 06:09:36 开始定向 RED，06:12:11 开始多进程 RED，06:13:18 开始定向 GREEN，06:16:58 开始多进程 GREEN（Asia/Shanghai）。[TDD 指纹交叉核对](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/tdd-source-crosscheck.json)。

旧工作包历史 TRX 缺失仍然无法回填；本轮没有伪造、重写或把新记录冒充旧 TRX。**这里关闭的是首审要求的如实披露及修复轮次可核对补证，不宣称历史 TDD 已获得完整恢复。**

## 3. 本轮亲自执行结果

| 验证 | 实际结果 | 独立证据 |
|---|---|---|
| 当前源码正常重编 Compiler/Cache | **50/50 PASS**：Compiler 21、Cache 29；退出 0，无跳过 | [TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/results/compiler-followup.trx)、[构建/测试日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/compiler-followup.log) |
| 首审原 I-1 独立复现，按新源码重编 | **2/2 PASS**；退出 0 | [TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/results/first-review-repro-followup.trx) |
| 当前源码重编 worker，运行多进程定向测试 | **1/1 PASS**；退出 0 | [worker 构建日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/worker-build.log)、[TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/results/compiler-followup-process.trx)、[测试日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/compiler-followup-process.log) |
| 当前独立交叉工程正常重编/执行 | **127/127 PASS**：Compiler 21、Cache 29、Manifest 73、RuntimeIntegration 4；退出 0 | [TRX](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/results/compiler-followup-cross.trx)、[日志](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/compiler-followup-cross.log) |
| 新迁移分支：新旧双 key 同时存在 | 当前 key 保留、旧 key 原字节保留为 archive、当前真实签名继续 CacheHit、DLL 路径一致 | [新增分支汇总](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/migration-risk-summary.json)、[实际 worker 结果](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/dual-key-second.json) |
| 新迁移分支：损坏旧 key | Success=false、LX_CACHE_FAILED，旧文件保留，不静默再生成替代 key | [实际 worker 结果](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/invalid-legacy-result.json) |

[本轮 TRX 总结](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/verification-summary.json)。双 key/损坏 key 验证只使用复制的已有源码夹具和从当前源码重编的 worker；没有新写测试/生产代码，没有回显认证密钥。

### 多进程结果的实际断言

不是同进程创建多个 Compiler：[原测试源码](file:///D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/openspec/changes/add-source-plugin-runtime-and-bridge/.comet/evidence/compiler/standard-fix-20261012-060715/process-probe/ProcessTests.cs) 启动 6 个不同进程，同根同时编译 .cache-auth-key、.cache-key.lock、demo 三个插件（各 2 worker）。实际读取根 key、cache.json 和 cache.sig 验算 HMAC；每个 id 恰好一次 miss、同一 key。随后每个 id 新启一进程要求 hit、同 DLL 路径、无候选残留。

本审生成的 [9 个独立子进程原始结果](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/process-results) 复核为：**9 个不同 PID，全部 Success=true，3 miss、6 hit**；每个插件为 1 miss + 2 hit。worker/Host 的独立构建指纹已保存：[worker SHA256](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/worker-hash.csv)、[worker Host SHA256](file:///C:/Users/BianShanghai/AppData/Local/Temp/LoomX-compiler-followup-20261012-db1f1803bce84bf086f3edf00b788e69/worker-host-hash.csv)。没有把实现者的旧 worker 或共享 Tests DLL 当成本审 GREEN。

## 4. 迁移带来的风险判断

**没有发现本轮新的确定性必修缺陷。**以下边界保留，不扩大为新需求，也不因 GREEN 而声称已经保证：

- **旧、新二进制同时使用同一根：** 新版根锁与旧版锁不同；旧版可能持有或重新创建旧控制文件。实现者已经披露该组合未覆盖，升级应避免旧、新宿主并行使用同一源码缓存根。本轮只证明同一修复版的实际多进程；不宣称跨版本并行迁移安全。这是本次迁移新增的发布注意事项，不是在当前范围内已复现的阻塞缺陷。
- **双 key：** 本审已补验“保留新 key、归档旧 key、当前签名继续命中”分支。两把不同 key 分别签出的所有历史代际不可能同时由单个活动 key 验证；不能由本次测试声称全部旧签名都会命中。原需求的正常旧单 key 迁移连续性已验证。
- **崩溃/中途取消：** key 创建写入、旧文件迁移中断、磁盘满等故障注入没有新增覆盖；沿用首审保留的风险，不承诺断电事务。普通取消及资源回归没有因本次修复扩大证明。
- **损坏/reparse：** 损坏旧 key 已亲自验证 fail closed 且保留；reparse 分支按源码继续拒绝，没有本轮再做所有历史组合的故障注入。旧同名污染目录的哨兵文件在修复测试中明确保留。
- **平台/发布：** 本轮验证 Windows 的大小写与进程文件锁，不新增 Linux/macOS、single-file/trimmed、完整桌面发布或 GUI 承诺。无 SDK 发布证明沿用首审亲自重编/执行的结论，本轮没有重跑，也不伪称重跑。

## 5. 逐任务验收结论

| 任务 | 本轮决定 | 说明 |
|---|---|---|
| 4.1 | **可验收** | 首审编译/portable PDB/私有无 SDK 验证有效；本轮正常重编，API 与固定选项无变更 |
| 4.2 | **可验收** | Inputs/Paths 与首审指纹相同，原边界回归在 50 项中继续通过；不扩展为持续竞态沙箱 |
| 4.3 | **可验收** | References/targets 不变，M-2 的真实宿主加载与包内 DLL/.csproj 前提补齐且独立通过 |
| 4.4 | **可验收，解除首审阻塞** | I-1 命名空间修复关闭；完整身份/文案/引用变化回归通过，普通及控制同名插件可共用根 |
| 4.5 | **可验收，解除首审阻塞** | I-1 关闭，旧污染保留恢复、旧签名迁移连续性、HMAC/原子候选回归和真实多进程通过 |
| 4.10 | **可验收** | 首审固定引用/无 SDK/四语言/嵌入英文证明有效，相关实现未变；本轮相应缓存/文案回归继续通过 |

验收限定为 Compiler 的 task4.1–4.5、4.10，不是整项 change、生产 Runtime 或最终桌面成品已验收。没有代协调方勾任务，也没有重新开启下一轮 standard。**反馈 Bridge 原 I-2 保持未授权、未复查状态。**
