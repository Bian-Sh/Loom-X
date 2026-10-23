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

## Open Questions

- 发布期预编译种子在 `publish-desktop.ps1` 单产物校验（发布目录必须只有 LoomX.exe？不——脚本只校验 exe 数量）与现有 `FirstPartySourcePayload.targets` 布局中的落点，由实现会话结合打包脚本确认。
