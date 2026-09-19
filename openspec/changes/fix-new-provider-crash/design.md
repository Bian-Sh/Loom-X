# 修复方案

在 `ProviderTestPanelViewModel.RefreshRequestSummary` 生成预览地址前先验证 `BaseUrl` 是否为绝对 URI。

- 地址有效：继续调用 `ProviderTestService.ResolveEndpoint`，保持现有协议路径与完整 URL 展示。
- 地址为空或暂时无效：摘要仅显示 `POST` 及现有代理、Header 等安全信息，不构造绝对 URI，因此不会抛出异常。

真实测试请求仍沿用 `ProviderRouteEndpointResolver.Resolve`，无效地址在真正发送时继续按现有逻辑失败；本修复只处理编辑态预览，避免掩盖请求配置错误。

回归测试直接绑定一个空 `BaseUrl` 的 `ProviderEditorViewModel`，先证明当前实现抛出 `UriFormatException`，再验证修复后能够生成降级摘要。
