# Loom-x 开发约定

## 设置数据库路径

- Loom-x 的配置数据库唯一使用 `%LOCALAPPDATA%\LoomX\LoomX.db`，活动库使用 `%LOCALAPPDATA%\LoomX\LoomX.Activity.db`。
- `%LOCALAPPDATA%\OllamaHub\OllamaHub.db` 和 `%LOCALAPPDATA%\OllamaHub\Activity.db` 仅作为首次启动迁移源，迁移成功或失败后均保留，不得作为正常运行时路径。
- 服务端、桌面端、命令行和测试中的运行时配置访问必须通过统一路径实现，不得使用 `AppContext.BaseDirectory`、当前工作目录或其他路径创建、读取或写入设置数据库。
- 修改数据库路径逻辑时，必须验证所有入口仍指向上述唯一位置，并避免静默创建第二份空数据库。

## 日志规范

- 业务代码、后台服务和 UI 运行诊断统一通过依赖注入使用 `ILogger<T>`；日志最终由 Serilog 写入 `AppDataPaths.LogDirectory`，供桌面端“控制台”实时查看。

- 在请求、任务、配置刷新等有意义的事件边界记录日志。函数成功完成记录 `Information`，可恢复或预期降级记录 `Warning`，操作失败或未处理异常记录 `Error`，仅开发诊断细节记录 `Debug`。

- 使用结构化消息模板，不要用字符串插值拼接字段。例如：
  
  ```csharp
  logger.LogInformation(
      "代理请求完成 {ProviderId}/{ModelId} {StatusCode} {ElapsedMs}ms",
      providerId,
      modelId,
      statusCode,
      elapsedMs);
  ```

- 捕获异常时必须把异常对象作为第一个参数传给日志框架，并保留能够定位事件的结构化字段。例如：
  
  ```csharp
  logger.LogError(exception, "模型请求异常 {ProviderId}/{ModelId}", providerId, modelId);
  ```

- 禁止使用 `Console.WriteLine`、`Console.Error.WriteLine`、`Debug.WriteLine` 记录运行诊断。`Program.cs` 中面向命令行用户的用法提示、操作结果和参数错误属于 CLI 交互输出，可以继续使用 `Console.Out` / `Console.Error`。

- 禁止记录 API Key、Authorization、自定义 Header 值、请求正文、响应正文、用户 prompt、图片或工具调用参数。只记录 Provider/Model 标识、协议、路径、状态码、内容类型、字节数、耗时等安全摘要。

- 不要逐 token、逐流式 chunk 或在无业务意义的高频循环中写日志。需要高频诊断时使用指标或采样后的 `Debug` 日志。

- 新增事件驱动、函数驱动或异常驱动的业务流程时，同步补齐能够判断开始、完成、降级和失败的日志；测试必须覆盖敏感信息不会进入日志。

## Credential Protection 开发约束

详细权威设计与验收场景见 `openspec/changes/add-router-credential-protection/`。修改 Router Plugin Runtime、Provider Request/Response Pipeline、Credential Protection、插件启停/卸载或凭据 Vault 时，必须同步阅读并遵守该 change；以下规则属于不可静默改变的开发约束：

- `{{LOOMX_CREDENTIAL_<20 位 Base32>}}` 是长期有效的不透明本地凭据引用。模型可能改写它，因此最终 Provider 请求含 placeholder 时，必须临时注入 system/developer 级完整性指令；这是 placeholder 协议必选项，不得提供独立关闭开关，不得退化成 user message，也不得污染 Agent Session 或会话 JSONL。产品必须明确披露该插件会修改发送给 Provider 的系统指令。
- Prompt 只降低模型改写概率，不是安全边界。恢复必须依赖确定性解析、SQLite 精确查表和 fail closed；只允许 ASCII 大小写及 token 语法内部明确空白的受限归一化，禁止全局删空格、Unicode/易混字符替换、缺字补全、编辑距离或其他模糊猜测。
- Credential Protection 只规范和替换 placeholder 自身，不修改外围 Markdown、JSON、Header、URL、Shell 或 Tool Call 语法。JSON 结构引号由解析/序列化管理；解析后仍属于字段值的引号是实际数据，由 Tool Schema、执行器或目标协议判断是否合法。
- “暂停主动保护”与“解析既有 placeholder”是不同生命周期。普通禁用只能停止新明文检测与 token 化，历史 placeholder 的识别、完整性 Prompt、归一化和恢复必须继续有效；同时必须强警告新请求及历史会话中的明文可能直接发送给 Provider。
- Credential Protection 属于受保护的第一方系统能力，不得无提示一键卸载。卸载 Runtime 必须强警告历史会话、外部 Agent 缓存、导出文件与备份中的引用会失效并要求二次确认；卸载默认保留 Vault。销毁 Vault 是独立、不可逆且更高风险的操作，必须单独确认，不得与卸载绑定。
- 流式恢复必须覆盖跨网络 chunk 和跨 SSE event 的 token；候选长度保护只计算实际未闭合 placeholder，未知、残缺、歧义、异常超长、无法解密或恢复后破坏 JSON 时不得猜测并按安全边界 fail closed。

## 桌面端 Toast 反馈

- 全局即时反馈统一使用注入的 `ToastService`，由 `MainWindow` 负责渲染和自动隐藏。
- ViewModel 中调用 `toastService.Show("消息", ToastLevel.Success|Info|Warning|Error)`；View 代码后置在剪贴板等 UI 操作完成后调用同一服务。
- Toast 只放用户可见的安全摘要，禁止包含 API Key、Authorization、自定义 Header、请求/响应正文、用户 prompt 或工具参数。
- 页面 `Status` 继续用于详细过程状态；Toast 用于复制、测试完成、保存完成等短暂结果反馈。

## GitHub 跨 Session 协作

- GitHub `origin` 是多个 Codex 项目共享代码和开发进度的唯一来源；本文件的修改必须提交并推送后，其他 session 才能读取到。
- 多个 Codex 项目可以共用工作目录和当前分支。每次开始开发前先执行 `git status --short --branch` 确认分支和工作区，再执行 `git pull --ff-only`。
- 开发时只改当前负责的模块和必要的测试、文档；发现其他 session 的未提交修改时，不覆盖、不重置、不清理。
- 完成功能并通过必要验证后，使用中文提交消息提交并及时 `git push`，让其他 session 可以继续同步。
- `git pull --ff-only` 因本地修改或分支分歧失败时，不要立即暂停或反复重试。先执行 `git fetch origin`，确认当前分支、远端跟踪分支和分歧原因；禁止自动执行 `reset`、`clean`、`stash` 或强制推送。
- 工作区干净且本地与远端分支分歧时，使用 `git merge origin/<当前分支>` 合并远端变更。必须阅读双方改动并理解其意图，在保留双方有效行为的前提下解决冲突，不得机械选择一侧或把冲突原样留给用户；合并后运行相关测试，再用中文提交消息提交合并结果并 `git push`。
- 若未提交修改阻止合并，先区分当前 session 的已完成修改与其他 session 的修改：可以提交当前 session 的完整改动后再合并；其他 session 的未提交修改必须原样保留。只有在无法安全判断归属或行为取舍时，才保留现场并向用户说明具体冲突和已完成的检查。
- 发生 Git 冲突时，解决后必须检查 `git status`、确认不存在残留冲突标记，重新测试，再提交并推送；不得以“等待用户决定”作为默认处理方式。
- 不删除其他 session 的未跟踪、ignored、`outputs/`、`.codegraph/` 或流程状态文件；只有用户明确要求时才处理。

## 主题相关

当开启了透明主题，使用 CUA 等形式截图得到的APP的颜色将不会是真实的APP配色，这需要纳入考量，不能武断、暴力的认定为是 APP 主题配色，这在 调试APP主题配色下其他UI元素可见性时有用
