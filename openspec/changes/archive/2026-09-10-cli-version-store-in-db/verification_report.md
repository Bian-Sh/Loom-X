# 验证报告：cli-version-store-in-db

**Change**：`cli-version-store-in-db`（tweak，workflow=tweak，language=zh-CN）
**验证时间**：2026-09-10
**验证人**：LiveAgent

## 摘要

- **结论**：PASS
- 3 类改动（DB schema、Service 后端、测试）全部构建通过；`dotnet test` 全量 512/512 通过。
- 保留 `CliVersionCache` public API 与 5 处生产调用点的向后兼容。

## 变更范围

| 文件 | 变更 |
| --- | --- |
| `LoomX/Configuration/ConfigurationDbContext.cs` | +`DbSet<CliVersionEntryEntity> CliVersionEntries`、+`CliVersionEntryEntity` 实体、+`OnModelCreating` 配置、+`EnsureSchemaAsync` 中 `CREATE TABLE IF NOT EXISTS CliVersionEntries` |
| `LoomX/Services/CliVersionCache.cs` | 完全重写为 SQLite 后端；新增 `MigrateLegacyFromJson` |
| `LoomX.Tests/Services/CliVersionCacheTests.cs` | 全部改写为 SQLite；新增 4 个迁移用例 |
| `LoomX.Tests/Services/CliVersionServiceTests.cs` | `_cachePath` → `_connectionString` |
| `openspec/changes/cli-version-store-in-db/{.comet.yaml,proposal.md,design.md,tasks.md}` | OpenSpec 产物 |

## 验收对照

| 项 | 结果 |
| --- | --- |
| `dotnet build LoomX/LoomX.csproj` 0 error 0 warning（新增） | PASS（原有 3 条历史 warning 未变） |
| `dotnet test --filter "FullyQualifiedName~CliVersion"` 36/36 | PASS |
| `dotnet test`（全量）512/512 | PASS |
| `CliVersionCache` public API 不变（Get/Set/IsStale/SetUserOverride/HasUserOverride/ClearUserOverride） | PASS |
| 5 处 `new CliVersionCache()` 无参调用未修改 | PASS（`CliVersionService.cs:36`、`MainWindowViewModel.cs` 5 处） |
| 用户覆盖 `Source=UserOverridden` 不受 TTL 影响 | PASS（`IsStale` 明确分支） |
| 迁移不覆盖用户覆盖条目 | PASS（`MigrateLegacyFromJson_DoesNotOverwriteUserOverride`） |
| 迁移成功删除源文件 | PASS（`MigrateLegacyFromJson_CopiesEntriesAndDeletesSource`） |
| 坏 JSON 保留原文件 | PASS（`MigrateLegacyFromJson_InvalidJsonKeepsFile`） |
| DB 不可用静默降级 | PASS（所有读写方法 wrap try/catch） |
| SQLite 表自动创建 | PASS（`EnsureSchemaAndMigrate` 首次触发） |

## 关键设计决定

- 主键 = `CliIdentityType` 名字（≤3 行，无需 int 自增）
- `SaveAll` 只做 upsert；`ClearUserOverride` 单独走 `DbSet.Remove`
- `EnsureSchemaAndMigrate` 顺序关键：先建表再迁移，否则 `LoadAll` 会因表不存在被 catch 吞掉数据
- `ReadLegacyJson` 启用 `JsonStringEnumConverter`（默认 System.Text.Json 不识别字符串枚举）
- SQLite 不自动创建父目录，`EnsureSchemaAndMigrate` 先 `Directory.CreateDirectory`

## 已知边界

- 本 change 与主项目分支上其他 WIP 共存（`LoomX/Assistant/*`、`LoomX/Resources/*.resx`、`LoomX/Views/ProvidersView.axaml`、部分 Assistant 测试），提交时只提交本 change 的 5 个文件，避免污染
- 生产运行时首次访问 `CliVersionCache` 会触发一次 `MigrateLegacyFromJson` 检查，无旧文件时快速返回
- 后续可扩展：如需多用户/多 profile 隔离，需要引入 profile key；本次不做

## 提交策略

- 提交 5 个文件：`ConfigurationDbContext.cs`、`CliVersionCache.cs`、`CliVersionCacheTests.cs`、`CliVersionServiceTests.cs`、`openspec/changes/cli-version-store-in-db/`（4 个新文件）
- 消息使用中文，符合项目规范
