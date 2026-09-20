# 问题

点击提供商页面的“新增 Provider”按钮后，应用因未处理异常直接退出。

# 根因

新增 Provider 的 `BaseUrl` 初始为空。选中该草稿 Provider 时，`ProviderTestPanelViewModel.BindProvider` 会立即刷新测试请求摘要，并把空地址传入 `ProviderTestService.ResolveEndpoint`；底层以 `UriKind.Absolute` 构造 URI，抛出 `UriFormatException`，异常沿 UI 命令调用栈未被处理，最终终止进程。

# 修复目标

- 新建 Base URL 尚未填写的 Provider 时不再崩溃。
- 测试请求摘要在 Base URL 为空或编辑过程中暂时无效时安全降级。
- Base URL 有效时继续展示现有完整请求地址，不改变真实请求的地址校验语义。

# Capabilities

不新增或修改 capability；本次恢复既有交互的稳定性，使用 `skip_specs: true`。

# 影响

仅影响提供商测试面板的请求摘要预览与对应回归测试，不修改数据库、公开 API 或真实 Provider 请求执行路径。
