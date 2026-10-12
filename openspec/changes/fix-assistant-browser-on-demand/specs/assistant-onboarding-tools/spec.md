## ADDED Requirements

### Requirement: 工具历史不得误导模型构造新参数

隐藏参数摘要 SHALL 继续用于UI和落盘，但 MUST NOT 在Provider请求中伪装成summary工具参数。浏览器历史 SHALL 仅保留格式有效的会话标识；Skill历史 SHALL 仅保留实际已安装的分类与名称。用户输入、选择器、URL、凭据等任意值 MUST NOT 因此被公开。

#### Scenario: 隐藏摘要再次进入模型
- **WHEN** 历史工具参数被隐藏
- **THEN** 请求中的历史参数不包含合成summary键，模型收到按schema重新构造参数的系统策略

### Requirement: 空白正文不得创建气泡

正文开始前纯空白TextDelta MUST NOT 创建消息气泡；正文中的空格与换行 MUST 保留。历史与实时消息 SHALL 使用同一投影规则。

#### Scenario: 工具调用只有换行
- **WHEN** 模型仅输出换行并调用工具
- **THEN** UI显示过程块而不创建空正文气泡

### Requirement: 宿主会话使用100步上限

新建与恢复的宿主助手会话 MUST 使用100步模型调用上限，Harness通用默认值 SHALL 保持不变。

#### Scenario: 恢复旧会话
- **WHEN** 加载历史会话
- **THEN** 更新系统策略并应用100步上限

### Requirement: 提供独立网址可达性测试

loomx.test_url MUST 支持HTTP/HTTPS网址，拒绝内嵌凭据和非HTTP协议；使用HEAD，405/501时使用GET；MUST 不读响应正文或携带Provider凭据。结果 MUST 区分网络可达与HTTP成功，并包含状态码与耗时；日志 MUST 不包含查询凭据、Header值和响应正文。

#### Scenario: 目标返回401
- **WHEN** 目标返回401
- **THEN** reachable为true，http_success为false，状态码为401

### Requirement: 暴露实际模型列表与具体请求接口

test_provider SHALL 返回实际模型ID列表。get_model_api MUST 返回已配置模型真实协议、方法、主机、端口、路径、代理状态和Header名，MUST 不返回Key、Header值或正文。test_model MUST 遵循Provider的Responses/Chat协议设置，合并模型覆盖Header，复用统一执行与代理路径，验证响应结构，不得仅凭200宣称模型测试成功。

#### Scenario: Responses模型带Header覆盖
- **WHEN** Provider采用Responses且模型覆盖同名Header
- **THEN** 模型测试使用responses路径与覆盖Header，返回安全接口摘要

### Requirement: 页面与网络观测基于真实证据

browser.read MUST 返回可见链接实际href与可操作selector、控件标签/类型/禁用状态，MUST NOT 在通用控件摘要读取输入值。HTML模式指定selector时 MUST 包含根元素属性。网络观测 MUST 使用原生Network事件且仅限已登记自动化tab，不缓存请求/响应正文、Cookie等无关认证材料；MUST 在导航前启用捕获。默认wait没有selector时 MUST 等待页面加载，不使用空选择器。

#### Scenario: 导航到实际密钥页
- **WHEN** 页面真实链接href为/keys
- **THEN** 助手依据读取到的href操作，不推测/api-keys

#### Scenario: 普通用户页面发起请求
- **WHEN** 未登记为自动化目标的标签页产生网络事件
- **THEN** 事件不进入LoomX网络缓存

### Requirement: 密钥名称按实际列表收集

助手 MUST 根据真实密钥页读取全部名称（包含分页），已有密钥时 SHALL 使用一个 single_select 字段列出全部名称并启用 allow_custom_input；自定义名称非空表示创建新密钥，选项和自定义文本 MUST 分别保留。无密钥时 MUST 使用必填 text 字段和 default_text="LoomX"，明确提交后创建该名称的密钥并继续接入，MUST NOT 改为是/否确认。取消 MUST 停止创建。

#### Scenario: 没有密钥
- **WHEN** 页面真实密钥列表为空
- **THEN** AskUser 显示默认 LoomX 的名称输入，用户可直接提交或改名，提交确认创建并继续接入

#### Scenario: 存在多个密钥
- **WHEN** 页面存在多个或分页密钥
- **THEN** 读取全部真实名称，单选和自定义名称输入同页展示；复用选择项或按明确约定创建自定义名称

### Requirement: 按需查询当前能力与参数契约

loomx.get_capabilities MUST 从当前注册表查询工具能力；按分类返回简洁索引，指定 tool_name 时返回完整 schema，MUST 不读取配置凭据或启动 Chrome。

#### Scenario: 运行时增加能力
- **WHEN** 查询前注册表新增工具
- **THEN** 查询返回新工具及风险、描述和参数信息，而非过期的硬编码列表

### Requirement: AI 与模型页共享模型目录获取

AI 与 UI MUST 复用请求和解析入口，支持 Provider 的自定义模型 URL 和默认地址。fetch_provider_models MUST 只读；sync_provider_models SHALL 新增模型及更新远端元数据，MUST 保留已有模型、本地设置、凭据和网关绑定。set_provider_model_list_url MUST 仅更新该字段；空值恢复默认，MUST 不整体替换 Provider。

#### Scenario: 刷新已有模型
- **WHEN** 远端返回更新的模型元数据
- **THEN** 更新远端元数据并保留本地执行参数、模型启用状态、覆写地址、Header 和凭据

#### Scenario: 列表异常
- **WHEN** HTTP 失败、响应不能解析或没有可用模型
- **THEN** 返回安全状态摘要，不同步任何模型，不记录正文、URL 查询值或凭据