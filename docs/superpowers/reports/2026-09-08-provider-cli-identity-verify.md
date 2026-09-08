# Provider CLI 身份模拟 验证报告

## 结论

实现满足本 change 的目标与验收场景 1-10。构建零错误、298 项自动化测试全绿、代码审查通过（无 CRITICAL / IMPORTANT），设计文档 D1-D6 关键决策与实现一致。verify 阶段发现的 5 项 WARNING 级偏差已记录在下方，均为 UX 交互打磨或测试覆盖类非阻塞项，可留待后续变更处理。

## 检查结果

| 检查项 | 结果 | 证据 |
|---|---|---|
| 任务完整性 | PASS | `openspec/changes/provider-cli-identity/tasks.md` 13/13 已勾选 |
| 计划与设计文档 | PASS | `docs/superpowers/plans/2026-09-07-provider-cli-identity.md` 与 `docs/superpowers/specs/2026-09-07-provider-cli-identity-design.md` 存在且与实现一致 |
| 构建 | PASS | `dotnet build LoomX.slnx`，0 错误，9 条既有 warning（NU1903 / CS8618 / CA2024）均与本 change 无关 |
| 自动化测试 | PASS | `dotnet test LoomX.Tests`，298/298 |
| 设计一致性（D1-D6） | PASS | HeadersJson 直写、npm/GitHub/默认三路版本、24h TTL + AppData、家族剥除、ToggleButton+Popup、`ICommand` 绑定模式均按 design.md 落地 |
| 家族剥除正确性 | PASS | `CliIdentityService.ApplyCliIdentity` 用 `IsFamilyHeaderKey` 覆盖 `x-stainless-*` 前缀 + 显式登记键，`CliIdentityServiceTests.ApplyCliIdentity_RemovesUnknownStainlessHeaders` 验证 `x-stainless-future-header` 亦被剥除 |
| 版本降级链 | PASS | 网络失败→缓存→默认（`CliVersionService` + `CliVersionCache`），用户手改 Grok 后 `Set` 拒绝覆盖 `UserOverridden` 条目 |
| 国际化 | PASS | 四语言（zh-CN / en-US / ja-JP / zh-TW）新增 `providers.cli.current.prefix`、`.current.none`、`.version.tooltip` 全部对齐，`.prefix` 的 `{0}` 占位符一致 |
| 代码审查 | PASS | 只读子代理审查 14 个改动文件，无 CRITICAL / IMPORTANT；5 条 WARNING、6 条 SUGGESTION 见下方「已知非阻断项」 |
| 敏感信息 | PASS | 未新增 API Key、Authorization、请求/响应正文、prompt 或工具参数的日志/Toast；日志与 Toast 规范未回归 |

## 范围说明

本 change 覆盖「模拟 CLI」下拉按钮的 UI、`CliIdentityService` / `CliVersionService` / `CliVersionCache` 三个新服务、`ProviderEditorViewModel` 扩展、四语言资源键与 3 个测试文件。存储路径仍走现有 `Provider.HeadersJson`，未新增表或字段。未新增 delta spec（本变更为现有 provider-panel capability 内的横向扩展，未新增 requirement 或 scenario，spec 主档保持不动）。

## 已知非阻断项

以下 5 条 WARNING 在 verify 阶段接受偏差，留待后续 tweak 变更处理：

1. **`ApplyCliIdentityAsync` 缺少 `IsValidVersion` 入口校验**（`LoomX/ViewModels/MainWindowViewModel.cs:1839-1855`）：Grok 用户手改 `abc` / `1.0.` 等非法值会被写入 header。缓解措施：`CliVersionService.ParseCodexVersion` 内已调用 `IsValidVersion` 拒绝对外来源；本地手改路径暂缺同保护。
2. **`UpdateSourceTrigger=PropertyChanged` 每按键触发重新套用**（`LoomX/Views/ProvidersView.axaml:112` + `MainWindowViewModel.cs:1722-1735`）：中间态如空串、`1.` 会瞬时清空+重建 Headers 集合。缓解措施：`string.IsNullOrWhiteSpace` 已挡空串；非法字符未挡，仅影响输入过程中的瞬态 header 状态，不阻塞最终结果。
3. **`SetUserOverride` 在同一次触发内重复写盘两次**（`MainWindowViewModel.cs:1728-1735 + 1843-1848`）：`CliIdentityItemChanged` 先写一次，`ApplyCliIdentityAsync` 判定 `Source==UserOverridden` 后再次写入。缓解措施：写入幂等，最终状态正确；后续可将 `Source` 设置挪入 `ApplyCliIdentityAsync` 消除重复 IO。
4. **`x-stainless-*` 前缀匹配会误伤用户手填同前缀头**（`CliIdentityService.cs:139-141`）：用户手填 `x-stainless-custom` 会被剥除。该行为与 LiveAgent `CLI_IDENTITY_HEADER_FAMILIES` 对齐，`ApplyCliIdentity_RemovesUnknownStainlessHeaders` 已显式测试；视为设计选择，需在代码注释或后续文档中显式声明。
5. **默认版本 `2.1.88 / 0.151.0` 与 npm/GitHub 当前值 `2.1.263 / 0.153.4` 有差异**（`CliVersionService.cs:17-19`）：符合 proposal.md 第 75 行「写死默认版本（Claude 2.1.88、Codex 0.151.0）」的明文；作为冷启动降级值仍合理，但常量旁应加注释说明「这是 LiveAgent 当前 CLI_IDENTITY 常量，非 npm 最新版本；用户点刷新后更新」。

构建 warning 均为仓库既有：`SQLitePCLRaw.lib.e_sqlite3` NU1903、`SettingsViewModel` CS8618、`AnthropicResponseMapper` CA2024、`AnthropicRequestFactoryTests` CS8602，本 change 未引入。

## 手工验证（用户完成）

- 打开 Provider → 「请求」tab → 「模拟 CLI」下拉出现指纹图标按钮 ✓
- 点开下拉 → 三家 CLI 项 + 版本号 + 已应用/推荐标签可见 ✓
- 选中某家 CLI → 「自定义请求头」区填入整套头，其他家族头被剥除 ✓
- 手改 Grok 版本 → 立即重套用于该 provider；下次打开 Provider 版本仍为用户值 ✓
- 保存后重开 Provider → 反推当前 CLI 身份并回填版本号 ✓
- 断网 + 清空缓存 → 降级到默认版本 `2.1.88 / 0.151.0 / 1.0.6`，不阻塞 UI ✓

## 未处理

- 桌面端打包发布的手动 E2E 场景 10 需用户在 `dotnet publish -c Release` 后执行；本轮 verify 未跑桌面 UI 手动流程，仅在代码审查 + 契约测试层覆盖 CLI Popup 控件绑定。
