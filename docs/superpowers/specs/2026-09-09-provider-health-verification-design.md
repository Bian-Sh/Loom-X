# Provider 连接健康验证设计

## 1. 目标

将 Provider 页面顶部的“连接健康”从静态文案和配置完整度统计，升级为可解释的 Provider 连通性验证结果。验证结果需要同时驱动：

- 顶部健康摘要卡片；
- 左侧 Provider 列表的状态圆点和异常摘要；
- 右侧编辑器的连接状态详情；
- 单个 Provider 测试和全部 Provider 验证流程。

验证只证明模型列表接口可访问、认证通过且响应符合兼容协议，不自动发送聊天请求，也不保证每个模型的生成接口都可用。

## 2. 范围与非目标

### 2.1 范围

- 复用现有 `ProvidersViewModel`、`ProviderEditorViewModel` 和连接测试入口。
- 支持当前 Provider 单独测试和所有已启用 Provider 批量验证。
- 对配置、网络、认证、HTTP 状态和响应格式进行分层分类。
- 将瞬时验证结果同步到摘要卡、列表项和编辑器。
- 增加可测试的验证服务和 HTTP 假客户端测试。

### 2.2 非目标

- 不修改 Provider 配置数据库结构。
- 不把上次验证结果持久化为长期健康状态。
- 不做后台轮询或应用启动时自动请求所有上游。
- 不通过真实聊天请求验证模型，以避免费用、额度消耗和副作用。
- 不改变现有 Provider/Model HTTP API 契约。

## 3. 用户触发与生命周期

验证入口采用“手动全部验证 + 单个 Provider 测试”：

1. 页面首次加载、刷新或应用重启后，已启用 Provider 的状态为 `Unknown`（待验证），不自动访问上游。
2. 用户点击单个 Provider 的“测试连接”时，只验证当前选中 Provider。
3. 顶部健康卡片提供“全部验证”，验证所有已启用且配置完整的 Provider。
4. Base URL、ModelListUrl、API Key、Headers、Provider 类型或启用状态发生变化后，该 Provider 状态重置为 `Unknown`。
5. 验证结果只保留在当前 ViewModel 生命周期内；应用重新启动后不展示过期成功结果。
6. 单个验证可以取消或被新的验证替换；批量验证使用有界并发，避免瞬时打满上游。

## 4. 验证流程

```text
配置变化 -> Unknown
点击单个测试/全部验证 -> Checking
本地配置预检查 -> 请求模型列表接口
HTTP/响应解析/耗时分类 -> 更新 ProviderHealthResult
结果广播 -> 摘要卡、列表圆点、编辑器详情同步刷新
```

### 4.1 本地配置预检查

- Provider 未启用：状态为 `Disabled`，不发请求。
- Base URL 为空或不是 HTTP/HTTPS 绝对地址：状态为 `ConfigurationError`。
- ModelListUrl 已填写但地址非法：状态为 `ConfigurationError`。
- Headers 存在空名称或空值：状态为 `ConfigurationError`。
- 预检查失败时不发送网络请求，也不清空现有模型目录。

### 4.2 模型列表请求

- 优先请求 `ModelListUrl`。
- 未填写时沿用现有 Base URL 推导规则生成 `/models` 地址。
- 沿用当前 API Key、Headers、代理和 8 秒超时设置。
- 记录安全摘要：Provider 标识、协议、路径、HTTP 状态、响应字节数和耗时；禁止记录凭据、请求正文或响应正文。

### 4.3 响应判定

- `2xx` 且 JSON 可解析、包含 `data` 或 `models` 数组：认证和模型列表接口验证通过。
- `2xx` 但响应结构不兼容：`ProtocolError`。
- `2xx` 且模型数组为空：`HealthyEmptyModels`，连接正常但没有发现模型。
- `401/403`：`AuthFailed`，API Key 无效或权限不足。
- `404/405`：`EndpointError`，地址可访问但路径或协议不兼容。
- `429`：`RateLimited`，上游限流。
- 其他 `4xx`：`RequestRejected`。
- `5xx`：`UpstreamError`。
- DNS、连接、TLS、代理或超时异常：`Unavailable`。

验证不通过时保留已有模型目录，只更新状态和错误摘要。

## 5. 状态模型

内部状态和用户文案分离，状态点颜色保持稳定：

| 内部状态 | 列表状态点 | 用户文案 | 说明 |
| --- | --- | --- | --- |
| `Unknown` | 灰色 | 待验证 | 尚未进行本次运行时验证 |
| `Checking` | 蓝色/旋转 | 验证中 | 请求正在进行 |
| `Healthy` | 绿色 | 正常 | 接口可访问且响应兼容 |
| `HealthyEmptyModels` | 黄色 | 正常但无模型 | 请求成功但模型数组为空 |
| `AuthFailed` | 红色 | API Key 无效 | 401/403 或等价认证拒绝 |
| `ConfigurationError` | 红色 | 配置错误 | URL、协议或 Headers 不合法 |
| `EndpointError` | 红色 | 地址不兼容 | 404/405 或路径错误 |
| `Unavailable` | 红色 | 无法访问 | DNS、TLS、连接或超时失败 |
| `RateLimited` | 黄色 | 上游限流 | 429 |
| `RequestRejected` | 红色 | 请求被拒绝 | 其他 4xx |
| `UpstreamError` | 红色 | 上游异常 | 5xx |
| `ProtocolError` | 红色 | 响应格式不兼容 | JSON 或模型列表结构异常 |
| `Disabled` | 灰色 | 已停用 | 不参与健康统计 |

验证结果对象至少包含：`State`、`FailureKind`、`Summary`、`Detail`、`LastCheckedAt`、`StatusCode`、`LatencyMs`、`DiscoveredModelCount` 和 `IsChecking`。

## 6. 页面联动

### 6.1 顶部健康卡片

- 主数字使用“正常 Provider / 已启用 Provider”，停用 Provider 不计入分母。
- 摘要动态显示异常和待验证数量，例如“1 个异常 · 1 个待验证”。
- 验证进行中显示“验证中 N/M”。
- 卡片提供“全部验证”操作。
- 卡片颜色、图标和文字由聚合状态决定，不再固定使用成功色。

### 6.2 Provider 列表项

- Provider 名称后保留一个状态圆点：灰色待验证、蓝色验证中、绿色正常、黄色警告、红色异常。
- 圆点 Tooltip 和无障碍名称包含完整状态摘要。
- 列表项底部增加一行短描述，仅在待验证、验证中或异常时显示；成功时显示“正常 · HTTP 200 · 640 ms”这类短摘要。
- 异常摘要示例：“异常 · API Key 无效（HTTP 401）”“无法访问 · 请求超时”。
- 不在标题后堆叠长文本 badge，避免目录密度过高。

### 6.3 编辑器连接区

继续使用现有 `ConnectionStatus` 和“测试连接”按钮，但内容扩展为：

- 当前状态和 HTTP 结果；
- 延迟和最近检查时间；
- 发现的模型数量；
- 可操作的失败原因和建议。

编辑器展示单个 Provider 的详细结果，不覆盖顶部聚合状态的定义。

## 7. 组件边界与数据流

新增一个无 UI 的 Provider 验证服务，职责仅包括预检查、构造请求、发送请求和分类结果。它不负责保存配置、不直接操作 Avalonia 属性，也不写 Toast。

`ProvidersViewModel` 负责：

- 管理单个/批量验证的取消、并发和生命周期；
- 将结果写回 `ProviderEditorViewModel`；
- 重新计算健康摘要；
- 通过现有日志和 `ToastService` 提供安全反馈。

`ProviderEditorViewModel` 负责保存瞬时健康字段及可绑定的本地化摘要。配置属性变化时清除旧结果并通知界面。

## 8. 错误与日志

用户可见错误必须是可处理的中文摘要：

- 请先填写 Base URL；
- Base URL 必须是 HTTP 或 HTTPS 地址；
- API Key 无效或权限不足；
- 无法解析服务器地址；
- 安全连接失败，请检查证书或代理；
- 上游在 8 秒内没有响应；
- 模型列表地址不正确；
- 上游请求过于频繁；
- 上游服务暂时异常；
- 上游响应不是兼容的模型列表格式。

业务日志遵循现有 `ILogger<T>` 规范：记录验证开始、完成、降级和失败的安全摘要，不记录 API Key、Authorization、Headers 值、请求/响应正文、用户 prompt 或工具参数。

## 9. 测试与验收

### 9.1 自动化测试

- 状态聚合：正常、待验证、异常、停用和验证中的计数及文案。
- 配置变化会重置为 `Unknown`。
- HTTP 假客户端覆盖 2xx、空模型、401、403、404、405、429、5xx、超时、TLS/连接异常、非法 JSON 和不兼容响应结构。
- 单个测试只更新目标 Provider；批量测试不会覆盖未参与验证的 Provider。
- 失败时模型目录保持不变。
- 日志断言不包含 API Key、Authorization、Headers 值和响应正文。
- Avalonia 视图契约验证摘要卡、状态点、异常描述和“全部验证”绑定存在。

### 9.2 手动验收

- 三个 Provider 分别处于正常、待验证和异常时，卡片和列表状态一致。
- 修改 Base URL 或 API Key 后状态立即回到待验证。
- 单个测试、全部验证、取消和重复点击行为稳定。
- 401、超时、404、429、500 和空模型列表的文案可理解且不重叠。
- 920x600、1180x760 和窄窗口下状态描述不遮挡开关、删除按钮或模型信息。
- 重新打包桌面端，在发布包中完成同样的窗口级验证。

## 10. 交付顺序

1. 定义状态枚举、失败类型、验证结果和本地化资源。
2. 抽取并实现验证服务，补齐 HTTP 分类单测。
3. 接入 `ProvidersViewModel` 的单个/批量验证、取消和聚合统计。
4. 更新 Provider 列表、顶部摘要卡和编辑器连接区绑定。
5. 运行定向测试、全量测试、构建和发布包窗口级验收。

