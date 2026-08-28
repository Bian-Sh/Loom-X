## Why

数据库和运行日志当前存放在应用程序目录中，重新发布或替换应用目录时会丢失配置数据。运行时数据应存放在当前用户稳定且可写的系统数据目录中，与发布产物解耦。

## What Changes

- 将数据库固定存放到 `%LOCALAPPDATA%\OllamaHub\OllamaHub.db`。
- 将运行日志固定存放到 `%LOCALAPPDATA%\OllamaHub\logs\`。
- 服务端、命令行和桌面端统一使用同一套路径。
- 不读取、不复制也不迁移应用程序目录中的旧数据库或日志。

## Capabilities

### New Capabilities

无。本次仅修正现有持久化实现的位置。

### Modified Capabilities

无。配置和日志的业务行为、数据结构及外部接口不变。

## Impact

影响应用运行时路径解析、SQLite 初始化、Serilog 文件输出和相关说明文档；不修改数据库 schema、HTTP API 或依赖项。
