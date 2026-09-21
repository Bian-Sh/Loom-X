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
