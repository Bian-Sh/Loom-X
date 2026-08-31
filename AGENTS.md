# OllamaHub 开发约定

## 设置数据库路径

- OllamaHub 的设置数据库唯一使用 `%LOCALAPPDATA%\OllamaHub\OllamaHub.db`。
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

## 桌面端 Toast 反馈

- 全局即时反馈统一使用注入的 `ToastService`，由 `MainWindow` 负责渲染和自动隐藏。
- ViewModel 中调用 `toastService.Show("消息", ToastLevel.Success|Info|Warning|Error)`；View 代码后置在剪贴板等 UI 操作完成后调用同一服务。
- Toast 只放用户可见的安全摘要，禁止包含 API Key、Authorization、自定义 Header、请求/响应正文、用户 prompt 或工具参数。
- 页面 `Status` 继续用于详细过程状态；Toast 用于复制、测试完成、保存完成等短暂结果反馈。

## GitHub 跨 Session 协作

- GitHub `origin` 是多个 Codex 项目共享代码和开发进度的唯一来源；本文件的修改必须提交并推送后，其他 session 才能读取到。
- 多个 Codex 项目可以共用工作目录和当前分支。每次开始开发前先执行 `git pull --ff-only`，确认工作区状态后再修改。
- 开发时只改当前负责的模块和必要的测试、文档；发现其他 session 的未提交修改时，不覆盖、不重置、不清理。
- 完成功能并通过必要验证后，使用中文提交消息提交并及时 `git push`，让其他 session 可以继续同步。
- `git pull` 因本地修改或分支分歧失败时暂停开发并报告，不自动执行 `reset`、`clean`、`stash` 或强制推送。
- 发生 Git 冲突时保留冲突现场，说明各版本的行为差异，由用户决定取舍；解决后必须重新测试再提交。
- 不删除其他 session 的未跟踪、ignored、`outputs/`、`.codegraph/` 或流程状态文件；只有用户明确要求时才处理。
