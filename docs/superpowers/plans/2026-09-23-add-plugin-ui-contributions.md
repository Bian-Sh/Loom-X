---
change: add-plugin-ui-contributions
design-doc: openspec/changes/add-plugin-ui-contributions/design.md
base-ref: 4e7fba1567d1f6833703b0ecd17d621088aee2a7
---

# 插件 UI Contribution 与凭据保护观测实施计划

<!-- comet-task-authority: openspec/changes/add-plugin-ui-contributions/tasks.md -->

## 目标与约束

实现平台无关、版本化、受 Router 验证和主题化渲染的插件 UI Contribution 契约；Credential Protection 只通过该契约声明运行期观测卡片。保持现有 Pipeline、ALC 类型身份、fail-closed、Vault 持久化和无 UI 插件兼容行为。所有生产代码先有能够因缺失行为而失败的测试；插件 UI、日志和诊断不得包含敏感正文、placeholder 或认证值。

## 工作包一：抽象契约与 Manifest

<!-- comet-task-ref:b3d99aba-a114-4124-b0fe-f32333cd5c20 -->
**1.1 声明式 UI 契约**

- 范围：新增 `LoomX.Plugin.Abstractions/PluginUiContributions.cs`，定义 Provider、Context、Contribution、Slot、节点、语义文字与颜色角色以及无负载失效事件。
- 依赖：仅 BCL，不引用 Avalonia 或宿主工程。
- RED：新增 `LoomX.Tests/Plugins/PluginUiContractTests.cs`，编译并断言契约可表达 CardBody、DetailBody、组合节点和事件。
- GREEN 验收：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~PluginUiContractTests`。

<!-- comet-task-ref:2d487102-a1e9-42fd-a2b0-0a9c1e0d9354 -->
**1.2 Manifest UI 声明**

- 范围：修改 `PluginManifest.cs`、`ManifestParser.cs` 和 `PluginCatalogTests.cs`，增加可选 `ui.contributions`、唯一 ID 和 Slot 解析。
- 兼容性：缺少 `ui` 的现有 Manifest 解析结果保持合法；UI 错误必须提供不含节点内容的诊断。
- RED/GREEN：先加入合法、重复 ID、未知 Slot、无 UI 四组测试，再运行 `dotnet test ... --filter FullyQualifiedName~PluginCatalogTests`。

## 工作包二：Runtime 代理、验证与隔离

<!-- comet-task-ref:37765949-74f3-4d64-bc26-49e077ae9cd4 -->
**2.1 Provider 发现与 Contribution 查询**

- 范围：修改 `PluginRuntime.cs`，识别可选 Provider，通过 Runtime 方法按插件和 Slot 返回已验证快照；桌面端不得直接持有动态插件实例。
- 测试：扩展 `PluginRuntimeTests.cs` 和测试 Fake Plugin，验证声明/Provider 匹配、未声明内容被拒绝、UI 失败不影响 Pipeline。

<!-- comet-task-ref:25110692-fa75-42a8-a2f3-dcbf4aeded3d -->
**2.2 节点安全验证器**

- 范围：新增 `LoomX.PluginHost/PluginUiValidator.cs`，集中检查 schema version、深度、节点数、文本长度、尺寸范围、列跨度和 Geometry 长度。
- 安全：错误只记录 Plugin ID、Contribution ID 与原因代码，不序列化或回显节点文本。
- 验收：超限与未知版本测试 RED 后实现，运行 PluginRuntime/Validator 测试。

<!-- comet-task-ref:bf3e3e92-54ff-4e89-afd9-04bb0e4dee6b -->
**2.3 失效事件代理**

- 范围：Runtime 订阅 Provider 的无负载事件并转发带 Plugin ID 的宿主事件，保留未来卸载时解除订阅的位置。
- 验收：测试只触发对应插件通知，事件参数不包含 Contribution 或业务值。

完成工作包二后执行第一次独立分段审查，重点检查公共契约、ALC 边界、验证绕过和敏感诊断。

## 工作包三：Router 通用 Presenter 与详情外壳

<!-- comet-task-ref:a69cda12-4cfc-447a-bb64-545d71640531 -->
**3.1 通用 Avalonia Presenter**

- 范围：新增 `LoomX/Controls/PluginUiPresenter.cs`，递归映射 Stack、Grid、Surface、Text、Icon、Divider，并把语义角色映射到现有 DynamicResource。
- 测试：新增 `LoomX.Tests/Views/PluginUiPresenterTests.cs`，使用 `AvaloniaTestBootstrap` 且不离开初始化 UI 线程；先验证缺失 Renderer，再实现节点映射和安全降级。

<!-- comet-task-ref:71dcf0cb-3a19-43d7-9234-8b0fa745697b -->
**3.2 CardBody 集成**

- 范围：修改 `PluginsViewModel.cs` 与 `PluginsView.axaml`，每个通用插件卡渲染零个或多个 CardBody Contribution。
- 约束：Router 源码和资源不得出现 Credential Protection 指标名称或数值格式逻辑。
- 验收：扩展 `PluginsViewContractTests.cs` 并运行 Presenter/UI 契约测试。

<!-- comet-task-ref:59b639c8-6564-44f4-aea6-7a57764800c0 -->
**3.3 DetailBody 与齿轮导航**

- 范围：ViewModel 增加列表/详情状态、打开/返回命令和当前插件详情快照；XAML 增加宿主管理的标题、返回按钮、插件摘要和 DetailBody 区域。
- 行为：只有 Manifest 声明 DetailBody 时显示齿轮；插件刷新后详情无效则自动回列表。
- 验收：使用测试插件或 ViewModel 测试覆盖齿轮、打开、返回和失效回退。

<!-- comet-task-ref:59f7ed18-672b-4e8a-85d8-461d0c5e7a68 -->
**3.4 事件与语言刷新**

- 范围：把 Runtime 失效事件投递到 Dispatcher UI 线程，按 Plugin ID 合并未处理刷新；语言变化时使用新 Culture 重新获取插件快照。
- 验收：测试后台事件不会直接修改 UI 集合，同一插件突发事件被合并，文化信息正确传给 Provider。

完成工作包三后执行第二次独立分段审查，重点检查 UI 线程、事件泄漏、主题资源和 Router/插件职责边界。

## 工作包四：Credential Protection 观测与自有 UI

<!-- comet-task-ref:d70aef7a-c4d3-4ecf-9bb1-b5ddcc01f944 -->
**4.1 线程安全观测状态**

- 范围：新增 `plugins/LoomX.CredentialProtection/CredentialProtectionObservability.cs`，仅保存五个 long 计数并发布无负载失效事件。
- RED/GREEN：新增串行和并发计数测试，验证快照初始为零、原子累计且不接受 payload 参数。

<!-- comet-task-ref:098def55-30b6-4861-8d39-bc399e6cb60a -->
**4.2 实际替换词项计数**

- 范围：修改 `CredentialEngine.cs`，增加兼容重载返回替换数量；JSON 敏感字段每个实际值计一次，文本正则按实际替换位置计数。
- 兼容性：现有 `Sanitize(payload, out changed)` 委托新实现，输出保持不变。
- 验收：先加入 JSON、自由文本、重复值测试并确认 RED，再运行全部 CredentialProtectionTests。

<!-- comet-task-ref:cfe8e6af-2ef5-43e6-b5f1-f0db47ba7c27 -->
**4.3 Request/Response Extension 接线**

- 范围：插件创建共享观测对象并传给两个 Extension；请求入口计 total，实际替换计 sanitized/terms，恢复成功按响应计一次，catch 计 error。
- 验收：完整性指令单独变化不计脱敏；单响应多 token 只计一次；ThrowingEngine 仍 Blocked 且 error 增加。

<!-- comet-task-ref:84dbca2e-7719-4eea-ba50-f28d62fd94e4 -->
**4.4 插件自有 CardBody Contribution**

- 范围：Credential Protection 实现 Provider，更新 `plugin.manifest.json`，由插件返回三栏 Surface/Grid 节点、图标、语义色和多语言最终文案。
- 格式：主值由插件生成 `sanitized/total`；辅助文字展示累计词项；恢复和异常各自独立。
- 安全验收：序列化或递归检查节点只出现安全文案、图标和数字，不出现测试 secret、placeholder、Authorization 或 payload。

## 工作包五：集成、发布与验证

<!-- comet-task-ref:dc0e514d-c222-4b72-bfff-cf9c7145c884 -->
**5.1 自动化回归**

- 更新发布契约与插件页面契约测试；运行 Plugin、View 相关过滤测试、`dotnet test LoomX.Tests/LoomX.Tests.csproj` 和 `dotnet build LoomX.slnx`。

<!-- comet-task-ref:c9b63ee1-0625-443b-8d8e-3fd91d87471e -->
**5.2 CUA UI 验收**

- 发布后使用隐藏方式启动 WPF/Avalonia 桌面包，通过 CUA 仅截取应用窗口；验证三张卡片、动态值、主题可见性、无 DetailBody 时无齿轮，以及测试详情入口的通用外壳。

<!-- comet-task-ref:ae00fed8-43f4-48b2-8a57-279625bf3986 -->
**5.3 可运行发布包**

- 使用项目发布脚本或 `dotnet publish` 输出到 `outputs/2026-09-23-plugin-ui-contributions-<HHmm>`，确认插件 DLL、Manifest、依赖和本地化资源存在。

<!-- comet-task-ref:ca9bcca6-d53f-4d8d-ba59-7d56f9e5da5a -->
**5.4 收尾交付**

- 完成敏感信息静态扫描、最终独立集成审查、Comet build/verify 证据、中文提交和 push；不清理其他 Session 或非本任务产物。

## 回退策略

Manifest 的 `ui` 可选，删除 Contribution Provider、Runtime 查询与 Presenter 即可回退；观测数据不落盘，不需要迁移。任何回退都不得删除或重建 Credential Vault。
