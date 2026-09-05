## Why

当前核心网关代码作为独立 `OllamaHub` 工程和 CLI 入口存在，但产品仅需桌面端。双工程结构让依赖、构建和发布维护产生重复，也会继续暴露已不再支持的命令行启动方式。

## What Changes

- 将原 `OllamaHub` 项目的有效网关、配置、代理和契约源码纳入 `OllamaHub.Desktop`，保持桌面端同进程托管网关的现有运行方式。
- **BREAKING** 从解决方案、测试引用和发布物中移除独立 `OllamaHub` 工程、`OllamaHub.exe` CLI 入口及其参数启动命令。
- 删除旧项目目录，并让桌面端直接承载原核心代码所需的依赖和构建配置。
- 更新测试工程，使其仅引用桌面工程；保留对网关与配置行为的覆盖。

## Capabilities

### New Capabilities

- `desktop-only-distribution`: 定义桌面端为唯一受支持的应用入口与发布目标，且不再生成独立 CLI 可执行文件。

### Modified Capabilities

- 无。

## Impact

- 受影响的代码：`OllamaHub`、`OllamaHub.Desktop`、`OllamaHub.Tests` 和 `OllamaHub.slnx`。
- 受影响的构建与发布：桌面项目新增原核心项目的直接包依赖，发布仅产出桌面端。
- 受影响的接口：移除 `OllamaHub.exe` 与命令行参数启动兼容性；桌面 UI 和本地网关 HTTP 行为保持不变。
