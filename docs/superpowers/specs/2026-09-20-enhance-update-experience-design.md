---
comet_change: enhance-update-experience
role: technical-design
canonical_spec: openspec
---

# Loom-X 桌面端更新体验深度设计

## 1. 设计依据

本设计细化 `enhance-update-experience` 的 OpenSpec 产物。行为契约以 `openspec/changes/enhance-update-experience/specs/desktop-update-experience/spec.md` 为准；本文只定义实现边界、状态所有权、UI 结构、并发控制、失败恢复和测试策略。

当前实现具有以下约束：

- `UpdateService` 同时完成 GitHub Release 拉取、安装包下载、SHA-256 校验和安装器启动。
- `UpdateCoordinator` 是主窗口与设置页共享的更新状态源，但当前下载结束后会立即启动安装器。
- 主窗口标题栏只有拖动区和三个窗口按钮；更新 UI 位于右下角卡片，并通过另一个纯文本遮罩查看说明。
- 设置页更新 Tab 只有版本、自动检查、代理和手动检查。
- 项目已经引用 `LiveMarkdown.Avalonia`，并在助手页面通过 `ObservableStringBuilder` 与 `MarkdownRenderer` 渲染 Markdown。
- 应用退出路径负责停止网关、释放数据存储、ViewModel 和日志；更新安装不得绕过该退出路径。

## 2. 总体结构

```text
                     ┌──────────────────────┐
                     │    UpdateService     │
                     │ Release / Prepare /  │
                     │ Launch verified pkg  │
                     └──────────┬───────────┘
                                │ IUpdateService
                 ┌──────────────┴──────────────┐
                 │                             │
        ┌────────▼─────────┐          ┌────────▼──────────┐
        │ UpdateCoordinator│          │ReleaseHistory VM │
        │ 单一安装状态事实源 │          │分页/缓存/选择/空态 │
        └───────┬──────────┘          └────────┬──────────┘
                │                              │
      ┌─────────┴─────────┐                    │
      │                   │                    │
┌─────▼──────┐    ┌───────▼────────┐   ┌──────▼─────────┐
│标题栏更新入口│    │更新确认浮窗      │   │设置页更新 Tab   │
└────────────┘    └───────┬────────┘   └──────┬─────────┘
                           │                   │
                           └────────┬──────────┘
                                    ▼
                         ┌────────────────────┐
                         │ ReleaseNotesView   │
                         │ 受限只读 Markdown  │
                         └────────────────────┘
```

核心规则：

1. `UpdateCoordinator` 只持有当前安装目标，不持有历史列表。
2. `ReleaseHistoryViewModel` 只浏览 Release，不改变当前安装目标。
3. 更新浮窗和设置页共享展示组件，但各自拥有独立的 Markdown 内容 ViewModel。
4. 自动下载只能产出“已验证安装包”，不能启动安装器。
5. 安装器启动和应用退出必须由一次性安装命令触发。

## 3. 服务契约与数据模型

### 3.1 窄接口

新增仅服务更新域的 `IUpdateService`，避免为整个项目引入新的依赖注入体系：

```csharp
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(
        UpdateProxySettings settings,
        CancellationToken cancellationToken = default);

    Task<UpdateReleasePage> GetStableReleasesAsync(
        UpdateProxySettings settings,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PreparedUpdate> PrepareUpdateAsync(
        UpdateRelease release,
        UpdateProxySettings settings,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    void LaunchInstaller(PreparedUpdate preparedUpdate);
}
```

`UpdateService` 实现该接口。接口刻意不暴露 `HttpClient`、GitHub DTO 或本地缓存细节。

### 3.2 新增模型

- `UpdateReleasePage`：`Items`、`Page`、`PageSize`、`HasMore`。
- `PreparedUpdate`：`Version`、`InstallerPath`、`VerifiedAt`。
- `ReleaseHistoryItemViewModel`：Release 引用、版本文本、发布日期文本、`IsLatest`、`IsCurrent`。
- `ReleaseNotesContentViewModel`：当前标题、日期、Markdown 构建器和空正文状态。

`PreparedUpdate` 只允许由校验成功路径创建。`LaunchInstaller` 在调用前再次检查安装器文件仍存在，并拒绝版本或路径为空的对象。

### 3.3 Release 分页规则

对外分页以“正式 Release”为单位，每页 10 条。服务内部可以读取多个 GitHub API 原始页，直到收集到目标页所需的正式 Release 或远端没有下一页；Draft、Pre-release 和无法解析的 tag 不占正式分页名额。

历史列表允许展示缺少安装资产的正式 Release。`CheckAsync` 则只选择：

- 版本高于当前版本；
- 非 Draft；
- 非 Pre-release；
- 同时具有符合命名契约的安装器和 `.sha256` 资产。

GitHub 分页请求必须设置合理的 `per_page`，读取响应中的分页信息，不通过猜测固定总页数判断 `HasMore`。

## 4. 更新包准备与缓存

### 4.1 准备流程

```text
选择可安装 Release
        │
        ▼
检查同版本缓存文件 ──有效且校验通过──▶ 返回 PreparedUpdate
        │无效
        ▼
下载临时安装器与校验文件
        │
        ▼
计算 SHA-256 并比较
        │失败
        ├────────▶ 删除无效临时文件并报错
        ▼成功
原子替换为稳定缓存文件
        │
        ▼
返回 PreparedUpdate；不启动安装器
```

缓存继续位于系统临时目录下的 Loom-X 更新目录，不写入设置数据库。重新启动应用后，检查到同一版本时允许重新校验并复用已有安装器；校验失败必须删除缓存并重新下载。

下载先写入带 `.partial` 后缀的临时文件，完成后再替换稳定文件，避免进程退出留下看似完整的安装器。取消、网络失败或校验失败时尽力清理 `.partial` 文件，但不得删除其他版本目录。

### 4.2 安装流程

“重启并安装”命令执行：

1. 通过原子标志阻止重复点击。
2. 确认当前状态为 `Ready`，且 `PreparedUpdate` 与当前 Release 版本一致。
3. 调用 `LaunchInstaller` 启动已验证安装器。
4. 通过注入的桌面关闭回调请求正常退出应用。
5. 正常退出路径继续停止网关、释放数据库和刷新日志。

安装器仍使用 Inno Setup 的 `CloseApplications=yes`、`RestartApplications=yes` 与 `[Run]` 启动 LoomX。应用侧不自行启动第二个新版本进程。若安装器启动失败，不请求退出并恢复到 `Ready`；若安装器已经启动但退出请求失败，记录 Error 并提示用户手动关闭应用，不重复启动安装器。

## 5. UpdateCoordinator 状态机

### 5.1 状态

```text
Idle
  └─检查──────────────▶ Checking
                          ├─无更新────────▶ Latest ──▶ Idle
                          ├─失败──────────▶ Error(Check)
                          └─发现更新──────▶ Downloading
                                              ├─失败──▶ Error(Prepare)
                                              └─完成──▶ Verifying
                                                          ├─失败──▶ Error(Prepare)
                                                          └─成功──▶ Ready
                                                                         └─确认安装──▶ Installing
```

保留 `Latest` 用于手动检查后的短暂反馈；自动检查无更新时不显示标题栏入口。删除长期停留的 `Available` 状态，因为发现可安装 Release 后会直接进入准备流程。

### 5.2 核心派生属性

- `IsUpdateEntryVisible`：Downloading、Verifying、Ready 或存在可重试安装目标的 Error。
- `IsDialogVisible`：仅由用户点击入口、发现更新时首次提示和“稍后/关闭”控制。
- `IsProgressVisible`：Downloading 或 Verifying。
- `IsProgressIndeterminate`：Verifying。
- `CanInstall`：Ready 且 PreparedUpdate 有效。
- `CanRetry`：错误来源为 Check 或 Prepare，并且不在并发任务中。
- `UpdateEntryText`：根据状态、版本与百分比生成的本地化文本。

### 5.3 命令

- `ToggleDialogCommand`：打开或关闭浮窗。
- `DismissDialogCommand`：关闭浮窗，不取消后台任务。
- `RetryCommand`：检查失败时重试检查；准备失败且 Release 仍有效时直接重试准备。
- `InstallAndRestartCommand`：只在 Ready 时执行一次性安装流程。
- `CheckCommand`：供设置页手动检查，复用当前检查任务。

### 5.4 并发与取消

协调器维护 `checkTask` 与 `prepareTask`，而不是仅依赖按钮禁用：

- 同一时刻只允许一个检查任务。
- 同一 Release 只允许一个准备任务。
- 手动检查遇到正在执行的检查时等待同一任务。
- 定时检查遇到正在下载或 Ready 状态时跳过，不替换用户正在处理的版本。
- 生命周期 CancellationToken 只在应用退出时取消；“稍后”不取消。
- 进度回调切回 UI Dispatcher 后再更新可绑定属性。

每次状态转换集中在一个方法中完成，以同时刷新派生属性、命令状态和结构化日志，避免散落的 `OnPropertyChanged`。

## 6. Release History 状态模型

新增 `ReleaseHistoryViewModel`，由 `SettingsViewModel` 持有：

- `ObservableCollection<ReleaseHistoryItemViewModel> Releases`
- `SelectedRelease`
- `Content`（`ReleaseNotesContentViewModel`）
- `IsInitialLoading`、`IsRefreshing`、`IsLoadingMore`
- `IsEmpty`、`HasError`、`HasCachedContent`、`HasMore`
- `LoadCommand`、`RefreshCommand`、`LoadMoreCommand`

首次进入更新 Tab 时调用 `EnsureLoadedAsync`。`SettingsViewModel.SelectedTabIndex` 在切换到更新 Tab 时触发该调用；重复进入不重新请求。

刷新规则：

1. 在临时集合中加载第一页。
2. 成功后一次性替换可见集合。
3. 尽量恢复刷新前选中的版本；不存在时选择最新版本。
4. 失败时保留旧集合和正文，只更新错误摘要。

加载更多规则：

1. 保持当前选择。
2. 请求下一正式页。
3. 按标准化版本号去重后追加。
4. 请求失败只影响列表底部状态，不覆盖当前正文。

当前版本标记比较 `AppVersion.Current` 的标准化值；最新标记只赋给当前已加载集合中版本最高的条目。

## 7. Release Notes 展示与内容安全

### 7.1 共享视图

新增 `Views/ReleaseNotesView.axaml`：

- 接收 `ReleaseNotesContentViewModel`。
- 使用 `LiveMarkdown.Avalonia.MarkdownRenderer`。
- 容器只处理排版、选择、滚动和空正文，不发网络请求。
- 浮窗与设置页分别在外层决定可用高度和滚动边界，避免 Markdown 内外双重滚动。

`ReleaseNotesContentViewModel.SetRelease` 为每次选中内容创建新的 `ObservableStringBuilder`，追加经过安全策略处理的 Markdown，并通知绑定更新；不在多个 Release 之间复用残留文本。

### 7.2 安全策略

`ReleaseNotesMarkdownPolicy` 在内容进入渲染器前执行：

- 移除原始 HTML block 与 inline HTML。
- 将图片和其他远程嵌入降级为替代文本，避免自动加载远程资源。
- 保留标题、列表、强调、引用、代码块和表格。
- HTTPS 链接保留；HTTP、文件、本地协议和无法解析的链接降级为普通文本。
- 空正文转换为本地化空说明，而不是把占位文字写入远端数据模型。

实现前通过 Context7 或包内文档核对当前 Markdig/LiveMarkdown API；若 LiveMarkdown 不暴露链接点击拦截，则在预处理阶段彻底移除非 HTTPS 目标。

## 8. 主窗口 UI

### 8.1 标题栏入口

当前标题栏 Grid 从 `*,42,42,42` 调整为 `*,Auto,42,42,42`，更新入口位于三个窗口按钮之前。入口设计：

- 高度与标题栏一致，图标命中区不小于 28×28。
- 非 Hover 时保持紧凑宽度。
- Hover/键盘聚焦时展开文字区，使用 `MaxWidth` 与 `Opacity` 过渡，避免改变窗口按钮尺寸。
- Ready 使用 `AccentSoftBrush`；下载/校验使用中性表面；错误使用现有 Danger 主题资源。
- 提供 `AutomationProperties.Name` 和与展开文字一致的 ToolTip，键盘用户不依赖 Hover。

入口不得覆盖标题栏拖动区，按钮 Pointer 事件必须阻止触发窗口拖动。

### 8.2 更新浮窗

替换旧右下角卡片和旧 Release Note 遮罩。新浮窗结构：

```text
遮罩
└─ 单个 Surface（水平拉伸、受 MaxWidth 约束）
   ├─ Header：图标、版本、发布日期、状态、关闭按钮
   ├─ Divider
   ├─ ReleaseNotesView（唯一滚动正文区）
   ├─ Divider
   └─ Footer
      ├─ Downloading：字节/速度/百分比 + ProgressBar + 稍后
      ├─ Verifying：状态 + Indeterminate ProgressBar + 稍后
      ├─ Ready：稍后 + 重启并安装
      └─ Error：安全错误摘要 + 稍后 + 重试
```

浮窗使用窗口现有动态主题资源，不写死参考截图中的黑色、金色或高强度阴影。焦点初始落在主操作或关闭按钮；Escape 等价于“稍后”，不取消下载。

## 9. 设置页 UI

更新 Tab 仍使用单个外层滚动容器，但 Release Notes 阅读区内部需要固定可用高度，以防完整 Markdown 把整个页面无限拉长。布局分两块：

1. **更新设置面板**：保留全部现有控件，更新自动检查提示文案，明确发现新版本后会后台下载。
2. **Release Notes 面板**：
   - Header：标题、刷新按钮和加载状态。
   - 左列：约 180–220px 的版本列表，展示版本、日期、最新/当前徽标及加载更多。
   - 右列：选中版本标题、日期和 `ReleaseNotesView`。

状态呈现：

- 初始加载：左侧轻量占位，右侧加载说明。
- 空态：面板中央说明暂无正式版本，并保留刷新。
- 无缓存错误：显示错误摘要和重新加载。
- 有缓存错误：列表和正文保持，Header 显示非阻塞错误与重试。
- 加载更多：只在左列底部显示进度。

版本切换只替换 `Content`，不触发远端请求。列表选择和 Markdown 滚动互不重置。

## 10. 本地化、日志与隐私

### 10.1 本地化

新增键覆盖：

- 标题栏 Checking/Downloading/Verifying/Ready/Error 文案。
- 浮窗标题、状态、进度、稍后、重试、重启并安装。
- 历史区标题、刷新、加载更多、最新、当前、空态和错误态。
- 自动检查提示中“发现后后台下载”的行为说明。

简体中文为 neutral 资源，同时补齐繁体中文和英文。动态状态由 ViewModel 监听 `LocaleService.CultureChanged` 后重新计算，不把已格式化字符串永久缓存。

### 10.2 日志

使用结构化日志记录：

- 检查开始/完成：当前版本、最新可安装版本、耗时。
- 历史拉取：正式页码、返回条数、是否有下一页、耗时。
- 下载与校验：版本、资产名、字节数、耗时、结果。
- 状态转换：旧状态、新状态、版本。
- 安装请求：版本和是否成功启动。

禁止记录 Release Body、API Key、代理凭据、Authorization、自定义 Header 值、安装文件内容或完整异常响应正文。

## 11. 文件边界

预计新增：

- `LoomX/ViewModels/ReleaseHistoryViewModel.cs`
- `LoomX/ViewModels/ReleaseNotesContentViewModel.cs`
- `LoomX/Views/ReleaseNotesView.axaml`
- `LoomX/Views/ReleaseNotesView.axaml.cs`
- `LoomX.Tests/ViewModels/UpdateCoordinatorTests.cs`
- `LoomX.Tests/ViewModels/ReleaseHistoryViewModelTests.cs`
- 必要的 Release Notes 视图契约测试。

预计修改：

- `LoomX/Services/UpdateService.cs`：接口、分页、准备/启动边界和缓存复验。
- `LoomX/ViewModels/UpdateCoordinator.cs`：状态机、并发任务、浮窗与安装命令。
- `LoomX/ViewModels/MainWindowViewModel.cs`：共享服务实例和桌面退出回调。
- `LoomX/ViewModels/SettingsViewModel.cs`：持有历史状态模型并在更新 Tab 首次选择时加载。
- `LoomX/MainWindow.axaml`：标题栏入口与新浮窗，移除旧卡片。
- `LoomX/Views/SettingsView.axaml`：更新设置和历史分栏。
- `LoomX/Resources/Strings*.resx`：新增本地化键。
- `LoomX.Tests/UpdateServiceTests.cs` 及相关视图契约测试。

不得改动设置数据库路径、活动库路径或迁移源规则。

## 12. 测试设计

### 12.1 服务测试

- Draft、Pre-release、非法 tag 不进入正式分页。
- 正式分页能跨原始 GitHub 页凑足 10 条。
- 缺少安装资产的 Release 可浏览但不可安装。
- 准备成功不会调用安装器。
- 校验失败删除无效文件并不产生 PreparedUpdate。
- 有效缓存重新校验后复用，无效缓存重新下载。
- 只有显式 LaunchInstaller 才调用启动器。

### 12.2 协调器测试

- 自动检查发现更新后依次经过 Downloading、Verifying、Ready。
- 同时触发两次检查只调用一次服务。
- 稍后关闭浮窗不取消准备任务，标题栏入口仍可见。
- Prepare 错误保留目标 Release，重试从准备阶段继续。
- Ready 状态重复点击只启动一次安装器。
- 安装器启动失败不请求退出。
- 手动检查无更新返回 Latest 反馈，自动检查无更新不显示入口。

### 12.3 历史状态测试

- 第一次加载 10 条并默认选择最高版本。
- 当前与最新标记独立计算。
- 切换版本不增加网络调用。
- 加载更多保留选择并去重追加。
- 刷新失败保留旧集合和 Markdown。
- 空响应进入空态；首次失败进入可重试错误态。

### 12.4 视图与本地化测试

- 标题栏 Grid 和更新入口位置契约。
- 不再存在旧 `Update.CardVisible` 右下角卡片。
- 浮窗包含共享 Markdown 视图与四类 Footer 状态。
- 设置页原四项能力仍存在，同时包含版本列表和 Markdown 区。
- 所有新增资源在 zh-CN、zh-TW、en-US 中键覆盖一致。
- 用户可见 ViewModel 状态无硬编码中文。
- 安全策略拒绝 HTML、图片和非 HTTPS 链接。

### 12.5 集成与视觉验证

- 运行相关测试后执行 Release 构建与必要完整测试。
- 通过受控假 Release 或测试注入展示 Downloading、Verifying、Ready、Error，不依赖真实发布新版本。
- CUA 只截取 Loom-X 应用，验证标题栏 Hover、浮窗操作、设置页切换、加载/空/错误态。
- 分别验证浅色、深色和关闭透明效果；透明主题截图不能单独作为配色结论。
- 修改完成后重新发布到 `outputs/` 下可读的时间命名目录，并校验实际进程路径。

## 13. 实施顺序与回滚

实施按以下依赖顺序进行：

1. 服务测试与 `IUpdateService`/分页模型。
2. 准备与启动安装器边界。
3. 协调器状态机测试与实现。
4. Markdown 安全策略和共享视图。
5. 标题栏入口与浮窗。
6. Release History ViewModel 与设置页。
7. 本地化、契约测试、构建、CUA 和发布包。

若实现中需要回滚，旧更新卡片可以在不改数据库的情况下恢复；服务层新增分页和准备边界可继续保留。任何回滚都不得恢复“自动下载后立即启动安装器”的行为。

## 14. 归档前验收补充设计

2026-09-21 的验收反馈扩大了更新体验的最终交付范围，以下内容覆盖此前“Ready 时重新打开 Release Notes 并直接安装”的交互：

1. `updateDialogOverlay` 改为完整窗口覆盖层，遮罩固定 `#A6000000`，不进入 `WindowAppearanceCoordinator` 的透明度资源缩放。弹窗相对于完整窗口居中，不再为左侧导航栏保留 `228px` 偏移。
2. 更新说明容器使用独立的 `ExperimentalAcrylicBorder` 材质；Avalonia 不支持 Acrylic 时使用高不透明度主题表面回退。磨砂材料只负责内容容器，外层遮罩始终固定纯黑 65%。
3. `ReleaseNotesContentViewModel` 在完成安全清洗后，将三个约定的二级标题切分为独立 section。每个 section 持有自己的 `ObservableStringBuilder` 与默认 `IsExpanded = true`；旧正文没有约定标题时回退为单一兼容 section。
4. 右上角关闭按钮替换为“前往发布页”，通过当前 `UpdateRelease.HtmlUrl` 打开 HTTPS 页面；关闭动作继续由 Footer 的“稍后”承担。
5. Ready 状态下，标题栏入口和浮窗安装按钮统一调用应用内确认流程。确认流程由可复用 `AppModalHost` 提供，不创建独立 Window；正文明确提示应用重启、路由服务短暂中断和进行中请求可能失败。取消后保持 Ready，确认后才调用现有一次性安装命令。
6. Inno Setup 桌面快捷方式从默认未勾选任务改为无条件创建，覆盖全新安装与升级。
7. 使用真实 GitHub Release `v0.12.7` 的测试正文验证三段结构；正文只包含虚构的安全测试信息，不包含敏感信息。

### 14.1 补充测试顺序

1. 先补 Release Notes 分段、默认展开、旧正文回退测试，并确认旧实现失败。
2. 补主窗口固定遮罩、完整窗口居中、发布页按钮和 Acrylic 容器契约测试。
3. 补 Ready 入口不再打开 Release Notes、确认/取消安装风险模态的协调器测试。
4. 补安装器桌面快捷方式无条件创建的文本契约测试。
5. 完成实现后运行相关测试、完整 Release 构建、透明/非透明 CUA 验证，并重新发布到可读时间目录。
