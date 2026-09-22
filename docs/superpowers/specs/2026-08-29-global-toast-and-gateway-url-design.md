# 全局 Toast 与网关地址交互设计

## 目标

- 网关左侧 Endpoint 卡片展示完整公开 URL。
- 点击完整 URL 写入系统剪贴板，并显示“地址已复制”反馈。
- URL 悬停时显示“点击复制地址” Tooltip。
- 网关右侧面板顶部移除 URL 文本和复制按钮。
- 建立可供代理测试、Provider 连通性测试等操作复用的全局 Toast。

## 方案

桌面端新增 `ToastService`，通过事件发布短消息。`MainWindow` 持有服务并负责唯一的 Toast 视觉承载：右下角显示消息，按级别设置视觉样式，默认约 2.5 秒后自动隐藏；新消息会替换当前消息并重新计时。页面 ViewModel 通过构造函数注入同一个服务，View 代码后置在需要系统剪贴板的交互完成后调用服务。

Toast 服务不保存业务状态，也不写入日志；调用方负责传递不含密钥、正文等敏感信息的用户可见摘要。业务 `Status` 文本继续保留，用于页面内的详细过程状态，Toast 只承载即时操作结果。

## 网关交互

`GatewayEndpointEditorViewModel.PublicUrl` 继续由服务端基础地址和 Endpoint 路径组合。左侧卡片使用无边框按钮承载 URL 文本，点击时根据按钮自身 DataContext 复制对应 Endpoint，而不是依赖当前选中项。复制成功后调用注入的 `ToastService` 显示成功消息；剪贴板不可用时不抛出 UI 异常。右侧面板标题区只保留 Endpoint 名称。

## 复用约定

需要反馈一次性操作结果时调用：

```csharp
toastService.Show("代理连接正常", ToastLevel.Success);
toastService.Show("代理测试失败", ToastLevel.Error);
```

禁止把 API Key、Authorization、请求正文、响应正文或用户 prompt 传给 Toast。长流程仍使用页面 `Status`，完成或失败时可额外发 Toast。

## 验证

- 构建 `OllamaHub.Desktop`，确认 XAML 和事件订阅编译通过。
- 运行现有测试项目，确保 ViewModel 构造函数兼容且既有行为不回归。
- 静态检查网关右侧不再包含 URL 或复制按钮，左侧绑定完整 `PublicUrl` 并配置 Tooltip。
