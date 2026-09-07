---
change: provider-cli-identity
design-doc: docs/superpowers/specs/2026-09-07-provider-cli-identity-design.md
base-ref: 60a0d5e10a2f0801b9d4645ce07e8d49f6af1dbd
---

# 实施计划：Provider CLI 身份模拟

## 执行顺序

按依赖顺序分 5 组，组内可并行：

```
Group 1 (后端服务层)  ─┬─ 任务 1: CliIdentityService
                        ├─ 任务 2: CliVersionService
                        └─ 任务 3: CliVersionCache

Group 2 (ViewModel 层)  ── 任务 4: CliIdentityItemViewModel
                              任务 5: 扩展 ProviderEditorViewModel

Group 3 (UI 层)  ── 任务 6: ProvidersView.axaml
                      任务 7: ProvidersView.axaml.cs

Group 4 (国际化)  ── 任务 8: Strings.resx
                       任务 9: Strings.en-US.resx

Group 5 (测试 + 验证)  ── 任务 10: CliIdentityServiceTests
                            任务 11: CliVersionServiceTests
                            任务 12: CliVersionCacheTests
                            任务 13: 构建 + 桌面验证
```

## 任务明细

### 任务 1：CliIdentityService

- **文件**：`LoomX/Services/CliIdentityService.cs`（新）
- **内容**：
  - `enum CliIdentityType { ClaudeCode, Codex, Grok }`
  - `record CliIdentityProfile` 静态配置（三家 UA 模板 + 静态头 + 家族键集合）
  - `BuildCliIdentityHeaders(type, version)` 方法
  - `ApplyCliIdentity(headers, type, version)` 方法（剥除其他家族 + 合并目标）
  - `DetectCliIdentity(headers)` 反推方法
- **验证**：编译通过 + 单测（任务 10）

### 任务 2：CliVersionService

- **文件**：`LoomX/Services/CliVersionService.cs`（新）
- **内容**：
  - `GetClaudeVersionAsync()`：`GET https://registry.npmjs.org/@anthropic-ai/claude-code/latest`
  - `GetCodexVersionAsync()`：`GET https://api.github.com/repos/openai/codex/releases/latest`（`.name` 回退 `.tag_name` 去 `rust-` 前缀）
  - `GetGrokVersionAsync()`：返回默认 `1.0.6`
  - 统一 `HttpClient` 超时 5s
  - 支持走 `LoomX.UseProxy`
- **验证**：编译通过 + 单测（任务 11）

### 任务 3：CliVersionCache

- **文件**：`LoomX/Services/CliVersionCache.cs`（新）
- **内容**：
  - `ICliVersionCache` 接口
  - `LoadAsync()` / `SaveAsync()` / `IsStale(type)` 方法
  - `GetOrFetchAsync(type, force)` 方法（缓存新鲜→返回；过期→后台刷新；缺失→触发 fetch）
  - `SetUserOverride(type, version)` / `HasUserOverride(type)`（Grok 手改保护）
  - 路径：`%LocalAppData%/LoomX/cli-versions.json`
  - TTL：24h
- **验证**：编译通过 + 单测（任务 12）

### 任务 4：CliIdentityItemViewModel

- **文件**：`LoomX/ViewModels/MainWindowViewModel.cs`（在文件末尾新增）
- **内容**：
  - `CliIdentityItemViewModel : NotifyViewModel`
  - 属性：Type / DisplayName / Version（可写）/ ShortDescription / IsRecommended / IsApplied / HasNewVersion / Source
- **验证**：编译通过

### 任务 5：扩展 ProviderEditorViewModel

- **文件**：`LoomX/ViewModels/MainWindowViewModel.cs`
- **内容**：
  - 新增 `CliIdentities: ObservableCollection<CliIdentityItemViewModel>`
  - 新增 `CurrentCliIdentity: CliIdentityType?`
  - 新增 `ApplyCliIdentityCommand: IAsyncRelayCommand<CliIdentityItemViewModel>`
  - 新增 `RefreshCliVersionsCommand: IAsyncRelayCommand`
  - `LoadHeaders()` 时反推 `CurrentCliIdentity`
- **验证**：编译通过

### 任务 6：修改 ProvidersView.axaml

- **文件**：`LoomX/Views/ProvidersView.axaml`
- **位置**：第 112 行附近，「自定义请求头」标题右侧、"添加"按钮左侧
- **内容**：
  - `Menu` 组件
  - MenuItem 模板（CLI 名称 + 版本 + 推荐 + 已应用）
  - 「刷新版本」小按钮
- **验证**：编译通过 + UI 手动验证

### 任务 7：修改 ProvidersView.axaml.cs

- **文件**：`LoomX/Views/ProvidersView.axaml.cs`
- **内容**：
  - Menu 事件绑定（MenuOpening / MenuItemClick）
  - 版本号内联编辑事件（如需）
- **验证**：编译通过 + UI 手动验证

### 任务 8：Strings.resx 新增键

- **文件**：`LoomX/Resources/Strings.resx`
- **内容**：14 个 `providers.cli.*` 键（见 tasks.md 任务 8）
- **验证**：编译通过

### 任务 9：Strings.en-US.resx 对应翻译

- **文件**：`LoomX/Resources/Strings.en-US.resx`
- **内容**：14 个对应英文翻译
- **验证**：编译通过 + 卫星程序集生成

### 任务 10：CliIdentityServiceTests

- **文件**：`LoomX.Tests/CliIdentityServiceTests.cs`（新）
- **内容**：9 个测试用例（见 design.md §7.1）
- **验证**：`dotnet test` 全绿

### 任务 11：CliVersionServiceTests

- **文件**：`LoomX.Tests/CliVersionServiceTests.cs`（新）
- **内容**：5 个测试用例
- **验证**：`dotnet test` 全绿

### 任务 12：CliVersionCacheTests

- **文件**：`LoomX.Tests/CliVersionCacheTests.cs`（新）
- **内容**：6 个测试用例
- **验证**：`dotnet test` 全绿

### 任务 13：构建 + 桌面验证

- 命令：
  - `dotnet build LoomX.slnx` 零 error 零新 warning
  - `dotnet test` 全绿
  - `dotnet publish -c Release` 生成安装包
- 手动验证：跑 proposal.md 10 条验收场景
- **验证**：发布包存在 + 手动跑通

## 依赖关系

```
任务 1 (CliIdentityService)  ─┬─ 任务 5 (ViewModel 扩展)
                              └─ 任务 10 (CliIdentityServiceTests)

任务 2 (CliVersionService)  ─┬─ 任务 3 (CliVersionCache)
                              └─ 任务 11 (CliVersionServiceTests)

任务 3 (CliVersionCache)  ─┬─ 任务 5 (ViewModel 扩展)
                            └─ 任务 12 (CliVersionCacheTests)

任务 4 (CliIdentityItemViewModel)  ── 任务 5 (ViewModel 扩展)

任务 5 (ViewModel 扩展)  ─┬─ 任务 6 (UI)
                          └─ 任务 7 (UI code-behind)

任务 8, 9 (国际化)  ── 任务 6 (UI 需要引用 Locale)

任务 10, 11, 12 (测试)  ── 任务 13 (构建验证)
```

## 风险与缓解

- **Avalonia Menu 样式**：MenuItem 默认样式可能与 LoomX 视觉不一致，可能需要覆盖 `Menu` / `MenuItem` 类选择器
  - 缓解：先用默认样式跑通，再迭代美化
- **HttpClient 代理配置**：`LoomX.UseProxy` 的具体 API 需查看现有代码
  - 缓解：任务 2 实施前 grep 现有 HttpClient 用法
- **AppData 路径**：`AppDataPathResolver` 的 API 需确认
  - 缓解：任务 3 实施前 grep 现有用法
- **NotifyViewModel 基类**：确认现有基类的 `SetProperty` 模式
  - 缓解：任务 4 实施前读 `MainWindowViewModel.cs`

## 完成标准

- 13 个任务全部勾选
- `dotnet build LoomX.slnx` 零 error 零新 warning
- `dotnet test` 全绿
- 打包发布后手动跑通 10 条验收场景
- 所有新增/修改文件符合 LoomX 代码风格（无中文硬编码、无 TODO 遗漏）
