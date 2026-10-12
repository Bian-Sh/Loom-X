# AI 助手按需连接 Chrome

## 问题与根因
新建会话或普通交流可能被模型当成浏览器初始化时机。实际代码中新建、恢复会话不调用 Bridge.StartAsync；唯一启动链为 browser.bridge_start → AcquireAsync → StartAsync。现有系统提示词、工具说明和 New-API Skill 只要求“需要浏览器”，未限定用户链接与新增 Provider 意图。

## 修复目标
由 AI 模型根据用户消息与上下文判定，只有用户提供链接、希望新增 Provider，且需要读取页面时才申请 Bridge 租约。意图不明确先澄清，已有完整资料直接接入。LoomX 不新增关键词或意图硬编码。保留现有主动停止、会话租约与扩展探测协议。

## 用户追加范围
使用内置AI实际接入 https://ienvie.top/dashboard，测试具体模型接口；缺失能力应补尽补。追加修复空气泡、提高模型步数至100、通过落盘对话观测实际执行。实测参数故障证据指向模型将隐藏历史摘要误作新调用参数；新出现的功能补全使工作流升级为full。

追加能力：按需查询工具契约；AI访问模型页同等获取/同步与自定义模型URL；AskUser明确凭据名称用途以修复术语误拦。新增/更新契约属于assistant-onboarding-tools与assistant-user-decisions。危险Provider操作强制确认另建后续需求，当前只记录。
