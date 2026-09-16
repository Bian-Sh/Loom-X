## Purpose

为桌面 AI 助手提供可靠、可审计且与客户端无关的 TOML 结构化读写能力，避免使用 Shell、字符串替换或整文件重建破坏用户现有配置。

## ADDED Requirements

### Requirement: TOML 文件必须支持结构化读取与路径访问
系统 SHALL 提供对指定 TOML 文件的读取、合法性验证和 nested path 查询能力，路径访问 SHALL 能区分不存在路径与值为 null 的情况，并 SHALL 支持字符串、整数、浮点、布尔、数组和表等 TOML 类型。

#### Scenario: 读取嵌套配置
- **WHEN** 调用方读取包含 `[model_providers.loomx]` 的 TOML 文件并查询 `model_providers.loomx.base_url`
- **THEN** 系统返回该字符串值及其 TOML 类型，不需要调用方解析原始文本

#### Scenario: 验证非法 TOML
- **WHEN** 调用方验证包含未闭合字符串或非法表结构的 TOML 文件
- **THEN** 系统返回失败结果、可定位的解析错误，并且不修改该文件

### Requirement: TOML 修改必须使用结构化 Patch
系统 SHALL 提供 set、delete 和批量 patch 操作，支持 nested path，并 SHALL 只修改目标字段而保留无关 section、未知字段、注释和原文件中可保留的格式信息。

#### Scenario: 修改嵌套字段并保留无关配置
- **WHEN** 对已有 TOML 设置 `model` 和 `model_providers.loomx.base_url`
- **THEN** 目标字段被更新，无关字段和无关 section 保持可解析且语义不变

#### Scenario: 删除指定字段
- **WHEN** 删除一个存在的 nested path
- **THEN** 仅删除该 path，父表和其他兄弟字段仍然存在

#### Scenario: 批量 Patch 原子应用
- **WHEN** 批量 Patch 中任一操作的路径或值类型非法
- **THEN** 整批操作失败，原文件内容保持不变

### Requirement: TOML 写入必须遵循备份、验证和原子替换
系统 SHALL 在修改既有文件前创建可定位的备份，在写入前验证候选文档，在同目录完成临时文件写入与原子替换，并在写入后再次验证；写入失败不得留下半写入的目标文件。

#### Scenario: 成功修改既有文件
- **WHEN** 对存在的 TOML 文件执行有效 Patch
- **THEN** 系统先保存备份，再原子替换原文件，并返回备份路径、写入路径和验证成功状态

#### Scenario: Windows 路径包含空格
- **WHEN** TOML 文件路径或生成文件路径包含空格和非 ASCII 字符
- **THEN** 系统按文件系统路径处理，不依赖 Shell 转义，且能完成读取、备份、写入和验证

#### Scenario: 写入阶段失败
- **WHEN** 临时文件写入、验证或替换阶段发生失败
- **THEN** 系统保留原文件内容，返回失败原因，并不报告修改成功

### Requirement: TOML 工具输出和日志必须保护敏感信息
系统 SHALL 对包含 key、token、password、secret、authorization 等敏感路径的读取结果进行脱敏，且 SHALL 不记录完整 TOML 文档、请求正文或敏感值。

#### Scenario: 读取敏感配置
- **WHEN** TOML 中包含 `env_key`、`api_key`、`token` 或 `password` 字段
- **THEN** 工具输出只返回安全摘要或脱敏占位符，原始敏感值不进入助手上下文

#### Scenario: 操作失败日志
- **WHEN** TOML 解析或写入失败
- **THEN** 日志包含操作类型、路径安全摘要和错误类型，但不包含文件完整内容或敏感值
