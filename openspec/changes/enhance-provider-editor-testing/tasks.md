## 1. Provider 基础配置模型

- [x] 1.1 为兼容类型映射和旧配置反向解析编写失败测试，实现 OpenAI Chat、OpenAI Responses、Anthropic Messages 三种映射并验证测试通过
- [x] 1.2 为新 Provider 稳定唯一业务 ID 编写失败测试，实现自动生成与冲突保护，并验证名称和类型变化不会修改 ID
- [ ] 1.3 调整 Provider 编辑持久化属性与加载行为，验证已有 Provider ID、API Key 和旧协议配置仍可无损读取保存

## 2. 真实请求测试服务

- [x] 2.1 定义测试请求、进度、结果和错误 DTO，并用测试固定安全摘要、响应截断和取消语义
- [x] 2.2 为 OpenAI Chat、OpenAI Responses 和 Anthropic Messages 编写失败测试，实现普通请求构造、鉴权、自定义 Header 与响应解析
- [x] 2.3 为三种协议编写流式响应测试，实现文本增量归一化、完成状态、错误事件和长度限制
- [x] 2.4 接入 Provider 代理设置与 CLI 身份 Header，验证代理开关真实影响 HttpClient 且日志不包含密钥、Header 值、Prompt 或响应正文

## 3. 测试面板状态与命令

- [x] 3.1 新增独立 ProviderTestPanelViewModel，并验证模型默认选择、常规/流式模式、默认“每日一言”和无模型禁用状态
- [x] 3.2 实现发送、停止、重试、清空和复制响应流程，验证切换 Provider 会取消旧请求并清空上下文
- [x] 3.3 将测试面板接入 ProvidersViewModel 的选择和生命周期，验证自动保存中的内存配置可直接用于测试

## 4. Provider 页面与本地化

- [x] 4.1 重构基础 Tab 为名称、三种兼容类型卡片、Base URL 和 API Key，隐藏 Provider ID，并通过视图契约测试验证
- [x] 4.2 将请求 Tab 更名为高级，仅保留代理、自定义请求头与 CLI/UA 模拟，移除旧连接测试区块且保留顶部健康验证
- [x] 4.3 新增专业测试 Tab 的请求表单、安全摘要和终端式 Response 面板，验证发送/停止/重试/复制/清空绑定与响应状态可见性
- [x] 4.4 补齐 zh-CN、en-US 与 zh-TW 本地化资源，运行本地化覆盖和硬编码文案测试

## 5. 集成验证与交付

- [ ] 5.1 运行 Provider、测试服务、ViewModel、视图契约和敏感日志定向测试并修复失败
- [ ] 5.2 运行完整测试与 Release 构建，确认无编译错误、无新增警告回归且 OpenSpec 严格验证通过
- [ ] 5.3 使用 CUA 后台启动应用，验证四个 Tab、三种兼容选择、普通/流式测试、取消和错误展示
- [ ] 5.4 重新发布桌面应用到带可读日期时间的 `outputs` 子目录，验证发布包可启动且进程路径正确
