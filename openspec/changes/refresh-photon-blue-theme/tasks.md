# Tasks

## 1. Photon Blue 主题令牌

- [x] 1.1 在现有 View 契约测试中新增 Light/Dark 主题令牌断言，覆盖主强调色、辅助高亮、关键冷中性文字/表面色及受透明协调器管理资源仍为 `SolidColorBrush`；运行定向测试并确认它因旧青绿色令牌而失败。 <!-- comet-task:904a92ed-592f-4f42-82ec-612c8ad665e1 -->
- [x] 1.2 最小修改 `LoomX/Styles/VisualTokens.axaml` 的 Light/Dark 现有资源，使主强调色分别为 `#2563EB` 与 `#60A5FA`、辅助高亮为 `#38BDF8`，并完成冷中性色校准；运行新增令牌测试以及窗口外观、设置页和插件 UI 相关测试并确认通过。 <!-- comet-task:a3aed4b7-41f5-455f-b8ba-ca924c284da1 -->

## 2. 侧栏图标状态语义

- [x] 2.1 在 `MainWindowNavigationContractTests` 中新增侧栏图标契约，要求 `Path.nav-icon` 默认使用 `TextSecondaryBrush`、当前导航项内图标使用 `AccentBrush`；运行定向测试并确认它因现有无条件 Accent 规则而失败。 <!-- comet-task:fb2ed6c2-f9c4-434e-ab7f-1c6d17e43627 -->
- [x] 2.2 最小修改 `LoomX/MainWindow.axaml` 的导航图标样式并审查直接 `AccentBrush` 引用，只修复无强调语义的默认内容；运行导航契约测试并确认未选中与选中状态规则通过。 <!-- comet-task:7c627a48-949d-42a3-85d7-442c3a9e0a35 -->

## 3. 集成验证

- [x] 3.1 运行 `LoomX.Tests` 中主题、导航、设置、插件呈现和 Provider 视图相关测试，确认 Photon Blue 资源未破坏主题切换、插件语义映射或现有控件契约。 <!-- comet-task:c1742a8d-55bd-4850-b7f9-5870c46329e5 -->
- [x] 3.2 构建完整解决方案并检查 Git diff，确认没有修改 Success/Warning/Danger 语义、应用图标、数据库/API/插件协议或任务范围外文件。 <!-- comet-task:1e62f052-5b01-4afe-995e-6cdf9e7c2a1a -->

## 4. 打包与桌面视觉验收

- [x] 4.1 使用项目现有发布方式重新打包桌面应用到 `outputs/<可读时间>/`，并验证发布目录包含可启动的 Loom-X 可执行文件及所需插件负载。 <!-- comet-task:1025daab-abea-4204-85f8-a5e9fa24beb9 -->
- [x] 4.2 使用 `Start-Process -FilePath <绝对 exe 路径>` 隐藏启动发布包并校验进程路径；通过 CUA 仅截取应用窗口，验收浅色、深色和至少一种透明开关组合下的文字层级、普通/选中侧栏图标、主强调色与辅助高亮，且不把透明背景混色当作精确色值证据。 <!-- comet-task:77513b03-e84b-4a3e-9e86-631146f1733f -->

## Workflow follow-up

- 在 Comet Verify 阶段记录自动化测试、构建、打包与 GUI 验收结果。
- 满足项目评审要求后归档该 change，并验证归档结果。
