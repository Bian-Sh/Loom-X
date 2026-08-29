# 修复设计

Provider 编辑器在生成 `ProviderInput` 时，将空白 API Key 规范化为 `null`。配置服务已将 `null` 定义为“未修改”，因此更新 Enabled、UseProxy 或其它字段时不会覆盖已有密钥；仍可由调用方传入空字符串触发清除逻辑。

回归测试先创建带密钥的 Provider，再以 `ApiKey: null` 更新 `Enabled`，断言响应仍报告 `HasApiKey`，随后重新读取数据库确认保护值仍存在。
