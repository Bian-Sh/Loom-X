# Task 3 实现报告

## 状态

`DONE_WITH_CONCERNS`

已完成 TOML 内存 Patch、候选验证、no-op 比较、同目录备份与临时文件、原子替换、有限重试、写后验证和失败回滚。未修改 OpenSpec tasks、Comet 状态、计划文件或其他业务模块，未 push。

## 变更文件

- `LoomX/Assistant/Configuration/TomlDocumentService.cs`
- `LoomX/Assistant/Configuration/TomlFileOperations.cs`
- `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-3-report.md`

未修改 `ITomlDocumentService.cs` 或 `TomlModels.cs`：现有 `PatchAsync` 与 `TomlWriteResult` 契约足以实现本任务；测试注入通过 `TomlDocumentService` 的 internal 构造函数完成，不扩大公共接口。

## 设计取舍

- Patch 候选通过重新解析原文得到独立 Tomlyn syntax tree，顺序应用全部操作，任一操作失败即返回且不进入文件事务。
- set 支持现有键替换、缺失普通父表创建、dotted/quoted key 真实 segment、内联表穿越；拒绝穿越标量、普通数组或数组表。delete 只删除目标键值，不清理空普通表。
- 同值 set、缺失 delete 和空操作列表保持原文不变，比较阶段直接返回 no-op，不创建备份、不写临时文件。
- `ITomlFileOperations` 仅抽象 copy/write/replace/move/delete/delay；默认实现直接调用 `File`/`Task.Delay`，测试 fake 只控制事务边界故障。
- 文件事务固定执行 `Backup → Temp Write → Validate Temp → Atomic Replace/Move → Validate Target`。现有文件使用 `File.Replace`，新文件使用 `File.Move`；两者只对 `IOException`/`UnauthorizedAccessException` 最多尝试 3 次，重试间隔 50ms，并传播取消。
- 成功保留备份；任一失败均清理本次不再需要的 `.tmp`。写后目标验证失败时从备份恢复并再次验证；恢复失败保留备份并返回安全错误。
- 日志只记录操作、文件名摘要、阶段、错误类型、尝试次数和耗时；异常对象作为日志首参，不记录完整文档、Patch 值、Secret 或完整用户目录。

## TDD 证据

### Patch 语义 RED

命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：退出码 `1`；新增 8 个 Patch 测试失败，24 个既有读取测试通过。关键失败是 `PatchAsync` 仍返回“尚未实现”，set/delete、父表创建、批量原子性和 no-op 均未满足。

最初按 brief 尝试的 `FullyQualifiedName~TomlDocumentServiceTests&Name~PatchAsync` 在当前 xUnit 适配器下没有匹配测试，因此未把该次运行计作有效 RED，随后改用上述可稳定匹配的筛选器。

### Patch 语义 GREEN

同一命令在最小候选编辑与基本写入实现后通过：退出码 `0`；32 个测试通过，0 个失败。

### 新文件 RED / GREEN

RED：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~PatchAsync_新文件"
```

结果：退出码 `1`；1 个测试失败，缺失文件仍被当作 not-found。

GREEN：实现空候选与 Move 分支后，同一命令退出码 `0`；1 个测试通过。

### 文件事务 RED / GREEN

RED：添加 fake 文件操作和故障场景后运行 TOML 服务测试，退出码 `1`；关键编译错误为 `CS0246`，缺少 `ITomlFileOperations`，证明事务注入边界尚不存在。

GREEN：新增 `TomlFileOperations.cs`、internal 注入构造函数、Replace/Move 有限重试和恢复流程后运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：退出码 `0`；43 个测试通过，0 个失败。

### 内联表 RED / GREEN

RED：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~PatchAsync_Set与Delete"
```

结果：退出码 `1`；内联表子路径被错误视为普通表冲突。

GREEN：补充内联表定位、添加和删除后，同一命令退出码 `0`；1 个测试通过。

## 最终验证

### Task 3 与 Task 2 读取回归

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：退出码 `0`；44 个测试通过，0 个失败，0 个跳过。既有 Read/Get/Validate、UTF-8、大小限制、取消、日志和敏感脱敏测试均包含在该组回归中。

### Task 1 契约与敏感策略回归

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlModelsTests|FullyQualifiedName~SensitiveKeyPolicyTests"
```

结果：退出码 `0`；39 个测试通过，0 个失败，0 个跳过。

### 定向格式验证

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/Configuration/TomlDocumentService.cs LoomX/Assistant/Configuration/TomlFileOperations.cs LoomX.Tests/Assistant/TomlDocumentServiceTests.cs --verbosity minimal
```

结果：退出码 `0`。工具仅报告既有“加载工作区时遇到警告”，没有目标文件格式或编码诊断。

### Diff 检查

```powershell
git diff --check
```

结果：退出码 `0`；已处理测试文件与服务文件的 EOF 多余空行。

## 覆盖摘要

- set、delete、父表创建、内联表穿越、标量/数组表穿越拒绝、批量中途失败不落盘。
- 注释、尾注释、未知 section、无关字段、空父表保留；同值和缺失删除 no-op。
- 新文件写入、现有文件备份、中文/空格路径、备份和临时文件同目录。
- 临时写失败、临时验证失败、Replace/Move 有限重试、最终替换失败。
- 写后目标验证失败、备份恢复成功、恢复失败、取消传播、临时文件清理。
- 敏感 Patch 值、完整 TOML 和完整用户目录不进入结构化日志或错误结果。

## 剩余风险

1. 测试与格式命令继续报告仓库既有 `NU1903`：`SQLitePCLRaw.lib.e_sqlite3 2.1.11` 存在高严重性漏洞；与本任务无关，未越界升级依赖。
2. 构建仍显示既有 `SettingsViewModel` 空引用初始化警告、`AnthropicResponseMapper` CA2024 警告，以及测试项目其他文件的 CS8602 警告；本任务新增/修改代码没有新增编译警告。
3. 本任务按代理边界未执行全解决方案测试、OpenSpec strict validate、应用重新发布或 push；应由后续集成阶段处理。
