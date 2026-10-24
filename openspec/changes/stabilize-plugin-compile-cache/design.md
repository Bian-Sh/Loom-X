# Design

## Context

缓存身份由 `SourcePluginInputs.CacheKey`（`SourcePluginInputs.cs:96`）生成：`"LoomX.SourceCache/v1"` + 引用集摘要 + 编译器身份 + 编译选项身份 + Manifest 字节 + 全部源码/本地化字节。其中：

- 引用集为发布期内嵌的 169 个 DLL（167 个固定 `net10.0|10.0.9` 框架引用 + 1 个 Plugin API 引用），摘要清单 `LoomX.SourceReferences.sha256` 随宿主发布内嵌（`SourcePluginReferences.cs:33`）。Plugin API 引用所属程序集（`LoomX.Plugin.Abstractions`）每次源码迭代都会产生新字节 → 摘要变化 → 所有源码插件缓存失效。
- 编译器身份 = Roslyn 程序集 FullName + MVID（`SourcePluginCompiler.CompilerIdentity`）。SDK/Roslyn 包版本变化（宿主重新发布时常见）→ 所有插件缓存失效。

实测证据：

- `loomx-20261013.log`：同一宿主二进制内，首次编译后重启命中缓存 12 次（缓存机制在同身份下可靠）。
- `loomx-20261024.log`（新发布宿主）：两个插件全部未命中重新编译，`编译开始 → 编译完成` 无 `缓存命中`；合计约 4.4s。
- `00:22:51.105 Database 完成 → 00:22:54.195 首个编译开始` 存在约 3.1s 前置开销，尚未归属（候选：`LoomXHost.CreateAsync` DI 构建、`EnsureBrowserRuntime()`、`PluginCatalog.Discover`、`SourcePluginReferences` 初始化）。
- 两个插件串行编译（3.59s + 0.82s），相互独立可并行。

约束：缓存正确性契约不可破坏——「任一编译输入变化导致失效」「不完整缓存不加载」「认证密钥签名机制」（`SourcePluginCache`）是安全边界，本 change 不得绕过或弱化。

## Goals / Non-Goals

**Goals:**
- 宿主常规发布/迭代后，第一方源码插件首次启动即复用编译产物，消除每次启动约 4.4s 的重复编译。
- 缓存未命中时可直接从日志定位是哪个身份组成部分变化。
- 压缩插件阶段固定开销（并行编译 + 前置 3.1s 归属）。

**Non-Goals:**
- 不做跨身份的产物复用（同源码不同 Roslyn 版本复用 IL 是不安全的，明确不做）。
- 不改变用户自建源码插件的运行时编译体验与安全边界。
- 不处理启动闪屏与数据库读取消（独立 change：`reduce-startup-db-reload`）。

## Decisions

1. **先插桩定位，再做机制改造**：第一步给缓存身份各组成部分输出结构化摘要日志（referencesDigest 前 16 位、compilerIdentity 哈希、optionsIdentity、插件输入哈希），未命中时与既有缓存目录记录的对应值对比，指明翻转项。避免在不知道主因（Plugin API 引用变化 vs Roslyn MVID vs 其他）的情况下选错方案。
2. **第一方插件预编译种子（推荐主方案）**：发布期用与运行时相同的编译输入离线执行同一编译流程，把产物连同完整性元数据写入安装镜像的缓存目录；运行时发现缓存即可直接命中。需要解决「发布期 Roslyn 身份 ≠ 运行期 Roslyn 身份」：为预编译产物提供独立的信任路径（复用认证密钥签名，标记为宿主随附可信产物），完整性仍绑定插件输入哈希；运行期 Roslyn/引用与发布期不一致时按现有安全语义降级为重新编译。
   备选「稳定化 CompilerIdentity 用 Roslyn 包版本号替代 MVID」被列为并行小改：收益有限（Plugin API 引用变化仍会失效），但实现小、无安全影响，可同 change 一并落地。
3. **插件并行编译**：`PluginRuntime` 内相互独立的源码插件编译用并行任务（默认并行度 = 处理器核数，上限 4），诊断与加载顺序保持发现顺序不变。收益约 0.8s（当前串行 4.4s → 约 3.6s），与预编译叠加后插件阶段接近纯加载耗时。
4. **前置 3.1s 先归属再处理**：在 `EnsureHostedServicesAsync` → `LoomXHost.CreateAsync` 链路加阶段计时日志（Debug/Information 均可），定位大头；若是 `EnsureBrowserRuntime()` 等非编译开销，只记录结论并把可异步的部分移出关键路径，不在本 change 内强行重构。

## Risks / Trade-offs

- 预编译种子的信任路径如果实现不当会扩大攻击面：必须复用既有认证密钥签名机制，且「预编译可信」仅限宿主安装镜像自带的产物，用户目录下同名产物不享受该待遇。
- 并行编译会使日志交错，需要保证每个插件的诊断组仍按插件聚合输出（保持 `PluginDiagnostics` 按插件归组）。
- CompilerIdentity 稳定化若仅用包版本号，理论上存在同版本号不同构建的极小风险；保持「包版本 + 程序集版本」双写并在诊断日志中同时输出 MVID 以便排查。

## Open Questions（已于 tasks 1.3 实测关闭）

### 结论：宿主重建后翻转的身份组成部分是「引用集摘要」（tasks 1.3 实测，2026-10-24）

连续构建 + 运行验证（Release、同一缓存根、插件源码不变）：

| 运行 | 条件 | 结果 |
|---|---|---|
| A | 宿主构建 #1 首次启动 | 两插件编译并封存身份（references=571CB298BD6E9AFA，compiler=8CA98715BC80AC35） |
| B | 同一二进制再次启动 | 两插件缓存命中，身份逐项一致 |
| C | 契约程序集加入探针成员后重建、启动 | 两插件未命中，身份对比 Flipped=**references**（571CB298BD6E9AFA→61DD458A1C9AB897），compiler/options/inputs 全部不变 |
| D | 还原契约源码重建、启动 | 两插件直接命中原代际（references 回到 571CB298BD6E9AFA），无编译耗时 |

- **翻转项 = 引用集摘要**，驱动字节是发布期内嵌的 `LoomX.Plugin.Abstractions.dll`：契约程序集源码迭代 → 引用字节变化 → 全部源码插件缓存失效。纯宿主应用代码迭代不改契约字节时，缓存身份稳定（运行 B/D 证实）。
- **补充（4.2 发布实测发现）**：`dotnet publish -r win-x64 --self-contained` 产出的契约程序集字节与普通 `dotnet build` **不一致**（两次独立 publish 字节彼此一致且缓存键一致）——因此即便源码毫无变化，**安装发布版本身就会翻转引用集摘要**，这正是「宿主一重新构建/发布，缓存身份即失效」的完整解释。种子从发布产物离线编译，与运行期身份按构造一致，恰好覆盖该场景。
- **编译器身份在同一 SDK（net10.0/10.0.9）+ Microsoft.CodeAnalysis.CSharp 5.3.0 下跨构建稳定**：NuGet 预编译程序集的 MVID 不随宿主重建变化，只有升级 Roslyn 包版本才翻转。CompilerIdentity 稳定化（tasks 3.3）仅用于消除「同包版本不同构建」的理论风险，可按 Decision 2 的并行小改落地。
- 编译选项与插件输入不随宿主构建变化。
- **预编译种子（Decision 2）落点确认**：种子必须在发布期用与宿主随附完全相同的引用集与 Roslyn 程序集（同一次构建/restore 产物）离线编译；运行期身份与发布期不一致时，缓存键不同 → 种子目录名不匹配 → 按现有键语义自然回落运行时编译，无需独立开关。

### 结论：前置 3.1s 的归属（tasks 1.2 插桩后 warm 实测）

| 环节 | warm 实测（四次启动） |
|---|---|
| EnsureBrowserRuntime | 18–25ms |
| LoomXHost.CreateAsync | 143–252ms（DB init 68–76ms + reload 19–20ms + 容器构建 10–24ms） |
| PluginCatalog.Discover | 15–17ms |
| 每插件包验证（含首次输入读取） | 155–258ms（12 源文件插件）/ 14–18ms（22 源文件插件） |
| SourcePluginInputs.ReadAsync（编译内重读） | 6–11ms |
| 合计「Database 完成 → 首个编译开始」 | **约 0.4–0.6s** |

20261024 发布日志中 Database 完成 → 首个编译开始的 3.1s 是**冷启动成本（JIT/磁盘/杀软扫描）叠加在同一结构链路上**，warm 重启不支付，不属于固定结构开销。

### 结论：2.2 可压缩项取舍

- **保留**：包验证（含源码输入首读，约 0.2s）与清单漂移检查必须紧邻各自加载回合执行——`PluginRuntimeReviewRegressionTests` 的快照语义测试证明把验证提前到并行阶段会破坏「能力协商、入口与注册使用同一份已验证清单」的既有防御，不压缩。
- **计量而非前移**：引用集惰性初始化（169 个内嵌 DLL 哈希 + MetadataReference 构建）无法移出首个编译的关键路径（编译必须使用同一引用集，提前物化与其他工作重叠收益 ≤20ms）；已在编译事件中输出 `ReferencesMs` 供发布版实测归属。
- **结论**：warm 前置链路（浏览器运行时 18–25ms、容器构建 143–252ms、发现 15–17ms）均为必要且量级小；冷启动 3.1s 不可由本 change 结构性消除，由日志留证。

### 结论：4.2 发布实测（tasks 4.2，2026-10-24）

`publish-desktop.ps1` 发布版（outputs/seed-verify），同一缓存根，真实桌面启动（冷热差异照旧存在，只比较插件阶段）：

| 场景 | Database 完成 → 插件运行时就绪 | 说明 |
|---|---|---|
| 基线（desktop-startup-splash 实测，改动前发布版） | 约 8.0s（编译 4.4s 串行 + 前置 3.1s + 加载） | 全部重新编译 |
| 改动后首次启动（宿主更新、用户缓存身份失效） | **约 0.68s**（并行编译段 389ms） | 缓存未命中 → **种子命中**（两条「编译缓存种子命中」日志），零编译 |
| 改动后第二次启动 | **约 0.28s**（346ms 含网关前段） | 普通缓存命中（种子采用的代际以用户密钥重签后可正常命中） |

并行编译单独收益（无种子、源码迭代后首次启动，运行 C）：CP 3.1s + TRC 0.45s 并行 → 插件编译段合计约 3.2s（原串行 4.4s）。种子与并行叠加后，插件阶段在宿主更新后的首次启动不再出现编译耗时。
