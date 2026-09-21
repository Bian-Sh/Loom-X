## 1. 契约程序集 LoomX.Plugin.Abstractions

- [x] 1.1 新建 `LoomX.Plugin.Abstractions` 类库（net10.0）并加入 `LoomX.slnx`，验证 `dotnet build` 通过
- [x] 1.2 定义插件 Manifest 模型、Extension 接口（Request / ToolResult / Persistence 三类扩展点）、Pipeline Entry 与执行上下文/结果模型，验证契约库不包含对 LoomX 与 LoomX.Harness 的项目引用（只依赖 BCL）

## 2. Plugin Runtime（LoomX.PluginHost）

- [x] 2.1 新建 `LoomX.PluginHost` 类库并加入解决方案，实现插件目录发现与 Manifest 验证（缺少 id 或 extensions 的插件被拒绝并记录诊断、不影响其余插件），验证对应单元测试通过
- [x] 2.2 实现 AssemblyLoadContext 动态加载（每插件一个 collectible ALC，共享程序集经 Default 上下文解析），验证只引用契约类型的示例插件可加载并实例化
- [x] 2.3 实现 Extension 注册与 Pipeline 归属约束（单插件可注册多个 Extension，同一 Extension 仅属一个 Pipeline），验证重复挂载被拒绝的测试通过
- [x] 2.4 实现 Pipeline Runner 有序执行与启用/禁用（禁用 Entry 被跳过，重新启用后按配置顺序恢复），验证多插件同 Pipeline 按配置顺序执行的测试通过
- [x] 2.5 实现错误隔离与失败策略（普通 Entry 失败记录诊断并继续；数据安全类 Entry 失败返回 Blocked 且原始数据不放行），验证两类失败策略的测试通过

## 3. Credential Protection 第一方插件

- [x] 3.1 新建 Credential Protection 插件项目（仅引用契约库），声明 tool-result/persistence 扩展与 credential.detect、credential.mask 能力，验证其 Manifest 通过 Runtime 发现并验证
- [x] 3.2 实现检测规则引擎（敏感名称集、值形态正则、自由文本内容检测；内置基线迁移自 SensitiveKeyPolicy 语义），验证 sk-/Bearer/JWT/敏感字段名/自定义 Header 值用例全部命中且普通业务数据不误判
- [x] 3.3 实现规则 Plugin-owned 持久化（插件自有 JSON 文件，支持规则启用/禁用/增删），验证新增自定义规则后对后续数据生效且不写宿主核心配置
- [x] 3.4 实现脱敏替换与持久化清理（固定占位符 `***`，输出不含原值任何片段），验证含 API Key 的 JSON 输出不含原值
- [x] 3.5 实现插件侧 fail closed（处理失败时阻止原始数据并返回安全失败），验证处理失败用例不放行原始数据

## 4. Pipeline 挂载到现有 Router 边界

- [x] 4.1 在 `AgentLoop` 的 ToolResult 安全化之后、`session.AddMessage` 之前挂载 Tool Result Pipeline，验证含明文 Key 的工具结果进入会话历史前被脱敏，且 Pipeline 为空时行为与现状一致（现有 AgentLoop 测试全绿）
- [x] 4.2 在 `AssistantSessionStore.SaveAsync` 写入前挂载 Persistence Pipeline（保留现有 SecretLeakScan 兜底），验证落盘 JSONL 不含 sk-/Bearer 形态且现有会话存储测试全绿
- [x] 4.3 在宿主启动时完成插件发现、加载与 Pipeline 注册（DI 接入），验证启动日志包含插件加载摘要且不含敏感值

## 5. LoomX.PluginPlayground 验证

- [x] 5.1 新建 `LoomX.PluginPlayground` 控制台项目（引用 PluginHost、Abstractions 与 Credential Protection 插件），验证 `dotnet run` 可完成插件发现、加载与执行演示
- [x] 5.2 在 Playground 中模拟 Tool Result 与 Persistence 两条 Pipeline，验证 Sensitive Data 插件对含 Key 样例数据脱敏、Entry 排序调整生效、禁用生效、异常隔离与 fail closed 生效

## 6. 集成验证

- [x] 6.1 运行全量 `dotnet test`（Avalonia UI 线程相关既有失败除外并记录原因），验证 `dotnet build` 无新增警告
- [x] 6.2 端到端验证：构造含明文 API Key 的助手工具结果，确认会话历史、UI 展示与 JSONL 落盘均为脱敏后内容