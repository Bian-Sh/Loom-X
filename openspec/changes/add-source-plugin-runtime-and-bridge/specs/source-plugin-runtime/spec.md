# Spec Delta

## Purpose

定义 LoomX 如何发现、验证、编译、缓存、诊断和重新加载由 Manifest 描述的可信 C# 源码插件，使插件无需预编译项目或本机 .NET SDK 即可复用现有插件运行时契约。

## ADDED Requirements

### Requirement: Manifest 声明源码插件 Runtime
源码插件 Manifest SHALL 以版本化 Runtime 声明标识 `csharp-source`、插件入口类型和非空的显式源码相对路径列表。源码收集 MUST 仅以 Manifest 为准，不得依赖 `.csproj` 或隐式目录扫描。

#### Scenario: 有效源码插件声明
- **WHEN** Manifest 声明 `csharp-source`、有效入口类型和多个存在的 `.cs` 相对路径
- **THEN** Runtime 将声明的文件作为该插件本次编译的完整源码集合

#### Scenario: 缺少源码 Runtime 必备字段
- **WHEN** 源码插件缺少入口类型、sources 为空或包含重复路径
- **THEN** Runtime 在编译前拒绝该插件并返回对应 Manifest 字段的结构化诊断

#### Scenario: 不存在的隐式源码不参与编译
- **WHEN** 插件目录含有未列入 sources 的 `.cs` 文件
- **THEN** Runtime 不读取也不编译该文件

### Requirement: 源码路径限制在插件包内
Runtime MUST 将每个源码路径按插件包根目录规范化，并拒绝绝对路径、UNC 路径、设备路径、盘符相对路径、目录穿越以及经 reparse point 或符号链接逃逸插件根目录的路径。

#### Scenario: 合法子目录源码被读取
- **WHEN** sources 包含 `src/Models.cs` 且规范化结果仍位于插件包根目录内
- **THEN** Runtime 读取该文件并保留 Manifest 中的相对路径用于诊断和缓存

#### Scenario: 目录穿越被拒绝
- **WHEN** sources 包含 `../shared/Secret.cs` 或规范化后位于其他插件目录的路径
- **THEN** Runtime 拒绝加载该插件且不读取目标文件

#### Scenario: 链接导致的目录逃逸被拒绝
- **WHEN** 声明路径表面位于插件目录内但最终解析到插件目录之外
- **THEN** Runtime 拒绝该源码路径并返回安全诊断

### Requirement: 宿主内置确定性源码编译
Runtime SHALL 使用宿主随附的编译能力将声明源码编译为可加载程序集，用户环境 MUST NOT 需要安装 .NET SDK。编译引用 MUST 限于宿主允许的框架程序集、Plugin API 及明确允许的共享契约。

#### Scenario: 未安装 SDK 时编译成功
- **WHEN** 用户机器未安装 .NET SDK 且源码只使用允许的引用
- **THEN** Runtime 仍能完成编译并生成可加载程序集及调试符号

#### Scenario: 源码请求未允许的依赖
- **WHEN** 源码引用未由宿主提供的第三方程序集
- **THEN** Runtime 返回编译诊断且不执行 NuGet 或其他网络依赖恢复

#### Scenario: 预编译插件保持兼容
- **WHEN** Manifest 继续声明现有预编译程序集 Runtime
- **THEN** Runtime 按既有程序集加载流程处理，不要求该插件改为源码形式

### Requirement: 内容寻址编译缓存
Runtime SHALL 以 Manifest、源码相对路径及内容、Plugin API 版本、编译器版本和生效编译选项共同生成缓存身份，并在身份相同且缓存完整时复用编译产物。

#### Scenario: 未变化源码命中缓存
- **WHEN** 同一插件的缓存身份与既有完整产物一致
- **THEN** Runtime 直接复用对应程序集和调试符号而不重复编译

#### Scenario: 任一编译输入变化导致缓存失效
- **WHEN** 源码内容、路径、Manifest、Plugin API 版本、编译器版本或编译选项任一发生变化
- **THEN** Runtime 生成不同缓存身份并重新编译

#### Scenario: 不完整缓存不被加载
- **WHEN** 缓存目录缺失程序集、调试符号或完整性元数据
- **THEN** Runtime 将该缓存视为无效并重新编译，不加载部分产物

### Requirement: 编译失败不破坏可用版本
源码编译 SHALL 先写入隔离的候选缓存，只有完整编译成功后才能原子发布。失败候选 MUST NOT 覆盖最近一次可用缓存或当前已加载的插件实例。

#### Scenario: 首次编译失败
- **WHEN** 插件不存在可用缓存且源码编译失败
- **THEN** Runtime 拒绝加载该插件并保留其他插件正常运行

#### Scenario: 更新源码编译失败
- **WHEN** 已加载插件的新源码版本编译失败
- **THEN** Runtime 保持当前实例运行并保留最近一次可用缓存，同时报告本次更新失败

#### Scenario: 更新源码编译成功
- **WHEN** 新源码版本编译成功并通过加载前验证
- **THEN** Runtime 发布新缓存并在重新加载边界用新实例替换旧实例

### Requirement: 提供结构化安全编译诊断
编译与加载诊断 SHALL 至少包含插件 id、阶段、严重级别、错误码、安全消息，以及适用时的源码相对路径、行和列。诊断 MUST NOT 包含插件数据、凭据、请求正文或规范化后的外部绝对路径。

#### Scenario: 语法错误可定位
- **WHEN** 声明源码存在语法错误
- **THEN** Runtime 返回含相对文件路径、行、列和编译器错误码的诊断

#### Scenario: 非源码错误仍可分类
- **WHEN** 失败发生在 Manifest 验证、缓存校验或入口类型加载阶段
- **THEN** Runtime 返回对应阶段和稳定错误码，不伪造源码位置

### Requirement: 源码插件遵守既有插件契约
编译后的源码插件 MUST 通过与预编译插件相同的共享契约、Manifest 一致性检查、Extension 注册、Pipeline 顺序、失败策略、启停和 UI Contribution 验证流程，不得获得额外宿主实现访问权。

#### Scenario: 入口类型不实现插件契约
- **WHEN** 编译成功但入口类型未实现当前 Plugin API 要求的插件接口
- **THEN** Runtime 拒绝实例化该插件并返回契约不匹配诊断

#### Scenario: 源码插件注册 Extension
- **WHEN** 有效源码插件创建 Request 和 Response Extension
- **THEN** 两个 Extension 按现有 Manifest 声明和 Pipeline 配置进入对应 Pipeline

### Requirement: 源码插件被视为可信代码而非安全沙箱
产品和开发工具 MUST 明确披露源码插件会在 LoomX 本地进程权限边界内执行，`AssemblyLoadContext` 仅用于加载与卸载，不构成恶意代码隔离。

#### Scenario: 安装或启用源码插件
- **WHEN** 用户首次安装或启用一个源码插件
- **THEN** 产品向用户展示可信代码边界说明，不宣称该插件已被沙箱隔离
