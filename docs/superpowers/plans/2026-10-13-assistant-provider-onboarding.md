---
change: fix-assistant-browser-on-demand
design-doc: openspec/changes/fix-assistant-browser-on-demand/design.md
base-ref: 52cb57599083e43db664580453a225424747393c
---

# AI助手浏览器接入与测试补全计划

<!-- comet-task-authority: openspec/changes/fix-assistant-browser-on-demand/tasks.md -->

1—3已完成首次按需启动修复、554项助手回归与基础CUA验收。

4：隔离模型历史中的合成summary参数，保留可证明安全的ID与Skill标识，覆盖参数投影与敏感值不泄露测试。

5：纯空白片段不创建气泡；新建和历史会话100步。测试实时投影、正常正文空格、新建与恢复。

6：补全URL探测、Provider实际模型ID、模型接口摘要；模型测试复用ProviderTestService，遵从协议与Header覆盖，补边界和日志安全测试。

7：相关测试及全量回归、时间目录发布；用CUA控制内置AI接入ienvie.top，观测JSONL安全摘要，记录真实Provider与模型测试结论和连接释放。只处理本次产物，保留其他会话所有修改与outputs。

8：返回真实链接与控件，模型历史按schema隔离不完整摘要；Node DOM与.NET回归。

9：复用原生Network事件，限制自动化tab与摘要字段；导航前启用捕获，默认wait正确处理加载，空气泡处理保留缩进。

当前状态：用户已授权CUA代操作测试弹窗及增删测试密钥；真实接入验收继续，任务7保持待完成，软件已验证部分提交共享。

追加任务10：复用 AskUser 默认文本与单字段自由输入；同步密钥两种分支，验证取消与直接提交，CUA 按用户授权操作测试确认。追加任务11：核对现有 Provider CRUD，记录独立于权限模式的强制危险确认后续需求，保持未实现状态。

12：get_capabilities按当前注册表查询分类索引或指定工具schema。
13：Provider模型目录UI与AI共享抓取解析；只读获取、同步与模型URL局部更新；原本地设置及凭据保留。
14：普通请求真实接入并覆盖空、单个、多个密钥及自定义名称；源码版本与扩展实际重载分别核实。
15：AskUser增加明确命名用途，修复英文Key/Token术语误拦；保持通用与TOML严格策略，经过真实Broker验证。
