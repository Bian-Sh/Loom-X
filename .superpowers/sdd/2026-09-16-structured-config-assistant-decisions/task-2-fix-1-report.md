# Task 2 修复报告（Round 1）

## 修改范围

- `LoomX/Assistant/Configuration/TomlDocumentService.cs`
  - 改为按字节读取并使用 `UTF8Encoding(false, true)` 严格解码。
  - 仅接受无 BOM UTF-8 与 UTF-8 BOM；显式拒绝 UTF-16 LE/BE BOM、UTF-32 LE/BE BOM及非法 UTF-8。
  - 在同步解析前后、建树前及读取、建树、数组、内联表、路径和值转换循环边界传播并检查 `CancellationToken`。
  - 日志阶段按 `ReadFile`、`ParseDocument`、`ConvertValue` 分类；日志只记录安全摘要。
- `LoomX.Tests/Assistant/TomlDocumentServiceTests.cs`
  - 新增 UTF-8/BOM、UTF-16/UTF-32/非法 UTF-8、float、父表递归脱敏、预取消、处理期间取消、日志阶段及 Patch 不产生文件副作用测试。
- 未修改接口、结果契约、计划、OpenSpec tasks、Comet 状态或其他 Session 产物。

## TDD 证据

### RED

命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

关键结果：

```text
失败: 5，通过: 21，总计: 26
```

预期失败点：

- `ReadGetValidate_一致拒绝Utf16Utf32与非法Utf8`：UTF-16 文档被错误接受，`Assert.False` 实际为 `True`。
- `GetAsync_大数组处理期间响应取消`：未抛出 `OperationCanceledException`。
- `ReadAsync_大量节点处理期间响应取消`：未抛出 `OperationCanceledException`。
- `文件读取失败日志阶段为ReadFile`：期望 `ReadFile`，实际 `ParseDocument`。
- `不支持值类型日志阶段为ConvertValue且不泄漏原值`：期望 `ConvertValue`，实际 `ParseDocument`。

同一 RED 运行中，float、父表递归脱敏、预取消及 Patch 不产生文件副作用属于审查要求的补充覆盖，原实现对应行为已经通过；修复性测试则按上述五项明确失败。

### GREEN

命令：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：

```text
已通过 - 失败: 0，通过: 26，总计: 26
```

取消重点测试另行连续运行三次：

```powershell
$filter='FullyQualifiedName~GetAsync_大数组处理期间响应取消|FullyQualifiedName~ReadAsync_大量节点处理期间响应取消|FullyQualifiedName~所有操作_预取消时抛出取消异常'
1..3 | ForEach-Object { dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --no-build --filter $filter }
```

三次均为：

```text
已通过 - 失败: 0，通过: 3，总计: 3
```

## 验证结果

### Task 2 定向测试

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~TomlDocumentServiceTests
```

结果：26/26 通过。

### Task 1 回归测试

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlModelsTests|FullyQualifiedName~SensitiveKeyPolicyTests"
```

结果：39/39 通过。

首次将 Task 2 与 Task 1 测试并行启动时，两个 `dotnet test` 进程竞争同一 `obj` 输出，Task 1 出现一次 `CS2012` 文件占用；改为顺序执行后通过，属于验证命令并发冲突，不是代码失败。

### 定向格式验证

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/Configuration/TomlDocumentService.cs LoomX.Tests/Assistant/TomlDocumentServiceTests.cs
git diff --check
```

结果：均以退出码 0 完成；`dotnet format` 仅输出既有的工作区加载警告提示。

## 自审结论

- Read/Get/Validate 共用同一读取、严格解码、解析与建树入口，编码失败错误集合保持一致。
- 取消在读取循环、解析边界、建树、key segment、数组、内联表、路径和值递归转换循环中检查；取消异常不转为普通失败结果。
- 文件读取类失败记录 `ReadFile`，语法诊断记录 `ParseDocument`，不支持值类型记录 `ConvertValue`。
- 语法错误和不支持类型日志不包含 TOML 原值、请求正文或完整路径。
- Patch 仍保持未实现契约，且不存在文件时不创建目标、备份或临时文件。

## 剩余风险

- “处理期间取消”使用 25ms 定时取消；已通过修复前 RED、修复后 GREEN及连续三次重复运行验证，但仍保留极低的机器调度时间窗口波动风险。
- 未加入 I/O 异常日志测试；跨平台文件锁和权限构造可能不稳定，本轮按审查要求避免为该可选测试扩大范围。
- 仓库现有 `NU1903`、既有编译警告及 Git `bad tree object`/当前分支无上游问题未处理，符合审查要求与任务边界。
