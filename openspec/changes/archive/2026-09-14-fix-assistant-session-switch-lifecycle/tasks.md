# 任务

## 1. 回归测试

- [x] 1.1 启用输出中切换会话的服务回归测试，补充 UI 忽略非当前会话事件的失败测试，并确认按预期失败。

## 2. 最小修复

- [x] 2.1 固定 `AssistantService.SendAsync` 的运行会话，并为 `AssistantViewModel` 增加当前展示会话投影边界。

## 3. 验证

- [x] 3.1 执行 Assistant 定向测试、完整测试、Release 构建，并重新发布 `win-x64` 到带可读时间的 `outputs/`。
