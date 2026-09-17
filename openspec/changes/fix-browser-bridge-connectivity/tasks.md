## 1. 规格与回归测试

- [x] 1.1 固化 Session 租约、Bridge 可重启、Extension 恢复、目标重同步和 Secret 边界规格。
- [x] 1.2 添加租约管理、Bridge 生命周期、Session 删除、正文 Secret 收割和 Extension/Skill 契约的失败测试。

## 2. Bridge 与 Session 生命周期

- [x] 2.1 实现可重复启动/停止的 BrowserBridge 与 Session ID 租约管理器。
- [x] 2.2 新增显式 Bridge 启停工具，向模型公开当前 Assistant Session ID，并在删除 Session 时尝试释放租约。
- [x] 2.3 移除 Gateway 对 Bridge 生命周期的控制，保留应用退出最终释放。

## 3. Extension 与安全边界

- [x] 3.1 实现 alarm + WebSocket 心跳、断线保留目标和 hello 目标快照重同步。
- [x] 3.2 将正文 Secret 收割集中到 .NET，完善 Provider `api_key_secret_ref` schema、后台 tab、CDP 截图和日志启动修复。
- [x] 3.3 更新 Browser Skill，规定 AI 按需申请/释放自己的 Session 租约。
- [x] 3.4 增加本地 HTTP 健康探针与 Extension WebSocket 静默预检，Bridge 离线时只重试、不累积连接拒绝错误。

## 4. 验证与交付

- [x] 4.1 运行定向测试、完整测试和 Release build，检查日志与持久化无明文 Secret。
- [x] 4.2 发布时间命名的独立桌面包，重新加载 Extension，验证多 Session 与重连矩阵。
- [x] 4.3 让 LoomX 小助手使用 Browser Bridge 完成 `Loomx` Provider 录入并验证。
- [x] 4.4 运行健康探针回环测试、Extension 契约测试、离线/在线脚本模拟、完整测试与 Release 发布，确认 Bridge 离线时不创建 WebSocket。

## 构建记录

- 执行方式：`direct`；TDD 模式按当前 Comet 状态为 `direct`，关键 bug 仍完成遮罩 Key 的 RED/GREEN 回归。
- 自动代码审查：`review_mode: off`。原因：继续既有单会话实现，且共享工作区存在其他 Session 改动；本轮追加健康探针 RED/GREEN、948 项串行完整测试、Release build、离线/在线 Extension 脚本模拟与独立发布包校验作为风险控制。