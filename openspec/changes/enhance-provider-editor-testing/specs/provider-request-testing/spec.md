## Purpose

为 Provider 配置提供一个轻量、可观察且安全的真实模型请求测试器，用于验证协议、模型、代理、自定义请求头和 CLI 身份模拟在普通或流式推理请求中的实际效果。

## ADDED Requirements

### Requirement: 用户可以配置并发送真实模型测试请求
测试 Tab MUST 允许用户从当前 Provider 中选择模型、选择常规或流式模式并编辑发送内容；发送内容默认 MUST 为“每日一言”。系统 MUST 按当前 Provider 兼容类型构造并发送真实推理请求。

#### Scenario: 发送常规请求
- **WHEN** 用户选择启用模型、保留默认 Prompt 并以常规模式发送
- **THEN** 系统使用当前 Provider 配置发送一次非流式推理请求，并展示最终响应

#### Scenario: 发送流式请求
- **WHEN** 用户选择流式模式并发送请求
- **THEN** 系统持续追加可显示的文本片段，并在流结束后展示完整结果和响应元数据

#### Scenario: 没有可用模型
- **WHEN** 当前 Provider 不存在可选择的真实模型
- **THEN** 发送操作不可用，页面明确提示先同步或添加模型

### Requirement: 测试请求继承当前 Provider 的有效连接配置
测试请求 MUST 使用当前 Provider 的 Base URL、API Key、自定义请求头、代理开关和已应用的 CLI/UA 身份。请求摘要 MUST 显示协议、路径、模型、模式、代理状态、CLI 身份与版本、自定义 Header 数量和请求 ID，但 MUST NOT 显示 API Key、Authorization 或 Header 值。

#### Scenario: 使用代理和 CLI 模拟
- **WHEN** Provider 已启用代理并应用 CLI 身份
- **THEN** 测试请求通过有效代理设置发送并携带对应 CLI 身份请求头，摘要显示代理与 CLI 安全信息

#### Scenario: 自定义请求头包含敏感值
- **WHEN** Provider 配置了自定义请求头
- **THEN** 测试请求携带完整 Header，但 UI 摘要和运行日志仅显示 Header 数量而不显示值

### Requirement: 测试器提供专业的执行状态与响应信息
测试 Tab MUST 展示准备、发送、连接、完成、取消或失败状态，并在可用时展示 HTTP 状态码、耗时、内容类型、响应字节数和响应正文。用户 MUST 可以停止进行中的请求、清空结果、复制响应和重试最近一次请求。

#### Scenario: 请求成功
- **WHEN** 上游返回成功响应
- **THEN** 页面显示完成状态、HTTP 状态码、耗时、响应大小和可复制的响应内容

#### Scenario: 请求失败
- **WHEN** 上游返回 401、404、429、5xx、无效协议响应或发生网络超时
- **THEN** 页面显示安全错误摘要和可用元数据，并提供重试操作

#### Scenario: 用户停止请求
- **WHEN** 用户在请求进行中点击停止
- **THEN** 当前请求被取消，页面显示已取消且不将取消视为未处理异常

#### Scenario: 切换 Provider
- **WHEN** 用户在测试请求进行中切换到另一个 Provider
- **THEN** 系统取消旧请求并清空旧 Provider 的测试上下文，避免响应串入新 Provider

### Requirement: 测试器保护敏感内容并控制展示成本
业务日志 MUST NOT 记录 API Key、Authorization、自定义 Header 值、Prompt、响应正文或流式片段。响应正文 MAY 在测试 Tab 中展示，但系统 MUST 对累计和渲染长度设置上限，避免异常上游响应导致 UI 无界增长。

#### Scenario: 记录测试请求日志
- **WHEN** 测试请求开始、完成、取消或失败
- **THEN** 日志只包含 Provider、Model、协议、路径、状态码、内容类型、字节数、代理状态和耗时等安全摘要

#### Scenario: 上游返回超长内容
- **WHEN** 普通或流式响应超过测试器展示上限
- **THEN** 页面保留受限长度内容并明确标记已截断，应用继续保持响应
