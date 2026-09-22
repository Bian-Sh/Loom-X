## 1. 契约程序集 LoomX.Plugin.Abstractions

- [x] 1.1 新建契约类库并加入解决方案
- [x] 1.2 定义 Manifest、Request/Response/ToolResult/Persistence Extension、Pipeline 上下文与结果

## 2. Plugin Runtime

- [x] 2.1 实现插件目录发现、Manifest 验证与安全诊断
- [x] 2.2 实现独立 collectible ALC 与共享契约类型身份
- [x] 2.3 实现 Extension 注册、Pipeline 归属、排序与启停
- [x] 2.4 实现 ContinueOnError 与数据安全 FailClosed

## 3. Credential Protection 插件

- [x] 3.1 实现 Plugin-owned 敏感规则与检测引擎
- [x] 3.2 实现 `{{LOOMX_CREDENTIAL_<Base32>}}` 结构化 token
- [x] 3.3 使用 SQLite 保存长期唯一 token 映射，原值 DPAPI 加密、SHA-256 唯一复用
- [x] 3.4 实现普通 JSON、嵌套 tool arguments 与 SSE 跨事件恢复
- [x] 3.5 注册 request、response、tool-result、persistence 四个 FailClosed Extension

## 4. 生产边界集成

- [x] 4.1 Provider Request Pipeline 在外发前 token 化请求正文
- [x] 4.2 Provider Response Pipeline 在返回本地调用方前恢复，并处理失效实体头
- [x] 4.3 AgentLoop 通过通用委托在 Tool Result 进入 Session/Event/UI 前执行 `tool-result` Pipeline
- [x] 4.4 AssistantSessionStore 每条 JSONL 写入前执行 persistence Pipeline，逐行加载时执行 response Pipeline
- [x] 4.5 所有处理失败 fail closed，不放行原始请求、响应、工具结果或持久化内容

## 5. 删除重复凭据保护

- [x] 5.1 删除 `SecretBoundary`，配置工具仅返回 `api_key_configured`
- [x] 5.2 删除 BrowserSecretVault/BrowserSecretHarvester 与 `api_key_secret_ref`
- [x] 5.3 将 `ToolArgumentSafety` 更名为 `ToolCallProjection`，仅保留工具协议公开投影职责
- [x] 5.4 将 `SensitiveKeyPolicy` 更名为 `AssistantContentPolicy`，仅保留 TOML 暴露与 AskUser 产品策略
- [x] 5.5 删除 `AssistantSessionStore.SecretLeakScan`，统一由 Credential Protection Pipeline 处理

## 6. 验证

- [x] 6.1 Credential token 跨引擎/跨重启稳定复用，SQLite 文件不含明文
- [x] 6.2 JSON、SSE content、SSE tool arguments、历史会话恢复测试通过
- [x] 6.3 Browser Tool Result 在统一 AgentLoop 边界 token 化，Provider 工具参数恢复后可直接使用 `api_key`
- [x] 6.4 全量测试（1232/1232）、解决方案构建、PluginPlayground 与 OpenSpec strict validation 全部通过
