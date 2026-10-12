# 验证报告

日期：2026-10-13（Asia/Shanghai）

## 当前结论

软件改动、完整测试与发布已通过，真实站点接入仍在验收中。用户已明确授权 CUA 提交测试弹窗、创建多个或删除测试密钥，无需再次询问是/否。只有存在真实 Provider/Model 测试调用与结果才判定接入完成；任务7、14保持未完成，暂不归档。

## 实现与根因

- 是否启动 Chrome 由模型根据链接、新增 Provider 意图与页面资料需要判断；新建、恢复、普通对话没有直接启动调用，不增加宿主关键词分类。
- 新建与恢复使用100步；失败、取消仅释放运行所属租约，正常完成由 AI 主动关闭本次页面并释放租约。
- 空气泡来自工具轮仅有换行的正文。实时投影延迟到非空白正文才创建气泡，保留正文开头缩进和内部空白。
- 隐藏参数摘要继续用于 UI/JSONL，模型历史按当前 schema 筛除 summary 和不完整投影，保留有效会话/Skill标识。
- test_url区分HTTP可达与成功，HEAD不支持时回退GET，不读取正文或附带Provider凭据。get_model_api给出真实协议、方法、主机与路径；test_model复用ProviderTestService校验实际响应结构和协议。
- browser.read提供真实links/controls与selector，HTML含所选根属性；不读取通用输入值。原生Network事件仅记录自动化tab，不缓存正文或Cookie等；导航前启用捕获。默认wait不查询空selector。
- get_capabilities按当前注册表提供分类索引与具体工具schema。fetch_provider_models只读获取真实远端元数据；sync_provider_models和模型页共享请求/解析，仅更新远端元数据、保留本地设置与凭据；set_provider_model_list_url仅更新该字段。
- 接入分支、默认LoomX、全部名称选择加同页自定义、验证和收尾均维护在Skill，普通用户只需给链接和接入目标。基础与通用Provider Skill的入口同步更新。
- 真实普通请求曾反复AskUser invalid_field，最后缩减到Q/a/b。根因为产品隐私策略把英文Key/Token名称术语也当成索取凭据。新增字段purpose=credential_name，限单行文本或单选、最多200字；允许名称术语，但拒绝可识别凭据、认证赋值、凭据引用和非法标识。一般询问与TOML策略保持严格。
- Provider增删改查已存在；独立于权限模式的强制危险确认缺失，按用户要求另建enhance-assistant-provider-safety，仅记录后续需求，未宣称实现。

## 自动化验证

- 最新完整.NET：1961通过，0失败、0跳过（2m30s）。命令：dotnet test LoomX.Tests/LoomX.Tests.csproj --no-build --no-restore --nologo -v quiet。
- 最新相关集成：656通过，0失败；覆盖助手、命名/Broker、配置和模型目录。
- Node真实扩展源码回归：7通过，0失败。命令：node --test LoomX.Tests/Assistant/browser-network.test.cjs LoomX.Tests/Assistant/browser-page-snapshot.test.cjs。
- RED证据覆盖按需启动、历史参数、空白/缩进、异常租约、DOM导航、原生网络、100步、能力查询、模型目录局部修改，以及名称用途缺失。名称用途还经真实Broker验证选择与custom_inputs同时保留，真实凭据提交被拒。
- Comet执行的一轮完整测试曾出现10项已有Junction/UI时序失败；代表用例单独复跑及后续直接完整1959/1961测试均通过。保留失败日志作为诊断，不把失败结果改写为成功。
- 原文件UTF-8/BOM确认并恢复；补丁引入的混合换行已修复。git diff --check通过。既有nullable/分析器警告未清理。

## 发布与实际验收

- 最新包：outputs/20261013-055937；绝对exe路径隐藏启动，使用--loomx-child，进程28648，窗口6032650。
- 启动与新会话时17831无监听；普通请求由模型按需启动。
- 早前实际HTTP200、/dashboard真实href导航到/keys，列表为空；默认LoomX文本输入已通过CUA显示与提交验证，但该轮依赖较详细提示，未算Skill普通请求完整通过。
- 新版普通请求使用Sensenova/sensenova-6.8-flash-lite，会话42fcb52fbad24d82b6e456c85dad01ac加载new-api/generic Skill并读真实页面。尚未确认最终Provider/模型调用成功，GUI验收继续。
- 用户指出另一个模型不可用后，已恢复Sensenova；后续禁止擅自切换。
- Chrome扩展源码版本0.1.3；不将源码版本等同于已核实浏览器重载。截图工具当前仅有base64文本，模型未获得真正视觉输入；使用DOM与CUA核对效果，不把截图调用当成模型看到了页面。
- 落盘观测只输出安全状态、工具名称、步骤和测试摘要，不打印密钥、Header值、正文或无关网页内容。

## 待完成

完成空列表默认取名、已有单个/多个名称与自定义输入的真实流程；实际创建并接入Provider、获得真实模型ID、执行get_model_api/test_model，关闭本次页面并释放租约。若出现具体缺失能力，补需求或实现并复验，不虚报完成。