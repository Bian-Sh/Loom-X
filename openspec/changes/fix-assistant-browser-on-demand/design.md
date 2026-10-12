# 设计

收紧 AssistantService 系统提示、browser.bridge_start 工具说明和 New-API Skill 的同一触发条件。提示模型禁止因新建或恢复会话、普通对话、仅收到链接、查看或修改已有 Provider 而连接 Chrome。判断由模型执行，工具处理器与通信协议沿用现有实现。

通过提示词与工具说明回归测试守住模型可见约束；使用生命周期替身验证新建、恢复、普通对话不会直接启动。历史会话载入时刷新系统策略。浏览器已启动后仍由 AI 在任务完成时调用 bridge_stop；不改变会话切换租约策略。

验证后重新发布 Windows 自包含桌面包至 outputs 时间目录，并用 CUA 检查应用新建会话无 Bridge 监听。真实模型语义判断具有不确定性，自动测试仅验证约束下发和本地生命周期，不声称证明所有模型都遵从。

## 追加设计
保持AI语义判断启动条件。隐藏工具参数摘要继续用于UI与JSONL，但不以summary字段伪装成模型调用参数；保留经过格式验证的会话ID与实际Skill目录标识，并在系统指令说明历史不是调用模板。纯空白TextDelta在未开始正文时不创建气泡。将宿主会话默认与恢复步数统一设为100。

补充loomx.test_url（HEAD，405/501时GET，只返回状态与耗时，不读正文）、loomx.get_model_api（已配置模型的实际请求路径、协议、安全Header名摘要）、test_provider返回真实模型ID。具体模型测试复用ProviderTestService处理Responses/Chat、代理、模型Header覆盖与响应结构验证；Ollama沿用原生请求。全部输出不含凭据、Header值或请求/响应正文，测试证明敏感信息不会进入公开结果。

实测达到步骤上限后Bridge仍保持监听，因此增加异常运行兜底：仅在runSession.State为Failed/Cancelled时释放该运行快照的Session租约，不操作其他会话，不按UI当前Session取ID。正常完成仍要求AI主动释放。

追加页面观测：默认Page.read仅返回可见文字，AI因缺少href猜测/api-keys，而用户指出真实路由为/keys。改为返回通用links与controls（真实href/selector，最多各60项，不读取输入值），带selector的HTML读取包含根元素属性。模型历史按当前schema筛除摘要字段，缺少顶层必需参数的投影不当成原始输入。使用无第三方依赖的Node DOM模拟执行真实background.js回归，并需重载实际Chrome扩展完成端到端验证。

网络观测根因：content脚本默认隔离世界，后台Runtime.evaluate读取页面世界的window变量，初始XHR也早于document_idle；实际browser.network无法提供线索。改为在Service Worker引入content.js纯事件缓存，由chrome.debugger.onEvent记录已登记自动化tab的XHR/Fetch摘要；不注入任何普通用户页面，不缓存正文，不采集Cookie/Set-Cookie/Proxy-Authorization。先创建about:blank、attach、Network.enable、登记、再导航以覆盖初始请求。默认wait没有selector时等待document.readyState，不调用空选择器。纯空白正文延迟显示但缓冲同一消息的开头缩进。扩展版本0.1.2。

密钥交互复用现有 AskUser schema、DefaultText 初始化与选择题同页输入，不新增站点专用判定器或弹窗体系。模型依据真实列表分支，有密钥读取全部名称（含分页）；无密钥直接默认 LoomX，明确提交的创建影响。选择与自由输入保持各自结果，自定义名称非空时创建，否则复用选择项。Provider 增删改查已存在；权限模式之外的强制危险操作确认另建 enhance-assistant-provider-safety，按用户要求留待后续实现。

2026-10-13 补充：用户要求流程优化由 Skill 编排，系统提示只保留通用加载指引，不将密钥业务分支放进用户提示。新增 loomx.get_capabilities 按当前注册工具分类查询，指定工具时返回详细 schema；模型页的获取能力抽取统一抓取与解析入口，AI 提供只读获取、同步及局部 model_list_url 修改，避免整体替换误清除配置。危险 Provider 管理强制确认仍为独立后续需求。

实测中 Sensenova 重复 AskUser invalid_field 并缩减为 Q/a/b。根因是内容策略拦截英文 Key/Token 术语，误伤取名和名称列表。字段 purpose=credential_name 作为结构化命名用途，只支持单行文本或单选，最多200字；单字段请求允许名称术语，已知凭据、认证赋值和本地引用仍拒绝，一般询问与 TOML 策略不放宽。Skill 声明用途，宿主不从用户语言判断业务意图。旧调用兼容。
