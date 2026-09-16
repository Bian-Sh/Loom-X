## 1. 基础依赖与领域契约

- [x] 1.1 在 `LoomX/LoomX.csproj` 与测试项目中加入 Tomlyn 2.10.1 依赖，并确认 `dotnet restore` 成功且没有改变现有配置数据库路径
- [x] 1.2 定义 TOML 路径、受控值类型、读取/校验/写入结果和安全摘要契约，使用 `string[]` 表示路径，并以单元测试覆盖字符串、整数、浮点数、布尔值、数组和表值
- [x] 1.3 定义统一的敏感键识别与脱敏策略，覆盖 `key`、`token`、`password`、`secret`、`authorization` 等路径，并以测试确认原始值不进入工具结果或日志

## 2. TOML 文档读取与结构化编辑

- [x] 2.1 实现通用 TOML 文档服务的读取、路径查询和语法校验，统一使用 Tomlyn syntax parser/document model，并以测试覆盖嵌套表、数组表、dotted key、包含点号的键名和非法 TOML
- [ ] 2.2 实现 `set`、`delete`、`patch` 的内存候选文档编辑，将 JSON 输入转换为受控 TOML 值，并以测试确认注释、无关 section、未知字段和原有语义得到保留
- [ ] 2.3 实现 Read → Compare → Backup → Temp Write → Parse Temp → Atomic Replace → Parse Target 的事务式写入流程，临时文件与备份文件放在目标目录，并以测试确认 no-op 不创建备份
- [ ] 2.4 为 Windows 路径、空格、非 ASCII 字符、文件占用和替换失败增加安全处理与有限重试，并以失败回滚测试确认原文件内容保持不变且错误结果包含可恢复信息
- [ ] 2.5 为 TOML 服务补齐 `ILogger<T>` 结构化日志，记录操作类型、路径安全摘要、结果、错误类型和耗时，测试确认不记录完整文档、Secret、请求正文或响应正文

## 3. TOML Assistant 工具

- [ ] 3.1 注册 `toml.read`、`toml.get`、`toml.validate`、`toml.set`、`toml.patch` 和 `toml.delete` 的 `ToolDefinition` 与 JSON Schema，并以工具注册测试确认名称、参数和返回结构稳定
- [ ] 3.2 将 TOML 工具映射到既有风险等级：读取/查询/校验为 Read，设置/补丁为 Write，删除为 Destructive，并复用既有审批与取消机制完成权限测试
- [ ] 3.3 在工具边界应用路径校验、输入大小限制、敏感字段脱敏和安全错误摘要，并以测试确认异常输入不会写入目标文件或泄露敏感值

## 4. 结构化 AskUser Broker

- [ ] 4.1 定义 AskUser 请求、字段、选项、默认值、必填标记、影响摘要、取消状态和结构化结果模型，支持单选、多选、数字与自由文本，并以序列化/校验测试覆盖字段 id 冲突与非法选项
- [ ] 4.2 实现 `UserDecisionBroker` 的 request id 分配、pending 事件、`TaskCompletionSource` 等待、提交、取消、超时/会话取消和重复完成保护，并以并发与生命周期测试确认不会永久阻塞
- [ ] 4.3 注册 `assistant.ask_user` 工具并接入 AssistantService/AgentLoop，使工具调用可以暂停当前步骤、保留会话上下文并在用户提交后恢复，以集成测试验证提交、取消和页面关闭路径
- [ ] 4.4 在 AskUser 边界过滤 API Key、Authorization、完整请求正文和其他敏感内容，并以安全测试确认问题文本、选项、影响摘要和结果不会泄露敏感信息

## 5. AskUser 桌面端交互

- [ ] 5.1 在 AssistantViewModel 中订阅 Broker 的 pending request、提交、取消和页面卸载事件，维护当前会话状态，并以 ViewModel 测试确认关闭页面会取消所有等待请求
- [ ] 5.2 基于 `GlassDialogWindow` 风格实现 AskUser Dialog/ViewModel，渲染单选、多选、数字和自由文本字段，支持必填校验、默认值展示、提交和取消，并以 UI/视图模型测试覆盖各种字段组合
- [ ] 5.3 将 AskUser 的用户可见反馈接入 `ToastService`，区分提交成功、取消和错误状态，确认 Toast 不包含 Secret、Authorization、完整请求/响应正文或用户敏感输入

## 6. 资料收集与后续 Skill 约束

- [ ] 6.1 为后续 Client Skill 提供资料收集服务边界说明：优先使用 Assistant 模型已有搜索能力，其次使用现有 Browser Bridge，最后通过 AskUser 请求用户提供资料，并以文档测试确认流程不新增搜索 API Key
- [ ] 6.2 更新相关 Assistant/Browser 文档或 Skill 说明，明确登录、验证码、Cloudflare、JS challenge 等情况交还用户处理，禁止绕过网站安全机制，并确认本 Change 不引入 WebView、爬虫或第三方搜索 Provider

## 7. 验证与交付

- [ ] 7.1 编写并运行 TOML 服务、原子写入、敏感信息保护和失败回滚单元测试，确认新增测试全部通过
- [ ] 7.2 编写并运行 AskUser Broker、Assistant 工具接入、取消恢复和桌面端 ViewModel 的集成测试，确认提交、取消、页面关闭和会话停止均可收敛
- [ ] 7.3 运行 `dotnet test` 覆盖 `LoomX.Tests` 与现有测试，修复回归后确认日志、数据库路径和既有 Assistant 工具行为不变
- [ ] 7.4 运行 `openspec status --change structured-config-assistant-decisions --json`、`openspec validate structured-config-assistant-decisions --strict`，并检查任务勾选状态、变更范围和中文文档完整性

