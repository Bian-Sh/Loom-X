# Provider 类型与设置 ORM 设计

## 范围

Provider 页面保留基础与请求配置：Provider 类型限定为 `openai`、`anthropic`、`ollama`，请求页移除重复的协议输入，新增 Provider 级 `UseProxy` 复选项。设置页 UI 暂不实现。

## SQLite 模型

新增单例 `AppSettingsEntity`，以强类型列保存通用、代理、更新和隐私设置；代理密码使用 DPAPI 保护后落盘。`ConfigurationDatabase.InitializeAsync` 创建数据库时确保存在默认设置行。

`ProviderEntity` 新增 `UseProxy`，并贯通输入/响应 DTO、管理服务和运行时配置快照。Provider 类型由管理服务统一校验为三个受支持值。

## 服务契约

`ConfigurationManagementService` 提供读取和更新应用设置的方法，更新时校验代理端口、代理模式、更新渠道和日志保留天数，并刷新运行时配置快照。桌面 `ConfigSnapshotService` 转发这些方法，供后续设置页使用。

## 验证

- SQLite 初始化会写入默认设置并可读回更新值。
- Provider 的 `UseProxy` 和类型值可创建、更新并从快照恢复。
- `dotnet build`、`dotnet test` 和桌面发布通过。
