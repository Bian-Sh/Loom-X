# HANDOFF — stabilize-plugin-compile-cache

> 给接手本 worktree 的新会话：这是一个已立项、待实施的 openspec change。先读完本文件与 `openspec/changes/stabilize-plugin-compile-cache/` 下四份 artifact 再动手。

## 背景一句话

源码插件编译缓存机制本身可靠（同宿主二进制内重启可命中），但宿主重新构建/发布后缓存身份整体失效，两个第一方插件每次启动都重新编译约 4.4s，是启动耗时最大单项。

## 实测证据

**缓存机制在同身份下可用**（`loomx-20261013.log`）：首次编译后，进程重启 12 次全部 `编译缓存命中`。

**宿主更新后全部失效**（`loomx-20261024.log`，新发布宿主首次启动）：

```
00:22:51.105  桌面启动阶段完成 Database
00:22:54.195  源码插件编译开始 loomx.credential-protection 12   ← 前置开销 ~3.1s 未归属
00:22:57.780  源码插件编译完成 loomx.credential-protection      ← 3.59s
00:22:58.257  源码插件编译开始 loomx.tool-result-compression 22
00:22:59.075  源码插件编译完成 loomx.tool-result-compression    ← 0.82s（串行）
00:22:59.109  插件已加载 ×2，插件运行时就绪
```

**缓存键组成**（`LoomX.PluginHost/SourcePluginInputs.cs:96`）：`v1 + 引用集摘要 + 编译器身份 + 编译选项 + Manifest + 全部源码字节`。引用集 = 发布期内嵌 169 个 DLL（167 框架 `net10.0|10.0.9` + 1 个 Plugin API，`SourcePluginReferences.cs:33`）；编译器身份 = Roslyn FullName + MVID（`SourcePluginCompiler.CompilerIdentity`）。宿主迭代时这两者任意一个翻转 → 所有插件重编译。当前日志只输出最终键，看不出哪个组成部分翻转——这是 tasks 1.1 要先解决的。

## 实施要点

- **先插桩定位（tasks 1.1-1.3）再动机制**：不知道主因是 Plugin API 引用变化还是 Roslyn MVID 翻转之前，不要选定预编译/稳定化方案。
- 主方案（design.md Decision 2）：发布期预编译种子，必须复用既有认证密钥与完整性机制，不得引入绕过身份校验的产物加载；发布期/运行期编译身份不一致时回退运行时编译。
- 缓存正确性契约（`openspec/specs/source-plugin-runtime/spec.md` 的「内容寻址编译缓存」「编译引用与本地化负载参与缓存完整性」）不可破坏。
- 两个插件串行编译可并行化（Decision 3），独立于预编译方案，可先行。
- 打包脚本约束：`scripts/publish-desktop.ps1` 要求发布目录有且仅有一个 LoomX.exe（其余为附属文件不限制），预编译种子落点需与 `FirstPartySourcePayload.targets` 布局约定一致（AGENTS.md「插件负载布局与命名」）。

## 验收标准

1. `dotnet test`（LoomX.Tests）全量通过，含新增的种子命中/输入变化重编译/产物损坏回退测试。
2. 发布版实测：宿主更新后首次启动，插件阶段无编译耗时或明显下降（基线 4.4s，对照记录写入 tasks 4.2）。
3. 缓存未命中时日志可指明翻转的身份组成部分。

## 常用命令

```bash
dotnet build LoomX.slnx -c Debug        # Git Bash 下注意用 dotnet.exe
dotnet test LoomX.Tests/LoomX.Tests.csproj -c Debug --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-desktop.ps1
日志：C:\Users\BianShanghai\AppData\Local\LoomX\logs\
插件缓存目录：C:\Users\BianShanghai\AppData\Local\LoomX\plugin-cache\source\
```

## 分支

`feature/20261024/stabilize-plugin-compile-cache`（基于 master ab8ca28，含本 change 的完整 planning artifacts）
