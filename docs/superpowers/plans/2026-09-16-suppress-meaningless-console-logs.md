---
archived-with: 2026-09-16-suppress-meaningless-console-logs
status: final
---
# 清理无意义控制台日志并移除旧数据库迁移实施计划

> **2026-09-20 对账记录：** 本 change 已于 2026-09-16 完成验证并归档；本计划中遗留的未勾选步骤现按归档任务、提交历史和复验结果统一标记完成。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (推荐) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 删除 LoomX 旧版 OllamaHub 数据库自动迁移能力，让运行时只初始化 `%LOCALAPPDATA%\LoomX` 当前数据库，同时保持已完成的无意义日志清理。

**Architecture:** 保持现有 `AppDataPaths`、`ConfigurationDatabase.InitializeAsync`、`ConfigSnapshotService` 和 `ActivityStore` 的当前路径数据流。移除独立的 `ApplicationDataMigration` 组件及所有启动入口调用，通过负向契约测试确保旧目录、旧路径常量和迁移符号不会回归。

**Tech Stack:** C#、.NET 10、Avalonia、Microsoft.Data.Sqlite、Entity Framework Core、xUnit、OpenSpec/Comet。

**Spec:** `docs/superpowers/specs/2026-09-16-suppress-meaningless-console-logs-design.md`；OpenSpec delta：`openspec/changes/suppress-meaningless-console-logs/specs/app-data-migration/spec.md`

## Global Constraints

- 运行时数据库路径唯一使用 `%LOCALAPPDATA%\LoomX\LoomX.db` 和 `%LOCALAPPDATA%\LoomX\LoomX.Activity.db`。
- 不读取、复制、校验、删除或修改 `%LOCALAPPDATA%\OllamaHub` 下的数据库文件。
- 不修改配置数据库 schema、活动数据库 schema、API 路由或日志基础设施最低级别。
- 不删除 `AssistantPreferences`、`CliVersionCache` 等与数据库迁移无关的 JSON 兼容逻辑。
- 所有文档、代码注释和提交消息使用中文；不覆盖工作区中其他会话的未提交修改。

---

### Task 1: 增加旧数据库迁移移除的失败契约测试

**Files:**
- Modify: `LoomX.Tests/AppDataPathsTests.cs`
- Modify: `LoomX.Tests/Hosting/LoomXHostTests.cs`
- Modify: `LoomX.Tests/Views/LoomXBrandingContractTests.cs`
- Test: `LoomX.Tests/ApplicationDataMigrationTests.cs`（仅作为当前迁移存在的基线，不在本任务删除）

**Interfaces:**
- Consumes: 现有源码契约测试的 `ReadSource` / `ReadRepositoryFile` 辅助方法。
- Produces: 能证明生产入口仍包含迁移调用、旧路径属性仍存在的失败测试，作为后续删除实现的回归护栏。

- [x] **Step 1: 编写负向测试断言**

  在 `AppDataPathsTests` 增加源码/反射级断言，要求 `AppDataPaths` 不暴露 `LegacyRootDirectory`、`LegacyDatabasePath`、`LegacyActivityDatabasePath` 和 `DataMigrationLockPath`，并确认当前四个 LoomX 路径仍存在。

  在 `LoomXHostTests` 和 `LoomXBrandingContractTests` 增加源码断言，要求 `LoomXHost.cs`、`App.axaml.cs` 不包含 `ApplicationDataMigration`、`EnsureMigratedAsync` 和 `new ApplicationDataMigration`。

- [x] **Step 2: 运行测试确认按预期失败**

  Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AppDataPathsTests|FullyQualifiedName~LoomXHostTests|FullyQualifiedName~LoomXBrandingContractTests" --no-restore`

  Expected: FAIL，失败原因必须来自现有迁移类型、调用或旧路径属性仍存在，不得是测试编译错误或路径错误。

- [x] **Step 3: Commit**

  ```bash
  git add LoomX.Tests/AppDataPathsTests.cs LoomX.Tests/Hosting/LoomXHostTests.cs LoomX.Tests/Views/LoomXBrandingContractTests.cs
  git commit -m "test: 锁定旧数据库迁移入口移除"
  ```

### Task 2: 删除生产代码中的旧数据库迁移入口

**Files:**
- Modify: `LoomX/App.axaml.cs`
- Modify: `LoomX/LoomXHost.cs`
- Modify: `LoomX/AppDataPaths.cs`
- Delete: `LoomX/ApplicationDataMigration.cs`
- Test: `LoomX.Tests/Hosting/LoomXHostTests.cs`
- Test: `LoomX.Tests/Views/LoomXBrandingContractTests.cs`
- Test: `LoomX.Tests/AppDataPathsTests.cs`

**Interfaces:**
- Consumes: Task 1 的负向契约测试。
- Produces: 启动流程直接使用 `AppDataPaths.DatabasePath` 和现有 `ConfigurationDatabase.InitializeAsync`，不再提供数据库迁移类型或旧路径常量。

- [x] **Step 1: 删除 App 桌面启动迁移调用**

  删除 `App.OnFrameworkInitializationCompleted` 中创建 `ApplicationDataMigration` 并同步等待 `EnsureMigratedAsync` 的代码；保留后续 `ConfigSnapshotService`、`AppDataStore` 和主窗口初始化顺序。

- [x] **Step 2: 删除 Host 启动迁移调用**

  删除 `LoomXHost.CreateAsync` 中临时迁移 logger factory、`ApplicationDataMigration` 实例和 `EnsureMigratedAsync` 调用；保留 `AppDataPaths.EnsureCreated()` 后的当前配置数据库初始化。

- [x] **Step 3: 删除旧路径属性和迁移实现**

  从 `AppDataPaths` 删除所有 `Legacy*` 和 `DataMigrationLockPath` 属性；删除 `ApplicationDataMigration.cs`，使快照、校验、临时文件、迁移锁和迁移异常类型全部消失。

- [x] **Step 4: 删除迁移专用测试并运行目标测试**

  删除 `LoomX.Tests/ApplicationDataMigrationTests.cs`。运行：

  `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~AppDataPathsTests|FullyQualifiedName~LoomXHostTests|FullyQualifiedName~LoomXBrandingContractTests|FullyQualifiedName~ConfigurationDatabase" --no-restore`

  Expected: PASS，且配置数据库初始化测试仍通过。

- [x] **Step 5: Commit**

  ```bash
  git add LoomX/App.axaml.cs LoomX/LoomXHost.cs LoomX/AppDataPaths.cs LoomX/ApplicationDataMigration.cs LoomX.Tests/ApplicationDataMigrationTests.cs LoomX.Tests/AppDataPathsTests.cs LoomX.Tests/Hosting/LoomXHostTests.cs LoomX.Tests/Views/LoomXBrandingContractTests.cs
  git commit -m "fix: 移除旧数据库自动迁移"
  ```

### Task 3: 同步当前规格和文档并确认无关迁移未受影响

**Files:**
- Modify: `openspec/specs/app-data-migration/spec.md`
- Modify: `docs/loomx-upgrade.md`
- Test: `LoomX.Tests/Services/CliVersionCacheTests.cs`
- Test: `LoomX.Tests/Assistant/AssistantPreferencesTests.cs`（如存在且相关）

**Interfaces:**
- Consumes: Task 2 的代码事实和已提交的设计文档。
- Produces: 主规格不再要求旧库迁移；当前升级说明与运行时行为一致。

- [x] **Step 1: 应用 OpenSpec delta**

  将 `openspec/changes/suppress-meaningless-console-logs/specs/app-data-migration/spec.md` 的修改/移除内容同步到 `openspec/specs/app-data-migration/spec.md`，保留当前 LoomX 路径与新安装行为，删除旧库迁移、安全复制、冲突处理和迁移失败处理要求。

- [x] **Step 2: 检查升级说明和无关迁移**

  确认 `docs/loomx-upgrade.md` 只描述当前 LoomX 数据目录；使用 `rg` 验证生产代码中仍保留 AssistantPreferences/CliVersionCache 的独立 JSON 迁移，但不存在数据库迁移符号。

- [x] **Step 3: 运行规格校验**

  Run: `openspec validate suppress-meaningless-console-logs --strict`

  Expected: PASS，且 change delta 与主规格路径一致。

- [x] **Step 4: Commit**

  ```bash
  git add openspec/specs/app-data-migration/spec.md docs/loomx-upgrade.md
  git commit -m "docs: 更新数据库路径与迁移规格"
  ```

### Task 4: 完整验证、发布和 Comet 收尾

**Files:**
- Modify: `openspec/changes/suppress-meaningless-console-logs/tasks.md`
- Create/Modify: `docs/superpowers/reports/2026-09-16-suppress-meaningless-console-logs-verify.md`
- Output: `outputs/LoomX-win-x64-2026-09-16-console-log-cleanup/` 或新的带时间目录

**Interfaces:**
- Consumes: Tasks 1-3 的提交、主规格和测试结果。
- Produces: 完整验证报告、可运行发布包和 Comet verify/archive 所需状态。

- [x] **Step 1: 运行 CodeGraph 和符号清理检查**

  Run: `npx @colbymchenry/codegraph sync`；随后查询 `ApplicationDataMigration`、`EnsureMigratedAsync`、`LegacyDatabasePath`、`DataMigrationLockPath`。

  Expected: 生产代码和测试代码无旧数据库迁移符号；历史归档文档可以保留并单独说明。

- [x] **Step 2: 运行完整测试**

  Run: `dotnet test LoomX.Tests/LoomX.Tests.csproj -c Release --no-restore`

  Expected: 0 failures；记录删除迁移测试后实际通过/跳过数量及既有 warning。

- [x] **Step 3: 构建并重新发布**

  Run: `dotnet build LoomX.slnx -c Release --no-restore`；再运行 `scripts/publish-desktop.ps1` 发布到 outputs 下新的可读时间目录。

  Expected: 0 errors，发布目录只有唯一 `LoomX.exe`。

- [x] **Step 4: 更新任务和验证报告**

  勾选所有完成任务，记录测试、构建、发布和符号检查证据，运行 `comet-state` / `comet-guard` 推进到 verify。

- [x] **Step 5: Commit and push**

  使用中文提交消息提交 Comet 状态、任务和验证报告，并推送 `origin/master`；不处理其他会话的未提交文件。
