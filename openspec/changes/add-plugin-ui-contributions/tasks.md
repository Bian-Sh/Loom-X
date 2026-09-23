## 1. 插件 UI 契约与 Manifest

- [ ] 1.1 先为声明式节点、Slot、Provider 和失效事件补充抽象层单元测试，再实现版本化 `IPluginUiContributionProvider` 与节点模型，并验证 `dotnet test` 对应契约测试通过 <!-- comet-task:b3d99aba-a114-4124-b0fe-f32333cd5c20 -->
- [ ] 1.2 先补充 Manifest 合法、重复 ID、未知 Slot 与可选 UI 的解析测试，再扩展 Manifest 模型和解析器，并验证现有无 UI 插件仍可加载 <!-- comet-task:2d487102-a1e9-42fd-a2b0-0a9c1e0d9354 -->

## 2. Runtime 加载、校验与隔离

- [ ] 2.1 先为 Provider 发现、Manifest/Provider 一致性和无效 UI 不影响 Pipeline 编写 Runtime 测试，再实现 Contribution 查询与安全诊断，并验证插件 Runtime 测试通过 <!-- comet-task:37765949-74f3-4d64-bc26-49e077ae9cd4 -->
- [ ] 2.2 实现节点树协议版本、深度、数量、文本、尺寸和图标限制，补充拒绝超限节点且诊断不回显节点文本的测试 <!-- comet-task:25110692-fa75-42a8-a2f3-dcbf4aeded3d -->
- [ ] 2.3 实现 Runtime 对 Provider 失效事件的代理与订阅生命周期，补充只通知对应插件且事件不携带业务数据的测试 <!-- comet-task:bf3e3e92-54ff-4e89-afd9-04bb0e4dee6b -->

## 3. Router 通用渲染与详情外壳

- [ ] 3.1 先为各声明式节点到 Avalonia 控件的映射编写 UI 线程测试，再实现 `PluginUiPresenter` 的主题化递归渲染并验证未知或无效内容安全降级 <!-- comet-task:a69cda12-4cfc-447a-bb64-545d71640531 -->
- [ ] 3.2 重构插件卡片以通用方式渲染 CardBody，删除凭据插件专用观测字段的可能性，并用契约测试验证 Router 不包含 Credential Protection 指标名称 <!-- comet-task:71dcf0cb-3a19-43d7-9234-8b0fa745697b -->
- [ ] 3.3 实现基于 DetailBody 声明的齿轮可见性、插件列表/详情状态、通用详情外壳与返回行为，并用测试插件验证详情失效后自动返回列表 <!-- comet-task:59b639c8-6564-44f4-aea6-7a57764800c0 -->
- [ ] 3.4 实现插件 UI 失效通知的 UI 线程投递和按插件合并刷新，并验证语言切换会带新 Culture 重新请求 Contribution <!-- comet-task:59f7ed18-672b-4e8a-85d8-461d0c5e7a68 -->

## 4. Credential Protection 观测贡献

- [ ] 4.1 先为总请求、已脱敏请求、实际替换词项、已还原回复和异常计数编写失败测试，再实现线程安全的进程内观测状态并验证并发累计正确 <!-- comet-task:d70aef7a-c4d3-4ecf-9bb1-b5ddcc01f944 -->
- [ ] 4.2 扩展 CredentialEngine 返回实际替换数量，同时保持现有 `Sanitize(..., out changed)` 兼容，并用现有及新增脱敏测试验证 JSON、自由文本和重复值计数 <!-- comet-task:098def55-30b6-4861-8d39-bc399e6cb60a -->
- [ ] 4.3 将观测状态接入 Request/Response Extension，验证完整性指令注入不计为脱敏、单响应多 placeholder 只计一次还原、异常仍保持 fail-closed <!-- comet-task:cfe8e6af-2ef5-43e6-b5f1-f0db47ba7c27 -->
- [ ] 4.4 由 Credential Protection 实现 CardBody Contribution、插件自有多语言文案、图标和三栏布局，验证主值按 `m/total` 格式显示且 Contribution 不含敏感原文或 placeholder <!-- comet-task:84dbca2e-7719-4eea-ba50-f28d62fd94e4 -->

## 5. 集成、界面与交付验证

- [ ] 5.1 更新插件页面与插件发布契约测试，运行相关测试项目和完整解决方案构建，确认现有插件卡、Pipeline 与发布复制行为无回归 <!-- comet-task:dc0e514d-c222-4b72-bfff-cf9c7145c884 -->
- [ ] 5.2 启动重新发布的桌面包并使用 CUA 验证浅色/当前主题下三张观测卡、刷新行为、无 DetailBody 时不显示齿轮以及通用详情测试入口布局 <!-- comet-task:c9b63ee1-0625-443b-8d8e-3fd91d87471e -->
- [ ] 5.3 将可运行发布包输出到 `outputs/` 下带可读时间的目录，验证其中包含 Credential Protection Manifest、程序集和本地化资源 <!-- comet-task:ae00fed8-43f4-48b2-8a57-279625bf3986 -->
- [ ] 5.4 完成最终敏感信息审查、代码审查、OpenSpec/Comet 验证记录，并用中文提交消息提交和推送当前分支 <!-- comet-task:ca9bcca6-d53f-4d8d-ba59-7d56f9e5da5a -->
