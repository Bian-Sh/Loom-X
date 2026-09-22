## Purpose

定义 OllamaHub 仅以桌面端应用交付和运行，避免向用户或自动化流程继续暴露已废弃的独立命令行入口。

## ADDED Requirements

### Requirement: 桌面端是唯一的受支持入口
系统 SHALL 仅将 `OllamaHub.Desktop` 作为 OllamaHub 的应用工程和发布目标。桌面应用 SHALL 在同一进程中托管本地网关，并继续提供桌面 UI 所依赖的本地网关行为。

#### Scenario: 构建并发布桌面端
- **WHEN** 维护者构建解决方案并发布桌面项目
- **THEN** 解决方案仅包含桌面工程和测试工程，发布目录仅提供 `OllamaHub.Desktop` 应用入口，且不生成独立的 `OllamaHub.exe`

### Requirement: 不再提供 CLI 参数命令
系统 SHALL 不再提供旧 `OllamaHub.exe` CLI 入口、`SetApiKey` 命令或其他通过该旧可执行文件传入参数的启动命令。

#### Scenario: 检查受支持的启动方式
- **WHEN** 用户查阅项目的当前使用说明或发布目录
- **THEN** 用户只能获得桌面应用启动方式，且找不到旧 CLI 参数命令的可执行入口或使用说明
