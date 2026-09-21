# Comet Design Handoff

- Change: enhance-update-experience
- Phase: design
- Mode: compact
- Context hash: 26c2f0184dcdb702b22a5e5eccc150774e2125298a92fa5de785d37358f59356

Generated-by: comet-handoff.sh
Task hash policy: task-content-v1. Read tasks.md for live completion; excerpts are design-time context.

OpenSpec remains the canonical capability spec. This handoff is a deterministic, source-traceable context pack, not an agent-authored summary.

## openspec/changes/enhance-update-experience/proposal.md

- Source: openspec/changes/enhance-update-experience/proposal.md
- Lines: 1-29
- SHA256: 5169ed070c60b1752c5e6670907e35baa3f571142d46bae48226f601b249c7b4

```md
## Why

Loom-X 当前的更新提示位于主窗口右下角，发现版本后仍需要用户手动发起下载，Release Notes 也被降级为纯文本且只能查看最新版本，流程割裂且难以持续感知下载与安装状态。现在需要参考 OpenCode 的低打扰更新入口，同时保持 Loom-X 自身的扁平化设计语言，将自动下载、安装确认和版本说明浏览整合为统一体验。

## What Changes

- 将更新提示从右下角卡片调整为窗口标题栏中的紧凑状态入口，在下载、校验、就绪和失败状态下提供可悬停展开的文字反馈。
- 自动检查发现正式新版本后自动下载并校验安装包，但必须等待用户在更新浮窗中确认后才启动安装器。
- 新增扁平化更新浮窗，直接渲染当前版本的 Markdown Release Note；下载期间展示实时进度，准备完成后提供“稍后”和“重启并安装”。
- 扩展设置页“更新”Tab，在保留当前版本、自动检查、更新代理和手动检查功能的基础上，增加最近正式版本列表、分页加载、版本标记以及 Markdown Release Notes 阅读区。
- 为 Release 历史提供加载、空、失败、缓存内容保留和加载更多状态，并过滤 Draft 与 Pre-release。
- 复用现有代理、GitHub Releases、SHA-256 校验、日志和本地化约束，不新增更新通道或后台静默安装。

## Capabilities

### New Capabilities

- `desktop-update-experience`: 定义桌面端自动更新状态机、标题栏更新入口、确认浮窗、下载与安装边界，以及设置页多版本 Release Notes 浏览行为。

### Modified Capabilities

无。

## Impact

- 主要涉及 `UpdateService`、`UpdateCoordinator`、`MainWindow`、`SettingsViewModel`、设置页 AXAML、Markdown 展示组件和本地化资源。
- 现有下载后立即启动安装器的服务边界将拆分为“下载并校验”和“用户确认后启动安装器”。
- GitHub Releases 拉取将同时支持最新可升级版本检查与最近正式版本的分页浏览。
- 将新增或调整更新服务、状态机、视图契约、本地化和 Markdown 展示测试；不改变数据库路径、发布资产命名和 SHA-256 校验契约。

```

## openspec/changes/enhance-update-experience/design.md

- Source: openspec/changes/enhance-update-experience/design.md
- Lines: 1-90
- SHA256: 0b51062bd6640614e13cc974c58db4cb916bb575ae25e99ce701763c709eb3f5

[TRUNCATED]

```md
## Context

参见 `proposal.md` 的动机与范围。当前更新实现由 `UpdateService` 拉取 GitHub Releases、下载并校验安装包，`UpdateCoordinator` 同时向主窗口与设置页暴露状态。现有 `DownloadAndInstallAsync` 在下载和校验完成后立即启动安装器，主窗口使用右下角卡片和纯文本 Release Note 弹层，设置页仅包含更新偏好与手动检查。

项目已经使用 `LiveMarkdown.Avalonia` 渲染助手 Markdown，并通过动态主题资源、本地化资源、`ILogger<T>` 和统一代理设置约束桌面 UI 与网络访问。本设计必须复用这些能力，不引入 WebView、第二套 Markdown 引擎或新的设置数据库路径。

## Goals / Non-Goals

**Goals:**

- 让检查、自动下载、校验、等待确认和安装形成单一可观察状态机。
- 将更新状态入口移动到窗口标题栏，并以低占用、可悬停展开的方式持续反馈状态。
- 让更新浮窗和设置页复用一致的 Release 模型与 Markdown 展示能力。
- 让设置页按页浏览正式 Release，同时不破坏现有偏好、代理与手动检查行为。
- 使网络失败、资产不兼容、校验失败和 Markdown 不安全内容都有明确且可测试的降级。

**Non-Goals:**

- 不实现断点续传、差分更新、静默安装、Beta 通道或跨设备同步。
- 不新增数据库表，也不将 Release Note 正文持久化到配置数据库。
- 不改变发布资产命名、安装器参数或 SHA-256 校验要求。
- 不照搬参考应用的颜色、阴影和卡片层级。

## Decisions

### 1. 保留一个全局更新协调器，但把 Release 历史浏览拆成独立状态模型

`UpdateCoordinator` 继续作为主窗口与设置页共享的安装状态事实源，只管理当前可安装目标及其生命周期。设置页新增独立的 Release History 状态模型，负责分页、选中项、缓存和空态/错误态。两者共享 `UpdateService` 返回的不可变 Release 模型，但互不复用 UI 状态。

选择该方案是为了避免把分页历史、列表选择和安装状态全部塞入 `UpdateCoordinator`。备选方案是在 `SettingsViewModel` 内直接维护所有集合和请求，但会让设置持久化与远程内容浏览耦合；完整独立 Updater 子系统则超出当前范围。

### 2. 将下载校验和启动安装器拆成两个显式阶段

更新服务提供“准备更新包”和“启动已准备安装包”两个边界。准备阶段下载安装器与校验文件、完成 SHA-256 验证后返回包含版本和本地安装器路径的结果；确认阶段只接受该已验证结果并启动安装器。协调器新增 `Ready` 状态，并保存当前会话的已准备结果。

这样可以保证自动下载不会导致应用意外退出，同时让“重启并安装”成为唯一安装入口。若用户关闭应用后重新启动，系统重新检查 Release；准备阶段可以检查同版本缓存文件并重新验证后复用，但不引入额外数据库状态。

### 3. 自动检查发现可安装版本后自动准备，手动检查复用同一任务

检查与准备分别由单实例异步门控制。自动检查发现可安装 Release 后立即进入准备流程；手动检查若已有检查或准备任务则等待或复用现有任务，而不是启动第二份请求。Release History 请求独立执行，不阻塞安装状态，但使用同一代理快照与 HTTP 安全策略。

只有同时满足“高于当前稳定版本”和“存在兼容安装器及校验资产”的 Release 才进入自动安装流程。缺少安装资产的正式 Release 仍可出现在历史列表中。

### 4. 标题栏入口是更新状态控件，不复用瞬时 ToastService

主窗口标题栏在最小化按钮左侧增加专用更新按钮。按钮仅在 `Downloading`、`Verifying`、`Ready` 或可重试错误状态显示；默认呈紧凑图标，指针悬停时通过宽度与透明度过渡展示本地化状态文字。它不使用会自动消失的全局 Toast，因为更新状态需要持续可访问。

点击按钮切换更新浮窗可见性。关闭浮窗只改变显示状态，不取消准备任务。现有右下角更新卡片和独立纯文本 Release Note 弹层将移除，普通操作 Toast 保持不变。

### 5. 更新浮窗采用单层布局并绑定全局更新状态

浮窗由遮罩、单个 Surface 容器、固定头部、可滚动 Markdown 正文和固定底部状态区组成。下载状态显示确定进度、字节数和速度；校验状态显示非确定进度；Ready 状态显示“稍后”和“重启并安装”；失败状态显示安全摘要和“重试”。

浮窗中的 Release Note 直接绑定当前目标 Release，不提供跳转设置页的中间流程。宽度和最大高度受窗口可用空间约束，颜色、边框和前景全部来自动态主题资源，以兼容透明、浅色和深色主题。

### 6. 设置页采用上方设置区加下方 Release Notes 分栏

更新 Tab 顶部保留当前版本、自动检查、更新代理与手动检查。下方新增 Release Notes 区域：左侧为固定最小宽度的版本列表和加载更多入口，右侧为选中 Release 的标题、日期、标记与 Markdown 阅读区。首次进入页面时惰性加载最近 10 个正式版本，当前会话内缓存结果。

刷新使用替换策略，但请求失败时保留旧集合；加载更多使用追加与版本号去重。默认选中最新 Release，若刷新后当前选中版本仍存在则维持选择。列表分别计算“最新”和“当前”标记，不假设当前版本一定存在于最近一页。

### 7. 提取只负责展示的 Release Notes 组件

新增可复用的 Release Notes 展示组件，输入为 Markdown 内容和可选加载状态，不持有网络或安装逻辑。组件复用 `LiveMarkdown.Avalonia` 和项目现有 Markdown 字体配置，供更新浮窗与设置页共同使用。每次切换 Release 时替换其 Markdown 构建器内容，而不是在 AXAML 中退化为普通 `TextBlock`。

原始 HTML、脚本和任意嵌入资源在进入渲染层前被限制；链接点击只允许交由系统浏览器打开 HTTPS 目标。网络日志只记录版本、页码、状态码、字节数和耗时，不记录正文。

### 8. 所有用户可见状态均进入本地化资源

更新阶段标签、按钮、空态、错误态、版本标记和辅助文本均通过现有 `.resx` 与 `IStringLocalizer<T>` 提供。协调器不得继续为用户可见状态写死中文；结构化日志可保持中文模板。

## Risks / Trade-offs

- [自动下载可能消耗用户网络流量] → 仅在现有“自动检查更新”开启时自动准备，并沿用更新代理设置；文案明确说明该行为。
- [GitHub API 未认证请求存在速率限制] → 首次进入时惰性加载、会话内缓存、按 10 条分页，并避免版本切换触发请求。
- [浮窗与设置页同时观察 Release 数据可能产生状态不一致] → 安装目标由全局协调器持有，历史列表只读共享模型，不反向修改安装目标。
- [Markdown 内容可能造成不可控布局或外部加载] → 使用受限展示组件、禁用不安全 HTML/嵌入，并对外链协议进行校验。
- [透明主题截图颜色可能与真实渲染不同] → 视觉验证同时检查非透明主题，并以动态主题资源和实际控件可读性为准。
- [大版本历史正文导致内存增加] → 每页仅缓存 10 个 GitHub Release 返回对象，加载更多由用户显式触发，不预取全部历史。


```

Full source: openspec/changes/enhance-update-experience/design.md

## openspec/changes/enhance-update-experience/tasks.md

- Source: openspec/changes/enhance-update-experience/tasks.md
- Lines: 1-57
- SHA256: f077c643b4649eabac18555456aacab47ed5ada0e7481935f71bb073ec56e684

```md
## 1. Release 数据与服务边界

- [x] 1.1 为正式 Release 分页、Draft/Pre-release 过滤、版本映射和缺少安装资产仍可浏览补充失败优先测试，并验证 `UpdateServiceTests` 中新增用例先失败。 <!-- comet-task:e4a05700-8775-4fe2-bc3f-7b3ea93eb46c -->
- [x] 1.2 扩展 Release 拉取接口以支持最近 10 条分页和安全元数据映射，并验证 Release 服务测试通过且日志不包含正文。 <!-- comet-task:c741dd3b-fca4-412b-bb51-5d113854351e -->
- [x] 1.3 为“下载并校验但不启动安装器”补充回归测试，并验证旧的下载即安装行为被测试拒绝。 <!-- comet-task:5883d1d3-ba19-4b7a-ba63-33e8e4f8de37 -->
- [x] 1.4 将更新包准备与安装器启动拆分为两个服务操作，支持同版本缓存重新校验，并验证未确认时启动器调用次数为零、确认后为一。 <!-- comet-task:8ebc1515-7e79-4a75-b3a8-163c3270a3ee -->

## 2. 全局更新状态机

- [x] 2.1 为自动检查后自动准备、重复请求复用、Ready、失败重试和稍后不取消下载编写协调器测试，并验证新增用例先失败。 <!-- comet-task:018e4db9-6083-42b4-bf53-08c1007d610d -->
- [x] 2.2 重构 `UpdateCoordinator` 状态和命令，使检查、下载、校验、Ready 与安装确认边界符合规格，并验证协调器测试通过。 <!-- comet-task:c0a422cc-d932-4bad-93c2-6660c065f82d -->
- [x] 2.3 将更新状态、进度和错误摘要全部接入本地化资源与文化切换刷新，并验证 CJK 扫描和资源键覆盖测试通过。 <!-- comet-task:dca76ff5-20ee-4d96-8901-1f4843133064 -->

## 3. Release Notes 共享展示

- [x] 3.1 增加 Release Notes 展示模型或适配层测试，覆盖 Markdown 替换、空正文、安全链接和不安全嵌入降级。 <!-- comet-task:cbccc188-e9d0-45bd-8a34-0902a436c24c -->
- [x] 3.2 提取基于 `LiveMarkdown.Avalonia` 的可复用 Release Notes 视图，供更新浮窗与设置页使用，并通过视图契约测试验证不再使用纯文本降级。 <!-- comet-task:4940c2eb-26c8-4e1d-b215-d4490945123e -->

## 4. 标题栏入口与更新浮窗

- [x] 4.1 为标题栏入口位置、仅在有效状态显示、Hover 文案、浮窗结构及旧右下角卡片移除编写 AXAML 契约测试，并验证新增断言先失败。 <!-- comet-task:d2a7b929-f10f-4494-9635-7b942de2f520 -->
- [x] 4.2 在窗口控制按钮左侧实现紧凑更新入口及悬停展开状态，使用动态主题资源，并验证契约测试与键盘焦点行为。 <!-- comet-task:30347da4-671a-435e-a115-f303a58cc8fc -->
- [x] 4.3 实现单层扁平更新浮窗，下载时展示 Release Note 与实时进度，校验时展示非确定进度，Ready 时展示“稍后 / 重启并安装”，失败时展示重试，并验证状态绑定测试。 <!-- comet-task:e4b31920-ee71-46e6-9979-b2edd57f4051 -->
- [x] 4.4 删除旧右下角更新卡片和独立纯文本 Release Note 弹层，确认普通 `ToastService` 反馈仍可见且不与更新浮窗重叠。 <!-- comet-task:f2f017d9-815c-4e52-96d0-40dabe1afdc9 -->

## 5. 设置页 Release 历史

- [x] 5.1 为 Release History 首次加载、10 条分页、默认选择、当前/最新标记、缓存、刷新失败保留内容和加载更多去重编写 ViewModel 测试，并验证新增用例先失败。 <!-- comet-task:adf6294f-5f85-4097-b36f-9e63ff9d419e -->
- [x] 5.2 实现独立 Release History 状态模型并接入设置页生命周期与现有更新代理配置，验证 ViewModel 测试通过。 <!-- comet-task:80883e85-89e2-4fd4-9cb9-84cc2d443fe3 -->
- [x] 5.3 在保留当前版本、自动检查、更新代理和手动检查控件的基础上，实现版本列表与 Markdown 阅读区分栏以及加载、空、失败、正常和加载更多状态，并验证设置页视图契约测试。 <!-- comet-task:4630a1ee-9224-4db6-a4b1-c6b2a0c31891 -->
- [x] 5.4 补齐简体中文、繁体中文和英文的更新入口、浮窗、历史列表、状态与辅助文案，并验证资源键对齐和运行时切换语言。 <!-- comet-task:68d86b48-6984-4993-9e0a-af0d5f93e1a4 -->

## 6. 集成验证与发布包

- [x] 6.1 运行更新服务、协调器、设置页、主窗口、本地化和敏感日志相关测试，修复本次改动引入的失败并记录结果。 <!-- comet-task:769ab435-65ec-4fce-a0ab-1fed06603a24 -->
- [x] 6.2 运行 `dotnet build LoomX.slnx -c Release --no-restore` 与必要的完整测试，确认零编译错误且仅保留已知警告。 <!-- comet-task:18416923-e484-4367-9334-e6c4aa504488 -->
- [x] 6.3 使用 CUA 验证浅色、深色和关闭透明效果后的标题栏 Hover、浮窗进度/Ready 状态、版本切换及错误/空态可读性；透明主题截图仅作为辅助证据。 <!-- comet-task:306b389c-d609-49e5-85d2-8aa1c9a769cd -->
- [x] 6.4 重新发布桌面应用，并将可运行产物放入 `outputs/` 下以 `yyyyMMdd-HHmmss-enhance-update-experience` 格式命名的目录，校验启动进程路径与发布文件完整性。 <!-- comet-task:1e00806c-1003-4407-bb4b-28ecc4d00719 -->
- [x] 6.5 更新 Comet 任务状态和验证报告，确认实现与 `desktop-update-experience` delta spec 一致且未改动无关 session 产物。 <!-- comet-task:aa115763-31df-42d5-af5a-87774b46be6a -->

## 7. 归档前 Release Notes 验收修正

- [x] 7.1 为三个约定模块的切分、默认展开、独立折叠和旧正文回退补充失败优先测试。 <!-- comet-task:a8424e08-e7a4-49e4-9471-267eb7d13514 -->
- [x] 7.2 实现 Release Notes 分段 ViewModel 与 Foldout 渲染，继续复用安全 Markdown 策略。 <!-- comet-task:0e29205f-2c44-4631-ac35-9122cc63c40e -->
- [x] 7.3 将更新说明遮罩固定为纯黑 65%，使用独立磨砂容器并相对于完整主窗口居中；右上角改为“前往发布页”。 <!-- comet-task:498183c6-ffa5-460b-91da-e7539296171f -->

## 8. Ready 安装确认与安装器修正

- [x] 8.1 集成通用应用内 `AppModalHost`，补充 Ready 入口和安装按钮的确认、取消与一次性启动测试。 <!-- comet-task:632cb215-6a6b-4ea2-aef0-ac2613eecd13 -->
- [x] 8.2 将 Inno Setup 桌面快捷方式改为无条件创建，并增加安装脚本契约测试。 <!-- comet-task:05e3eaa0-bb65-4b61-ae4a-2b16583311bb -->
- [x] 8.3 将 GitHub Release `v0.12.7` 正文更新为三个约定模块的安全测试数据，并核对 API 抓取结果。 <!-- comet-task:4ecfd11c-09a6-434e-a09e-d74784c2cfad -->

## 9. 补充验证与发布

- [x] 9.1 运行更新协调器、Release Notes、主窗口、安装器和本地化相关测试，再执行完整 Release 构建与测试。 <!-- comet-task:bfe6c322-d777-4332-b68e-7cb5bc644c7f -->
- [x] 9.2 使用 CUA 验证透明/非透明模式的遮罩、磨砂、全窗口居中、Foldout 与安装风险确认模态。 <!-- comet-task:fc1ab8c3-338c-454d-9e62-8d59b9aad60b -->
- [x] 9.3 重新发布桌面应用到 `outputs/` 下可读时间目录，校验启动进程路径和产物完整性，并更新验证报告。 <!-- comet-task:64fd542b-9cc6-4e80-b551-7cd3a4771992 -->

```

## openspec/changes/enhance-update-experience/.openspec.yaml

- Source: openspec/changes/enhance-update-experience/.openspec.yaml
- Lines: 1-3
- SHA256: 81135ec87a9e4d837ff0b50c44b20b6ea158b98cb51c326b80509d708a47f2d3

```md
schema: spec-driven
created: 2026-09-20
goal: 在保留现有更新设置、代理和校验能力的前提下，提供统一、优雅且可验证的更新流程。

```

## openspec/changes/enhance-update-experience/specs/desktop-update-experience/spec.md

- Source: openspec/changes/enhance-update-experience/specs/desktop-update-experience/spec.md
- Lines: 1-157
- SHA256: 62bd21388fad0ea96a2e5e5b9689c14a76ce35ef0e7104d2c8128cce49bba578

[TRUNCATED]

```md
## Purpose

为 Loom-X 桌面端提供低打扰、可持续感知且需要用户最终确认的更新体验，并让用户能够在设置页浏览多个正式版本的完整 Release Notes。

## ADDED Requirements

### Requirement: 自动检查后准备更新包

当自动检查更新已启用时，系统 MUST 在启动后和既定周期内检查正式版本；发现高于当前版本且具备兼容安装资产的版本后，系统 SHALL 自动下载并校验更新包，但 MUST NOT 在用户确认前启动安装器。手动检查 MUST 复用同一更新状态，且 MUST 避免并发执行重复检查或重复下载。

#### Scenario: 启动后发现新版本
- **WHEN** 自动检查已启用，应用启动检查到高于当前版本的正式 Release
- **THEN** 系统自动进入下载与校验流程，并保留用户继续使用应用的能力
- **AND** 校验完成后进入等待用户安装确认的状态

#### Scenario: 没有可用更新
- **WHEN** 自动或手动检查未发现高于当前版本的正式 Release
- **THEN** 系统保持当前版本，不显示持久更新入口
- **AND** 手动检查向用户反馈当前已是最新版本

#### Scenario: 重复触发检查
- **WHEN** 检查或下载仍在进行时，定时器或用户再次触发检查
- **THEN** 系统复用正在进行的更新流程，不创建并行的重复网络请求或下载任务

### Requirement: 标题栏提供持久更新状态入口

当存在正在准备、已准备或准备失败的更新时，系统 MUST 在窗口控制按钮左侧显示紧凑的更新状态入口。入口 MUST 默认以图标为主，并在指针悬停时展示版本与当前状态文字；用户关闭更新浮窗后，入口 MUST 保持可用，直到更新完成或不再适用。

#### Scenario: 下载期间查看状态
- **WHEN** 更新包正在下载
- **THEN** 标题栏入口显示下载状态
- **AND** 用户悬停后可以看到目标版本和当前下载百分比

#### Scenario: 更新准备完成
- **WHEN** 更新包下载和校验均已完成
- **THEN** 标题栏入口以可识别的就绪状态持续显示
- **AND** 用户点击入口可以重新打开更新浮窗

#### Scenario: 更新准备失败
- **WHEN** 下载或校验失败
- **THEN** 标题栏入口显示非阻塞的失败状态
- **AND** 用户可以通过入口打开浮窗查看安全错误摘要并重试

### Requirement: 更新浮窗直接呈现说明和操作

用户点击标题栏更新入口时，系统 SHALL 打开符合当前主题的扁平化更新浮窗，并在浮窗内直接渲染目标版本的完整 Markdown Release Note。浮窗 MUST 根据更新状态显示进度或安装操作，且关闭浮窗 MUST NOT 取消后台下载。

#### Scenario: 下载未完成时打开浮窗
- **WHEN** 用户在更新包下载期间打开更新浮窗
- **THEN** 浮窗显示目标版本的 Release Note、已下载字节数、总字节数、速度、百分比和进度条
- **AND** 用户可以选择“稍后”关闭浮窗，下载继续进行

#### Scenario: 校验期间打开浮窗
- **WHEN** 安装包已下载完成但校验尚未结束
- **THEN** 浮窗保留 Release Note，并明确显示正在校验的非确定进度状态

#### Scenario: 更新已准备完成
- **WHEN** 安装包已通过校验
- **THEN** 浮窗显示“稍后”和“重启并安装”操作
- **AND** 只有用户选择“重启并安装”后系统才启动安装器

### Requirement: 设置页浏览正式版本 Release Notes

设置页“更新”Tab MUST 保留当前版本、自动检查、更新代理和手动检查功能，并 SHALL 新增正式版本 Release Notes 浏览板块。系统 MUST 默认加载最近 10 个非 Draft、非 Pre-release 版本，默认选中最新版本，允许切换版本，并允许按页加载更多。

#### Scenario: 首次进入更新页面
- **WHEN** 用户首次进入设置页“更新”Tab
- **THEN** 系统加载最近 10 个正式版本并选中最新版本
- **AND** 最新版本标记为“最新”，与本机版本相同的条目标记为“当前”

#### Scenario: 切换历史版本
- **WHEN** 用户在版本列表中选择另一个已加载版本
- **THEN** 右侧阅读区立即显示该版本的完整 Markdown Release Note
- **AND** 系统不因版本切换重复请求已经缓存的内容

#### Scenario: 加载更多版本
- **WHEN** 用户在版本列表底部选择“加载更多”
- **THEN** 系统在保留当前选中版本和正文的同时追加下一页正式版本

### Requirement: Release Notes 状态与内容安全

```

Full source: openspec/changes/enhance-update-experience/specs/desktop-update-experience/spec.md
