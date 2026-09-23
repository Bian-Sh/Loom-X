## Why

当前插件页面只能由 Router 预先写死卡片结构，无法让未知插件声明各自不同的观测信息，也没有为后续插件详情与设置区域建立稳定扩展边界。需要引入受宿主管控的声明式 UI Contribution 契约，使插件能够表达内容和布局，而 Router 继续统一负责加载、主题化渲染、导航、安全限制与生命周期。

## What Changes

- 为插件抽象层新增版本化的声明式 UI Contribution 契约，采用 `IPluginUiContributionProvider`、`PluginUiContribution`、`PluginUiSlot` 与受限节点树表达插件 UI。
- 扩展插件 Manifest，允许插件声明挂载到 `card-body` 或预留的 `detail-body` 的 UI Contribution；声明与运行时 Provider 不一致时只隔离对应 UI，不影响插件 Pipeline。
- 在 Plugin Runtime 中加载、校验并暴露插件 UI Contribution，禁止插件直接向宿主返回 Avalonia Control、XAML 或宿主 ViewModel。
- 在桌面插件页面加入通用 `PluginUiPresenter`，按主题资源绘制插件声明的布局；Router 不理解任何插件专用指标。
- 为声明了 `detail-body` 的插件卡片预留齿轮入口和插件列表/详情区域切换骨架；本次不定义交互式设置表单和保存协议。
- Credential Protection 作为首个使用者，自行声明三栏观测区域，展示已脱敏请求数/总请求数、累计脱敏词项、已还原回复数和异常数。
- Credential Protection 的观测数据仅在当前进程内累计，使用线程安全计数，只向 UI 暴露安全摘要，禁止包含敏感原文、请求/响应正文、凭据 token 或 Header 值。

## Capabilities

### New Capabilities

- `plugin-ui-contributions`：插件通过 Manifest 和平台无关 Provider 向卡片正文或未来详情正文贡献声明式 UI，由 Router 统一验证、渲染和管理生命周期。
- `credential-protection-observability`：Credential Protection 统计并通过自身 UI Contribution 展示当前进程内的安全运行指标。

### Modified Capabilities

- 无。

## Impact

- 影响 `LoomX.Plugin.Abstractions` 的公共插件契约与 Manifest 数据结构。
- 影响 `LoomX.PluginHost` 的 Manifest 解析、插件加载、诊断和 UI Contribution 暴露接口。
- 影响 `LoomX` 桌面端插件页面、通用声明式节点渲染、详情区域导航状态与本地化。
- 影响 `plugins/LoomX.CredentialProtection` 的请求/响应统计、脱敏词项计数和插件自有 UI 声明。
- 需要新增插件契约、Runtime、UI 渲染、线程安全、敏感信息隔离和发布包回归测试；不引入新的第三方运行时依赖。
