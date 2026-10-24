# Proposal

## Why

源码插件的编译缓存机制本身可用（2026-10-13 日志显示同一宿主内重启可命中 12 次），但宿主一重新构建/发布，缓存身份即失效，两个第一方插件每次启动都重新编译：2026-10-24 发布版实测 `loomx.credential-protection`（12 个源文件）编译 3.59s、`loomx.tool-result-compression`（22 个源文件）编译 0.82s，合计约 4.4s，全部落在启动闪屏的插件阶段，是启动耗时最大的单项（见 `desktop-startup-splash` 的启动耗时实测）。用户在宿主高频迭代下「每次启动都编译」。

## What Changes

- 为编译缓存身份增加可诊断性：缓存未命中时，日志 SHALL 明确指出身份的哪个组成部分发生了变化（引用集摘要 / 编译器身份 / 编译选项 / 插件输入），让「总是编译」的根因可直接从日志定位。
- 消除第一方源码插件在宿主常规发布后的重复编译：第一方插件的编译产物随宿主发布提供（发布期预编译种子或等效机制，方案见 design），运行时首次启动即复用，用户自建源码插件的运行时编译路径保持不变。
- 降低插件阶段的固定开销：并行编译相互独立的插件；定位并压缩「Database 阶段完成 → 首个编译日志」之间约 3.1s 的前置开销（DI 构建 / 浏览器运行时 / 插件发现 / 引用集初始化，需先插桩归属）。

## Capabilities

### New Capabilities

- 无。

### Modified Capabilities

- `source-plugin-runtime`：新增「编译缓存身份变化必须可诊断」与「宿主常规发布后第一方插件不得重复编译」两个需求；既有「内容寻址编译缓存」的正确性契约（输入变化即失效、不完整缓存不加载）保持不变。

## Impact

- `LoomX.PluginHost/SourcePluginCompiler.cs`（缓存命中/未命中诊断日志）
- `LoomX.PluginHost/SourcePluginInputs.cs`（身份组成部分的结构化输出）
- `LoomX.PluginHost/PluginRuntime.cs`（插件编译并行化）
- `LoomX.PluginHost/LoomX.PluginHost.csproj` / 发布目标（第一方插件预编译种子）
- `LoomX/LoomXHost.cs`（前置开销归属与压缩，视插桩结果）
- `LoomX.Tests/*`（缓存诊断与预编译种子的回归测试）

不改变缓存正确性契约与插件包布局约定；用户源码插件的编译、认证与安全边界不变。
