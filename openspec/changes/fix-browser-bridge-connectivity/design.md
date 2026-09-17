## 根因

1. Browser Bridge 注册为 Gateway Host 的 hosted service，桌面端只初始化容器但不一定启动 Host；Gateway 停止又会释放容器，浏览器能力因此错误地依赖 Gateway 生命周期。
2. `BrowserBridge` 持有一次性的 `HttpListener`/`CancellationTokenSource`，只有 `Start()` 和最终 `Dispose()`，无法表达 Session 按需启停及停止后重启。
3. Chrome MV3 Extension 仅用 `setTimeout` 重连，WebSocket 无周期消息；Service Worker 休眠后不能可靠恢复。断线时还执行 `detachAllTargets()`，把传输故障扩大为自动化会话销毁。
4. 页面 Secret 识别临时落在 `background.js`，违反 Extension 只负责传输和 Chrome API 的边界；服务端收割器又只检查敏感字段名，不能处理正文。
5. `LoggingBootstrap` 同时配置 `shared: true` 与 `buffered: true`，Serilog 启动时拒绝该组合。

## 架构决策

### Session 租约

新增进程级单例 `BrowserBridgeLeaseManager`，内部维护去重的 Assistant Session ID 集合：

- `browser.bridge_start` 接收 `assistant_session_id`，加入集合；第一个租约启动 Bridge。
- `browser.bridge_stop` 由 AI 主动调用并接收 `assistant_session_id`，移除集合；移除后仍有租约则保持监听，为空才停止 Bridge。
- 重复 start/stop 幂等。
- 新建、切换、离开或关闭 Session UI 都不自动释放旧租约；正常释放只由 AI 主动调用 `browser.bridge_stop` 触发。
- 删除 Assistant Session 是意外残留的被动兜底：必须尝试释放该 ID；释放失败记录安全日志，但会话文件删除语义保持明确。
- 应用退出最终释放 Bridge，不依赖 Session 主动清理。

Assistant 系统提示词包含当前 Session ID，内置 Browser Skill 负责判断何时启停并把该 ID 传给工具。`session_id` 继续专指 Chrome target session，工具参数使用 `assistant_session_id` 避免歧义。

### Bridge 生命周期

`BrowserBridge` 支持 `Stopped → Listening → Stopped` 的重复循环：

- `StartAsync()`、`StopAsync()` 幂等并串行化。
- 停止时取消当前生命周期 token、关闭 WebSocket、停止 listener、等待监听/接收任务退出并清理服务端目标快照。
- 停止后可重新启动同一端口。
- `DisposeAsync()` 只负责最终停止和释放底层资源。
- 状态区分“正在监听”与“Extension 已连接”。

Gateway 初始化、启动和停止不得直接调用 Bridge 启停。

### Extension 恢复与目标重同步

- 已连接时每 20 秒发送 `Bridge.keepAlive`。
- 增加 `alarms` 权限与最低 Chrome 120；创建 30 秒周期 alarm，在脚本加载、安装、浏览器启动和 alarm 回调时调用幂等 `connect()`。
- WebSocket 断开只停止心跳并计划重连，不 detach 或清空自动化目标。
- `Bridge.hello` 附带当前目标快照；服务端握手时重建 `Targets`。
- 用户主动关闭 tab 或 `browser.close` 才移除目标。

### Secret 边界

- Extension 返回原始 `content/html/markdown`，不识别、不记录 Key。
- `.NET` `BrowserSecretHarvester` 同时处理敏感结构化字段和正文中的严格 Key 形态。
- 正文命中替换为安全占位符，明文立即进入 `BrowserSecretVault`，结果追加只含 `secret_ref`/类型提示的 `harvested_secrets`。
- 对话、Session JSONL、日志、Toast 和最终汇报不得出现明文 Key。

### 其他修复

- 自动化 tab 使用 `active: false`。
- 截图使用 `chrome.debugger` 的 `Page.captureScreenshot`。
- `loomx.create_provider` schema 公开 `api_key_secret_ref`。
- 日志 sink 保留 `buffered: true`，移除 `shared`。

## 错误处理

- 端口占用时 `browser.bridge_start` 返回安全错误码，不使桌面应用崩溃，也不把失败 Session 留在租约集合。
- Extension 未连接时 browser 操作返回 `browser_bridge_offline`；Skill 可等待 alarm 重连或提示用户检查扩展。
- 删除 Session 释放租约失败时记录 `Warning`，仍继续删除会话文件；应用退出执行最终清理。
- 所有日志只记录 Session ID、端口、状态和错误码，不记录页面正文、Key、请求头值或工具参数。

## 验证

- 单元测试覆盖租约去重、共享、最后释放停止、启动失败回滚和删除 Session 释放。
- 回环测试覆盖 Bridge 启停幂等、停止后重启、目标快照恢复和断线清理。
- 源码契约测试覆盖 alarm、心跳、不 detach、后台 tab、CDP 截图、Skill 启停规则和服务端正文收割。
- 运行 Assistant/Browser 定向测试、完整 `dotnet test` 与 Release build。
- 发布新的独立桌面包，重新加载 Extension，验证多 Session 共享、持续连接和所有启动顺序。
- 让 LoomX 小助手访问授权页面，以 `business_id=loomx`、显示名 `Loomx` 完成 Provider 录入和测试，并扫描数据库、日志和 Session 文件确认无明文 Key。
