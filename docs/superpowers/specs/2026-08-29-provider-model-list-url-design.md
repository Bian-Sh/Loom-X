# Provider 模型请求 URL 与 API Key 输入设计

## 目标

在 Provider 页面的“模型” Tab 增加可选的模型列表请求 URL，并将其持久化为 Provider 级配置。该地址只服务于模型列表同步：填写时直接请求该地址，留空时根据 Provider Base URL 推导；聊天、补全和其他上游请求继续使用既有 Base URL/模型级 Base URL。

同时将 Provider 请求 Tab 的 API Key 输入改为带显示/隐藏操作的密码输入框，保持密钥默认不回显以及现有“清除已保存的 API Key”语义。

## 数据与 API 契约

- `ProviderEntity` 增加可空 `ModelListUrl` 字段，最大长度 2048。
- `ProviderInput`、`ProviderResponse` 增加 `ModelListUrl`；创建和更新时使用与 Base URL 相同的 HTTP/HTTPS 绝对地址校验及去尾斜杠规范化，空白值保存为 null。
- SQLite 初始化兼容已有数据库：在 `EnsureSchemaAsync` 中为旧 `Providers` 表补充可空列，新数据库由 EF 模型直接创建该列。
- API 响应只返回模型列表 URL，不返回任何 API Key 明文；原有密钥保护逻辑保持不变。

## 桌面端交互

- Provider ViewModel 增加 `ModelListUrl` 属性，并在响应映射、输入映射和自动保存链路中传递。
- “模型” Tab 将 URL 字段放在搜索/添加/同步工具栏上方：标签为“模型列表 URL（可选）”，占满可用宽度，水印为 `例如：https://api.example.com/v1/models`，辅助说明为“留空时根据 Base URL 自动推导；填写后仅用此地址刷新模型，不影响聊天请求。”。
- API Key 输入使用密码字符掩码，并提供眼睛图标按钮切换显示状态；切换只影响当前控件显示，不改变保存值或 `HasApiKey` 状态。桌面不再提供“清除已保存的 API Key”复选框，空输入会明确保存为无密钥；旧 API 调用传入 null 时仍兼容保留原值。若项目现有 Avalonia 版本不支持内置模板切换，则在 View 代码后置中以同一绑定源实现等价的双控件互斥显示。
- “同步模型”按钮接入现有 Provider 选中状态：使用 `ModelListUrl` 或 Base URL 推导地址，沿用 API Key、Headers、代理配置，并将成功/失败结果写入页面状态。同步响应仅用于刷新/合并模型目录，不自动覆盖用户已编辑的模型级配置。

## 请求与错误处理

- URL 为空时：若 Base URL 以 `/v1` 结尾请求 `{BaseUrl}/models`，否则请求 `{BaseUrl}/v1/models`，保持当前测试连接的推导规则。
- URL 非法在管理 API 保存阶段直接拒绝并显示“模型列表 URL 必须是 HTTP 或 HTTPS 绝对地址”。
- 同步请求遇到取消、网络异常或非 2xx 状态时，不清空现有模型列表，只更新状态文本；成功但响应格式无法解析时同样保留原目录并提示失败。
- API Key 显示/隐藏按钮提供 `ToolTip` 与无障碍名称，不记录密钥内容。

## 验证

- 配置服务测试覆盖：创建/更新 Provider 可持久化和返回模型列表 URL；空值保持 null；非法协议被拒绝；旧数据库初始化能补列。
- 桌面项目执行 `dotnet build`，配置测试执行 `dotnet test --no-restore`（若依赖已恢复）。
- 手动检查模型 Tab 布局、URL 字段绑定、API Key 显示/隐藏切换和同步按钮状态提示。
