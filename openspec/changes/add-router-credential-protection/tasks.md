## 1. 契约程序集 LoomX.Plugin.Abstractions

- [x] 1.1 新建契约类库并加入解决方案
- [x] 1.2 定义 Manifest、Request/Response Extension、Pipeline 上下文与结果

## 2. Plugin Runtime

- [x] 2.1 实现插件目录发现、Manifest 验证与安全诊断
- [x] 2.2 实现独立 collectible ALC 与共享契约类型身份
- [x] 2.3 实现 Extension 注册、Pipeline 归属、排序与启停
- [x] 2.4 实现 ContinueOnError 与数据安全 FailClosed
- [x] 2.5 将生产扩展点收敛为 Router Request/Response，不侵入 Agent Session 或本地持久化

## 3. Credential Protection 插件

- [x] 3.1 实现 Plugin-owned 敏感规则与检测引擎
- [x] 3.2 实现 `{{LOOMX_CREDENTIAL_<Base32>}}` 结构化 token
- [x] 3.3 使用 SQLite 保存长期唯一 token 映射，原值 DPAPI 加密、SHA-256 唯一复用
- [x] 3.4 实现普通 JSON、嵌套 tool arguments 与 SSE 跨事件恢复
- [x] 3.5 注册 request、response 两个 FailClosed Extension

## 4. Router 生产边界集成

- [x] 4.1 Provider Request Pipeline 在外发前 token 化完整请求正文
- [x] 4.2 Provider Response Pipeline 在返回 Router 客户前恢复，并处理失效实体头
- [x] 4.3 内置助手仅通过共享 `IProviderExecutionPipeline` 获得保护
- [x] 4.4 外部 Agent Client 与内置助手的用户消息、Tool Result 等内容均在请求序列化后统一处理
- [x] 4.5 请求/响应数据安全处理失败时 fail closed
- [x] 4.6 Native Anthropic 网关请求统一进入共享 `IProviderExecutionPipeline`

## 5. 清理职责交叉

- [x] 5.1 删除 `SecretBoundary`，配置工具仅返回 `api_key_configured`
- [x] 5.2 删除 BrowserSecretVault/BrowserSecretHarvester 与 `api_key_secret_ref`
- [x] 5.3 将 `ToolArgumentSafety` 更名为 `ToolCallProjection`，仅保留工具协议公开投影职责
- [x] 5.4 将 `SensitiveKeyPolicy` 更名为 `AssistantContentPolicy`，仅保留 TOML 暴露与 AskUser 产品策略
- [x] 5.5 删除 `AssistantSessionStore.SecretLeakScan`
- [x] 5.6 移除 AgentLoop Tool Result、AssistantSessionStore persistence/history 对 Plugin Runtime 的依赖

## 6. 验证

- [x] 6.1 Credential token 跨引擎/跨重启稳定复用，SQLite 文件不含明文
- [x] 6.2 JSON、SSE content 与 SSE tool arguments 恢复测试通过
- [x] 6.3 验证 Tool Result 位于 Provider 请求正文时由 Router Request Pipeline 统一 token 化
- [x] 6.4 Router 相关测试、解决方案构建、PluginPlayground 与 OpenSpec strict validation 通过；全量测试仅出现 4 项既有 Avalonia Dispatcher 线程波动，单独复跑 4/4 通过

## 后续 TODO（本次范围外）

- [x] 实现 placeholder 受限归一化：仅 ASCII 大小写与 token 内部允许空白；跨网络 chunk/SSE event 延续候选状态，并修正普通长文本加半截 token 的长度误判。后续继续补 property-based test 与 fuzz test 扩展语料。
- [x] 实现 placeholder 完整性 Prompt：最终 Provider 请求含 placeholder 时强制临时注入 OpenAI/Ollama system、Anthropic system 或 Gemini systemInstruction；不得写入 Session/JSONL，不得作为 user message。产品 UI/插件说明中的显式披露随生命周期设置界面实现。
- [ ] 拆分 Credential Protection 生命周期：完整保护与兼容解析模式；暂停主动保护时继续解析历史 token，并强警告明文外发风险。
- [ ] 将 Credential Protection 定义为受保护的第一方系统插件或常驻 Reference Runtime；实现危险卸载流程、Vault 默认保留、Vault 独立销毁与更高级别确认。
- [ ] 增加生命周期迁移与兼容测试：禁用后历史会话继续有效、卸载后重装恢复、Vault 销毁后明确永久失效，并覆盖外部 Agent 保存引用无法穷尽扫描的产品提示。
- [ ] 为内置 Agent 增加 Anthropic 原生协议 `IModelClient`，使其可直接选择仅支持 Anthropic API Mode 的 Provider/Model；继续复用共享 `IProviderExecutionPipeline`，不经过对外 HTTP Server、Endpoint 鉴权或 Combo 路由。
