# Task 3 fix round 1 报告

## 修复范围

仅修改以下允许文件：

- `LoomX/Assistant/Configuration/TomlDocumentService.cs`
- `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-3-fix-1-report.md`

未修改 OpenSpec tasks、Comet 状态或计划文件，也未修改 `TomlFileOperations.cs`。

## 实现摘要

1. 在日志边界新增安全异常转换。`IOException` 与 `UnauthorizedAccessException` 仍以异常对象作为日志首参并保留异常类别，但消息只包含安全类型名、无 inner exception，不再把原始文件系统异常交给 sink。
2. 所有本服务直接记录文件系统异常的边界均使用安全异常对象：原子替换重试、临时文件清理失败、统一操作失败日志。
3. 加强事务故障回归：
   - 临时写失败 fake 先真实写入部分 `.tmp`，再抛出含完整目录、Secret、完整 TOML 的 `IOException`，并验证 finally 调用删除且无残留。
   - 临时写失败、临时验证失败、Replace 重试成功、Replace 最终失败、恢复成功、恢复失败逐项捕获 logger。
   - 每项均检查格式化消息、结构化属性、`Exception.Message`、`Exception.ToString()` 不含 Secret、完整 TOML、完整目录及注入的原始异常敏感文本；同时检查安全异常无 inner exception。
4. 增加普通数组 `values = [1, 2]` 的 set/delete 穿越失败测试，验证原文不变且不创建 `.bak`/`.tmp`。

## TDD 证据

### RED

首次使用命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlDocumentServiceTests&Name~PatchAsync"
```

当前 xUnit 适配器未匹配到测试，因此改用可稳定匹配的完整测试类筛选器：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：退出码 `1`；共 46 个测试，42 个通过、4 个失败。失败均为新增日志安全断言捕获到原始异常对象泄漏，关键输出包括：

- `Exception.Message` / `Exception.ToString()` 包含 `secret-temp-write-123`、完整临时目录和 `api_key = "old"`。
- Replace 重试成功与最终失败日志中的异常对象包含注入的 Secret、完整目录和完整 TOML。
- 恢复失败日志中的 `UnauthorizedAccessException` 保留了敏感原始消息。

普通数组 set/delete 测试在 RED 阶段已经通过，证明生产代码原有拒绝行为存在，本轮补齐的是审查要求的显式回归覆盖；需要生产修复的缺陷由四个日志安全失败准确锁定。

### GREEN

最小生产修复后运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：退出码 `0`；46 个通过，0 个失败，0 个跳过。

## 最终验证

### 全部 TomlDocumentServiceTests

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：退出码 `0`；46 个通过，0 个失败。

### Task 1 契约/敏感策略回归

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlModelsTests|FullyQualifiedName~SensitiveKeyPolicyTests"
```

结果：退出码 `0`；39 个通过，0 个失败。

第一次最终验证曾将两个 `dotnet test` 与 `dotnet format` 并行启动，其中契约回归因并发写入同一 `LoomX/obj/Debug/net10.0/LoomX.GeneratedMSBuildEditorConfig.editorconfig` 返回文件占用错误。按 `systematic-debugging` 检查错误、进程与触发方式后，确认根因是验证命令间的构建产物竞争，不是代码或测试失败；改为顺序执行后上述 39 项回归通过。

### 定向格式

先执行定向格式化：

```powershell
dotnet format LoomX.slnx --no-restore --include LoomX/Assistant/Configuration/TomlDocumentService.cs LoomX.Tests/Assistant/TomlDocumentServiceTests.cs --verbosity minimal
```

随后验证：

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/Configuration/TomlDocumentService.cs LoomX.Tests/Assistant/TomlDocumentServiceTests.cs --verbosity minimal
```

结果：均退出码 `0`；仅报告仓库既有“加载工作区时遇到警告”，目标文件无格式诊断。

### Diff 检查

```powershell
git diff --check
```

结果：退出码 `0`。

## 自审

- 安全异常不保留原始异常为 inner exception，避免 Serilog/sink 展开泄漏。
- 结构化字段继续仅含文件名摘要、阶段、异常类型、尝试次数和耗时。
- 事务流程、重试次数、恢复行为与用户可见错误文案未改变。
- 未越界修改 OpenSpec/Comet 状态及其他文件。

## 既有警告与剩余风险

1. 测试仍报告仓库既有 `NU1903`：`SQLitePCLRaw.lib.e_sqlite3 2.1.11` 存在高严重性漏洞，本任务未升级依赖。
2. 构建仍报告既有 `SettingsViewModel` CS8618、`AnthropicResponseMapper` CA2024，以及其他测试文件 CS8602；本轮未新增这些警告。
3. 安全异常有意舍弃原始异常消息、stack trace 和 inner exception；诊断依赖结构化的 Stage/ErrorType/Attempt 与业务上下文，这是敏感边界要求下的明确取舍。
4. 按要求未 push；OpenSpec 2.2–2.5 由协调者在 review clean 后处理。
