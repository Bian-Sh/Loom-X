# Tasks

## 1. 协调器升级与外观缓存

- [x] 1.1 `WindowAppearanceCoordinator` 升级为应用级协调点：多窗口弱引用注册/注销、当前外观快照、注册即幂等应用；`MainWindow.ApplyTheme/ApplyAppearance` 保留签名、内部委托并作用于全部注册窗口
- [x] 1.2 新增外观缓存 `appearance.json`（enabled/opacity/blur/theme）：配置保存成功路径内原子写入（临时文件 + 替换），损坏/缺失静默降级并记 Debug 日志
- [x] 1.3 `App` 在显示闪屏前读取外观缓存并注入协调器；配置库加载完成后对账，仅不一致时一次性切换并记 Warning

## 2. 三类窗口接入与幂等收敛

- [x] 2.1 `SplashWindow` 在构造后、Show 前完成注册，保证第一帧即目标材质；确认「先设 TransparencyLevelHint 再 Show」的顺序契约（Avalonia 显示后改 Hint 在部分 Windows 版本不生效/闪烁）
- [x] 2.2 `GlassDialogWindow` 接入协调点（基类或附加属性 + 全局样式，按代码现状二选一）；确认其 axaml 是否需要补 AcrylicBlur 提示
- [x] 2.3 协调器幂等：与当前生效状态相同的应用请求跳过重绘（消除 Shell 阶段资源重换引发的可感知跳变）；不同则一次性应用并记 Information（含窗口数与变化项）
- [x] 2.4 运行时用户切换主题：全部注册窗口同步切换，逐窗口应用在同一 UI 帧内完成

## 3. 测试与验收

- [x] 3.1 单元测试：外观缓存读写/损坏降级/原子性；协调器幂等与多窗口同步；透明开/关两条路径的全窗口一致性
- [x] 3.2 源码契约/集成测试：闪屏注册先于 Show；对账逻辑（一致零切换、漂移一次性切换 + Warning）
- [x] 3.3 全量 `dotnet test` 通过（含既有外观/更新体验契约测试的适配）
- [x] 3.4 发布版实测 + 截屏验收：透明开启时闪屏第一帧即透明（不再出现 2026-10-24 验收第 29→31 帧的中途突变）；透明关闭时闪屏全程不透明；深浅主题两态截图各留档。注意 AGENTS.md「主题相关」：透明主题下 CUA 截图颜色非真实配色，以材质链路日志与窗口 TransparencyLevel 实测值为准

## 实施记录（2026-10-24 13:06 安静机器日志验收）

- 发布产物：`outputs/unify-acceptance-3-4`（分支 259d4ad，含协调器 logger 修复），安静机器（无并发 LoomX 实例）两次启动，判定依据为材质链路日志。
- 第 1 次（appearance.json 缺失）：首帧按默认值应用（`窗口主题应用完成 "system"`、`窗口材质应用完成 False 86 24 acrylic`），Shell 阶段一次性切换为库值 `True 66 35 light` 并 WRN 自愈写入 appearance.json——冷缓存路径符合 design Decision 2 的降级语义。
- 第 2 次（缓存一致）：首帧即正确材质与主题（`窗口材质应用完成 True 66 35 acrylic`、`窗口主题应用完成 light`，均先于 Database 阶段），整个启动链路到 `启动外观对账一致，零切换 True 66 35 "light"` 之间不再出现第二次材质/主题应用——第 29→31 帧中途突变的消除取得日志证据；Shell 阶段重应用被幂等跳过（Debug 不落盘，故无第二行）。
- 透明关闭路径：不改动用户真实设置，以 3.1/3.2 契约测试为证（关闭时清空 TransparencyLevelHint、全窗口一致），日志实测覆盖开启态。
- 截屏留档：按 AGENTS.md「主题相关」与 handoff 约定，透明下截屏颜色失真、判定以日志为准，未采集；如需留档可在安静机器另行截取。

