# Spec Delta

## Purpose

定义 LoomX 面向用户和 AI 的插件开发知识包与工具闭环，使开发者无需掌握宿主内部实现或手工搭建编译环境，也能创建、诊断、测试和重新加载插件。

## ADDED Requirements

### Requirement: 提供版本化插件开发 Skill
LoomX SHALL 提供可由 AI 和用户读取的插件开发 Skill，并标明适用的 Plugin API 与 Manifest 版本。Skill MUST 只描述稳定公共契约和工具，不得要求插件依赖宿主内部实现。

#### Scenario: AI 开始开发插件
- **WHEN** AI 读取与当前 LoomX 版本匹配的插件开发 Skill
- **THEN** Skill 提供完成插件所需的公共入口、约束、目录布局和验证步骤

#### Scenario: Skill 与宿主版本不兼容
- **WHEN** Skill 声明的 Plugin API 主版本与宿主不兼容
- **THEN** 开发工具在编译或安装前报告版本不兼容，不继续猜测契约

### Requirement: Skill 覆盖完整稳定契约
Skill SHALL 说明 Manifest Schema、源码插件目录与模板、Pipeline API、声明式 UI API、Bridge 能力与版本、权限边界、日志与通知安全规则，并包含至少一个可运行示例。

#### Scenario: 创建使用 SQLite 的源码插件
- **WHEN** 用户要求 AI 创建需要持久化数据的源码插件
- **THEN** Skill 引导其在 Manifest 声明 `storage.sqlite/v1`、自定数据库相对路径并使用参数化 SQL Bridge

#### Scenario: 创建带 UI 的插件
- **WHEN** 用户要求插件在卡片或详情区域展示内容
- **THEN** Skill 引导其使用声明式 UI Contribution，不生成 Avalonia Control、XAML 或宿主 ViewModel 依赖

#### Scenario: 需要日志和通知
- **WHEN** 插件需要运行诊断和用户即时反馈
- **THEN** Skill 引导其分别使用 `logging/v1` 与 `notifications/v1` 并遵守敏感信息限制

### Requirement: 提供源码插件模板
开发工具 SHALL 能创建包含合法 Manifest、显式 sources、入口类型、基础源码、测试占位与本地化负载的最小源码插件模板。生成结果不得要求 `.csproj` 才能由 LoomX 编译。

#### Scenario: 创建最小插件
- **WHEN** 用户提供合法插件 id 并执行创建操作
- **THEN** 工具生成可通过 Manifest 校验和源码编译的最小目录结构

#### Scenario: 创建参数非法
- **WHEN** 插件 id、目标目录或入口类型不符合规范
- **THEN** 工具拒绝写入并返回可定位到输入字段的结构化诊断

### Requirement: 提供插件验证操作
开发工具 SHALL 提供 validate 操作，用于在执行插件代码前检查 Manifest、源码路径、能力版本、入口声明、负载布局和禁止依赖，并以机器可读格式返回全部可安全报告的问题。

#### Scenario: 验证有效插件
- **WHEN** 插件目录满足当前契约
- **THEN** validate 返回成功以及实际使用的 Plugin API 和能力版本

#### Scenario: 一次报告多个独立问题
- **WHEN** Manifest 同时存在缺失 source 和不支持能力版本的问题
- **THEN** validate 在安全可行范围内返回两个结构化诊断，而非只返回第一个问题

### Requirement: 提供插件编译与诊断操作
开发工具 SHALL 提供 compile 和 diagnostics 操作，复用宿主实际源码编译与诊断规则。操作 MUST 在没有本机 .NET SDK 的环境中可用，并输出供用户或 AI 自动修复的结构化结果。

#### Scenario: 编译成功
- **WHEN** 源码插件通过验证并编译成功
- **THEN** compile 返回成功、缓存身份和产物状态，不要求用户手工定位编译器

#### Scenario: 编译失败后自动修复
- **WHEN** compile 返回包含文件、行、列、错误码和安全消息的诊断
- **THEN** AI 可修改对应声明源码并再次调用 compile 验证修复

### Requirement: 提供隔离的插件测试操作
开发工具 SHALL 提供 test 操作，在受控测试宿主中验证入口创建、Extension 注册、Bridge 契约、声明式 UI 与插件自带测试，不得默认向真实 Provider 发送请求或修改宿主核心配置。

#### Scenario: 测试 Pipeline Extension
- **WHEN** 插件包含 Request 或 Response Extension 测试
- **THEN** test 在测试 Pipeline 和隔离插件数据目录中执行并返回结构化结果

#### Scenario: 测试需要 SQLite
- **WHEN** 插件测试调用 `storage.sqlite/v1`
- **THEN** test 提供与生产契约一致但数据目录隔离的 SQLite Bridge

### Requirement: 提供安全重新加载操作
开发工具 SHALL 提供 reload 操作，用于对已安装插件执行验证、编译和原子替换。重新加载失败 MUST 保持当前可用实例运行，并返回失败诊断。

#### Scenario: 重新加载成功
- **WHEN** 修改后的插件验证与编译均成功
- **THEN** reload 在运行时边界替换插件实例并报告新版本已生效

#### Scenario: 重新加载失败
- **WHEN** 修改后的插件编译或初始化失败
- **THEN** reload 不替换当前实例且返回结构化诊断

### Requirement: 支持 AI 闭环开发流程
插件开发接口 SHALL 允许 AI 按“读取 Manifest 与 sources、修改源码、验证、编译、读取诊断、修复、测试、安装或重新加载”的顺序反复执行，所有操作结果 MUST 可机器读取且具有稳定错误码。

#### Scenario: 从自然语言需求迭代到可加载插件
- **WHEN** AI 根据用户需求生成插件并在首次编译遇到错误
- **THEN** AI 可仅依据 Skill、插件文件和结构化工具结果完成修复与复验，无需读取宿主内部源码

#### Scenario: 工具输出包含敏感运行数据
- **WHEN** 编译、测试或重新加载过程中遇到包含凭据或请求正文的异常
- **THEN** 工具结果仅返回安全摘要和稳定错误码，不回显敏感原值
