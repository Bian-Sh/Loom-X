---
change: refresh-photon-blue-theme
design-doc: openspec/changes/refresh-photon-blue-theme/design.md
base-ref: 5cbdcb1e58dce51fec79c50900c8a0c7d1606683
---

# Photon Blue 主题改造实施计划

<!-- comet-task-authority: openspec/changes/refresh-photon-blue-theme/tasks.md -->

## 实施原则

- 只修改现有主题令牌、侧栏图标样式和对应契约测试，不建立新的主题 API。
- 严格执行 RED → GREEN：先确认测试因旧青绿色值或旧导航规则失败，再修改生产 XAML。
- 保留资源键、`SolidColorBrush` 类型、透明 Alpha 协调、Success/Warning/Danger、插件协议和现有应用图标。
- 不提交 `outputs/` 发布产物，也不处理其他 session 的未跟踪文件。

## 1. 主题令牌契约与实现

<!-- comet-task-ref:904a92ed-592f-4f42-82ec-612c8ad665e1 -->

在 `LoomX.Tests/Views/SettingsViewContractTests.cs` 增加 Photon Blue 令牌契约，直接从 `VisualTokens.axaml` 加载 Light/Dark 资源并断言核心色值与画刷类型。至少覆盖 `AccentBrush`、`AccentBorderSoftBrush`、`AccentSoftBrush`、`UpdateEntryActiveBrush`、三级文字色，以及透明协调器会修改的关键 Surface 资源仍为 `SolidColorBrush`。

RED 验收：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~SettingsViewContractTests"
```

预期新增测试因旧 `#287C83` / `#53A7AD` 和旧文字色失败，且失败原因与待实现行为一致。

<!-- comet-task-ref:a3aed4b7-41f5-455f-b8ba-ca924c284da1 -->

最小修改 `LoomX/Styles/VisualTokens.axaml`：写入已确认的 Photon Blue 核心令牌，校准 Light 冷白灰蓝和 Dark 石墨蓝黑 Surface/Border/Acrylic 资源，保留原 Alpha 结构及全部语义状态色。随后运行同一定向测试和窗口外观相关测试，确认 GREEN 且透明协调行为未退化。

## 2. 侧栏图标状态契约与实现

<!-- comet-task-ref:fb2ed6c2-f9c4-434e-ab7f-1c6d17e43627 -->

在 `LoomX.Tests/Views/MainWindowNavigationContractTests.cs` 增加导航图标契约：默认 `Path.nav-icon` 使用 `TextSecondaryBrush`，`Button.nav-button.active Path.nav-icon` 使用 `AccentBrush`，现有 `Classes.active` 绑定保持不变。

RED 验收：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~MainWindowNavigationContractTests"
```

预期新增测试因当前默认 `AccentBrush` 且缺少 active 图标覆盖而失败。

<!-- comet-task-ref:7c627a48-949d-42a3-85d7-442c3a9e0a35 -->

最小修改 `LoomX/MainWindow.axaml` 的两条选择器并搜索直接 `AccentBrush` 引用，保留主按钮、焦点、选中、推荐、拖拽、处理中和插件 Accent tone 等明确强调语义。运行导航定向测试确认 GREEN。

## 3. 回归、构建与范围审查

<!-- comet-task-ref:c1742a8d-55bd-4850-b7f9-5870c46329e5 -->

运行主题、导航、窗口外观、设置、插件呈现和 Provider 视图相关测试；若出现非预期失败，先按调试协议查明根因，不把环境或无关失败当作 TDD RED。

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~SettingsViewContractTests|FullyQualifiedName~MainWindowNavigationContractTests|FullyQualifiedName~WindowAppearanceCoordinatorTests|FullyQualifiedName~PluginUiPresenterTests|FullyQualifiedName~ProvidersViewContractTests"
```

<!-- comet-task-ref:1e62f052-5b01-4afe-995e-6cdf9e7c2a1a -->

运行完整 Release 测试与构建，并检查 diff 只包含批准范围：

```powershell
dotnet test LoomX.slnx -c Release
dotnet build LoomX.slnx -c Release --no-restore
git diff --check
```

确认 Success/Warning/Danger 色值、应用图标、数据库、API、插件协议和无关中文内容未被修改。

## 4. 发布与桌面验收

<!-- comet-task-ref:1025daab-abea-4204-85f8-a5e9fa24beb9 -->

用项目发布脚本创建可读时间目录，不覆盖或清理既有 `outputs/`：

```powershell
$outputDirectory = "outputs/$(Get-Date -Format 'yyyyMMdd-HHmmss')-photon-blue-theme"
pwsh -File scripts/publish-desktop.ps1 -Configuration Release -OutputDirectory $outputDirectory
```

验证本轮目录存在 `LoomX.exe` 和 `plugins/<id>/plugin.manifest.json` 等运行负载。

<!-- comet-task-ref:77513b03-e84b-4a3e-9e86-631146f1733f -->

使用 `Start-Process -FilePath <绝对 LoomX.exe> -WindowStyle Hidden -PassThru` 启动并核对进程 `Path`。通过 CUA 仅截取应用窗口，分别检查 Light、Dark，以及至少一个透明开启/关闭组合：普通文字和未选中图标为冷中性色，选中图标和主强调状态为 Photon Blue，电光青只作少量点缀。透明截图不作为精确 RGB 证据。

## 5. 状态与交付

每项完成后，以实际 RED/GREEN、回归、构建、发布和 GUI 证据调用 `comet state task-complete`。完成 Build 所需的独立审查，解决 CRITICAL/IMPORTANT 问题，提交中文 Git 提交并运行 Build Guard 进入 Verify。
