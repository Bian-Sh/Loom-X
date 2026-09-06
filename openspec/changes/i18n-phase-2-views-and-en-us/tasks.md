## 任务

- [ ] 扫描 `LoomX/Views/*.axaml`、`LoomX/ViewModels/*.cs` 中所有硬编码中文，产出迁移清单（分文件、分命名空间）
- [ ] 补充 `LoomX/Resources/Strings.resx`：新增 `providers.*`、`gateway.*`、`placeholder.*` 键，同时补齐 MainWindowViewModel 残留键
- [ ] 迁移 `LoomX/Views/ProvidersView.axaml`：`{l:Locale}` 替换所有硬编码中文
- [ ] 迁移 `LoomX/Views/GatewayView.axaml`：`{l:Locale}` 替换所有硬编码中文
- [ ] 迁移 `LoomX/Views/PlaceholderView.axaml`：`{l:Locale}` 替换所有硬编码中文
- [ ] 迁移 `LoomX/ViewModels/GatewayViewModel.cs`：改为 `IStringLocalizer<T>`
- [ ] 迁移 `LoomX/ViewModels/MainWindowViewModel.cs`：确认 Phase 1 覆盖度，补齐漏迁的 UI 文案
- [ ] 新增 `LoomX/Resources/Strings.en-US.resx`：翻译全部键（键名与 zh-CN 完全一致）
- [ ] 校验 `.csproj` 卫星程序集配置，`dotnet build` 后确认 `en-US/LoomX.resources.dll` 存在
- [ ] 新增 `LoomX.Tests/LocalizationNoCjkTest.cs`：扫描 AXAML/CS 无 CJK 字符（排除 Resources/）
- [ ] 新增 `LoomX.Tests/LocalizationResourceParityTest.cs`：en-US 键数 ≥ zh-CN，无遗漏
- [ ] 运行 `dotnet build` 与 `dotnet test` 全绿
- [ ] 更新 `docs/i18n.md`：Phase 2 迁移范围与已支持语言列表
- [ ] 更新 `docs/i18n.md`：Phase 3 待办（XLIFF 工作流接入）

<!-- review skipped: pending build phase selection -->
