## Purpose

定义 Loom-X 在 Windows 登录后的可选自动启动行为，以及桌面应用跨运行周期恢复用户明确选择的网关运行意图。

## ADDED Requirements

### Requirement: 用户可以配置开机自启动

系统 SHALL 在设置页提供“开机时启动 Loom-X”开关，并将该选择持久化到当前 Loom-X 配置数据库。启用时系统 SHALL 为当前 Windows 用户注册 Loom-X 自启动项，关闭时 SHALL 移除该自启动项，且整个过程不得要求管理员权限。

#### Scenario: 启用开机自启动

- **WHEN** 用户开启“开机时启动 Loom-X”
- **THEN** 系统保存该设置
- **AND** 当前 Windows 用户下次登录时自动启动 Loom-X

#### Scenario: 关闭开机自启动

- **WHEN** 用户关闭“开机时启动 Loom-X”
- **THEN** 系统保存该设置
- **AND** 移除当前 Windows 用户的 Loom-X 自启动项

#### Scenario: 自启动注册失败

- **WHEN** 系统无法注册或移除当前用户自启动项
- **THEN** APP 继续运行
- **AND** 系统通过用户可见反馈和结构化日志报告失败
- **AND** 不得将失败报告为操作成功

### Requirement: 概览页操作持久化网关运行意图

系统 SHALL 持久化布尔值 `GatewayRunning`。只有概览页“启动网关”和“关闭网关”对应的用户操作函数 SHALL 翻转该值；网关健康检查、运行时状态变化、自动恢复和 APP 退出清理不得修改该值。

#### Scenario: 用户启动网关

- **WHEN** 用户在概览页执行“启动网关”
- **THEN** 系统将 `GatewayRunning` 设置为 `true`
- **AND** 调用网关启动流程

#### Scenario: 用户关闭网关

- **WHEN** 用户在概览页执行“关闭网关”
- **THEN** 系统将 `GatewayRunning` 设置为 `false`
- **AND** 调用网关停止流程

#### Scenario: 网关运行时状态自行变化

- **WHEN** 网关因健康检查失败、启动失败或其他非概览页用户操作而进入非运行状态
- **THEN** 系统不修改 `GatewayRunning`

### Requirement: APP 启动时恢复网关运行意图

系统 SHALL 在配置和桌面窗口初始化完成后读取 `GatewayRunning`。值为 `true` 时 SHALL 自动调用网关启动流程；值为 `false` 时 SHALL 保持网关停止。自动恢复不得再次写入 `GatewayRunning`。

#### Scenario: 恢复运行中的网关意图

- **WHEN** APP 启动且 `GatewayRunning` 为 `true`
- **THEN** 系统自动启动网关
- **AND** 不修改 `GatewayRunning`

#### Scenario: 恢复停止的网关意图

- **WHEN** APP 启动且 `GatewayRunning` 为 `false`
- **THEN** 系统不自动启动网关

#### Scenario: 自动恢复失败

- **WHEN** APP 根据 `GatewayRunning = true` 自动启动网关但启动失败
- **THEN** APP 继续运行并展示现有网关失败状态
- **AND** `GatewayRunning` 保持为 `true`
- **AND** 下次 APP 启动时仍会再次尝试恢复

### Requirement: APP 退出不覆盖网关运行意图

系统 SHALL 在 APP 退出时停止并释放当前网关资源，但该退出清理流程不得修改 `GatewayRunning`。

#### Scenario: 网关运行意图为开启时退出

- **WHEN** `GatewayRunning` 为 `true` 且用户退出 APP
- **THEN** 系统停止并释放本次运行的网关资源
- **AND** `GatewayRunning` 仍为 `true`

#### Scenario: 网关运行意图为关闭时退出

- **WHEN** `GatewayRunning` 为 `false` 且用户退出 APP
- **THEN** 系统完成退出清理
- **AND** `GatewayRunning` 仍为 `false`
