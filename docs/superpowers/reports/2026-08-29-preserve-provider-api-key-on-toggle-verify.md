# 验证报告：preserve-provider-api-key-on-toggle

## 总结

| 维度 | 结果 |
| --- | --- |
| 完整性 | 3/3 任务完成，无增量规格 |
| 正确性 | `ProviderEditorViewModel` 区分未编辑、替换和主动清空，避免自动保存覆盖既有密钥 |
| 一致性 | 符合 design.md；保留配置服务传入空字符串时的显式清除语义 |

## 验证证据

- `dotnet test OllamaHub.Tests/OllamaHub.Tests.csproj --no-restore`：40/40 通过。
- `dotnet build OllamaHub.Desktop/OllamaHub.Desktop.csproj --no-restore`：构建成功，0 错误。
- 回归测试覆盖空编辑框生成 `ProviderInput.ApiKey == null`、用户主动清空生成空字符串，以及加载已有密钥后仍保持未编辑状态。
- 已配置密钥的 UI Watermark 明确显示“已配置 API Key，输入新值替换”，避免将安全脱敏误判为 Tab 数据丢失。
- 已检查 Provider 更新服务：`null` 保持原保护值，显式空字符串仍按既有测试清除密钥。

## 安全检查

本次没有新增密钥、请求正文或日志输出，也没有改变 DPAPI 存储格式。

## 备注

Comet 自动构建推断不识别 .NET 项目，因此使用项目对应的 `dotnet build` 与 `dotnet test` 命令完成验证。
