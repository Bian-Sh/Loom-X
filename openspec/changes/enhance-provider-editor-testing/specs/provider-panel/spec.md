## ADDED Requirements

### Requirement: Provider 基础配置使用稳定内部 ID 与统一兼容类型
Provider 面板 MUST 隐藏 Provider ID 输入，新建 Provider 时 MUST 自动生成唯一且稳定的内部业务 ID；已有 Provider 的业务 ID MUST 保持不变。基础 Tab MUST 将协议类型和 OpenAI 请求格式合并为 OpenAI Chat Completions、OpenAI Responses、Anthropic Messages 三个兼容类型，并将 API Key 与名称、Base URL 一同提供。

#### Scenario: 新建 Provider 自动获得内部 ID
- **WHEN** 用户新建 Provider 并修改名称或兼容类型
- **THEN** 系统自动生成不可见的唯一业务 ID，且该 ID 不随名称或兼容类型变化

#### Scenario: 已有 Provider 保持业务 ID
- **WHEN** 用户打开并保存升级前已存在的 Provider
- **THEN** 系统保留原业务 ID，不执行迁移、重命名或创建第二份 Provider

#### Scenario: 选择统一兼容类型
- **WHEN** 用户选择 OpenAI Chat Completions、OpenAI Responses 或 Anthropic Messages
- **THEN** 系统保存与该选项对应的协议和请求格式，并在重新打开页面后回显同一选项

### Requirement: Provider 详情区按基础、高级、模型和测试组织
Provider 详情区域 MUST 提供基础、高级、模型和测试四个 Tab。高级 Tab MUST 仅包含代理、自定义请求头和 CLI/UA 身份模拟，不得包含旧的连接测试区块；模型 Tab 的现有模型同步、搜索、启停、排序和删除行为 MUST 保持不变。

#### Scenario: 打开高级 Tab
- **WHEN** 用户打开 Provider 的高级 Tab
- **THEN** 页面只显示代理、自定义请求头和 CLI/UA 身份模拟配置，不显示旧的连接测试卡片

#### Scenario: 页面健康统计保持可用
- **WHEN** 用户移除高级 Tab 内的旧连接测试入口后查看 Provider 页面顶部
- **THEN** 健康统计、Provider 健康状态和“验证全部”入口仍然可用

#### Scenario: 模型 Tab 行为保持不变
- **WHEN** 用户打开模型 Tab
- **THEN** 原有模型同步、搜索、启停、拖放排序、元数据展示和删除能力继续工作
