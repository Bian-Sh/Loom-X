# plugin-ui-contributions Specification

## Purpose
定义插件如何以平台无关、受宿主管控的声明式结构向插件卡片和未来插件详情区域贡献界面内容，同时确保 Router 统一掌握主题、导航、安全限制与生命周期。

## Requirements

### Requirement: 插件声明 UI Contribution
插件 Manifest SHALL 允许声明零个或多个 UI Contribution，每个声明 MUST 包含插件内唯一 ID 和宿主支持的挂载位置。初版挂载位置 SHALL 包含插件卡片正文和插件详情正文；没有 UI 声明的插件 MUST 继续正常加载和运行。

#### Scenario: 插件只声明卡片正文
- **WHEN** 插件 Manifest 只声明卡片正文 Contribution
- **THEN** Router 在该插件卡片正文中渲染其声明内容
- **THEN** Router 不为该插件显示详情页齿轮入口

#### Scenario: 插件声明详情正文
- **WHEN** 插件 Manifest 声明详情正文 Contribution
- **THEN** Router 在插件卡片中显示通用齿轮入口
- **THEN** 点击入口后在插件页面原内容区域显示该插件的详情正文

#### Scenario: 插件没有 UI 声明
- **WHEN** 插件 Manifest 不包含 UI Contribution
- **THEN** Router 仍加载该插件的 Runtime Extension
- **THEN** 插件卡片保持通用信息展示且不出现插件专用空白区域

### Requirement: 插件通过平台无关契约提供声明式 UI
插件 SHALL 通过版本化的 UI Contribution Provider 返回声明式节点树，不得向宿主返回 Avalonia Control、XAML、宿主 ViewModel 或其他宿主私有 UI 类型。声明式协议 SHALL 支持组合布局、表面、文本、图标和分隔线，并使用语义化文字角色与颜色角色表达视觉意图。

#### Scenario: Router 渲染合法节点树
- **WHEN** 插件返回与 Manifest 声明一致且符合当前协议版本的节点树
- **THEN** Router 使用当前主题资源将节点树渲染到对应挂载位置
- **THEN** Router 无需理解节点所展示数据的业务含义

#### Scenario: 插件数据变化
- **WHEN** 插件发出不包含业务数据的 UI 失效通知
- **THEN** Router 在 UI 线程重新获取该插件的安全 Contribution 快照
- **THEN** 其他插件卡片不需要重新构建

#### Scenario: 当前语言变化
- **WHEN** 用户切换桌面端语言
- **THEN** Router 使用新的文化信息重新请求插件 Contribution
- **THEN** 插件负责返回该语言对应的自有文案

### Requirement: Router 隔离无效 UI Contribution
Router MUST 验证 Contribution ID、挂载位置、协议版本、节点类型、节点深度、节点数量、尺寸值和图标数据。单个 Contribution 无效时 MUST 隔离该 UI 并记录不含敏感内容的安全诊断，不得因此停止同一插件的 Pipeline Extension 或其他插件。

#### Scenario: Provider 与 Manifest 不一致
- **WHEN** Provider 返回未在 Manifest 声明的 Contribution ID 或错误挂载位置
- **THEN** Router 不渲染该 Contribution
- **THEN** 插件的请求或响应 Extension 仍可继续运行

#### Scenario: 节点树超过宿主限制
- **WHEN** 插件返回超过宿主深度、数量或尺寸限制的节点树
- **THEN** Router 拒绝该 Contribution 并显示安全降级状态
- **THEN** 诊断不得包含插件节点中的潜在敏感文本

#### Scenario: 未知协议版本
- **WHEN** 插件声明的 UI 协议版本高于 Router 支持版本
- **THEN** Router 不尝试猜测或部分执行未知语义
- **THEN** Router 隔离该 Contribution 并保留插件 Runtime 能力

### Requirement: Router 管理插件列表与详情区域
Router SHALL 拥有插件列表、齿轮入口、详情外壳和返回行为。插件只贡献详情正文，不得创建新的顶级窗口、侧边栏导航项或控制宿主导航状态。

#### Scenario: 打开插件详情
- **WHEN** 用户点击具有详情正文声明的插件卡片齿轮按钮
- **THEN** 插件页面切换为该插件的通用详情外壳
- **THEN** 左侧导航仍保持在插件页面
- **THEN** 详情外壳正文渲染插件的详情 Contribution

#### Scenario: 返回插件列表
- **WHEN** 用户在插件详情外壳点击返回
- **THEN** 插件页面恢复通用插件列表

#### Scenario: 详情插件失效
- **WHEN** 当前详情对应的插件在刷新后不存在或不再声明详情正文
- **THEN** Router 自动退出详情状态并返回插件列表
