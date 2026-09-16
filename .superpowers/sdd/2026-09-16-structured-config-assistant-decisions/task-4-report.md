# Task 4 实现报告：注册六个 TOML Assistant 工具

## 实现范围

- 新增 `TomlTools.RegisterAll(ToolRegistry, ITomlDocumentService)`，注册 `toml.read`、`toml.get`、`toml.validate`、`toml.set`、`toml.patch`、`toml.delete`。
- 为文件路径、键路径、字符串值和 Patch 操作数增加 JSON Schema 与运行时双重边界。
- 工具层只解析参数、构造 `TomlPatchOperation`、传递 `CancellationToken`、调用 `ITomlDocumentService` 并生成安全 JSON 摘要。
- `get` 在工具边界再次应用 `SensitiveKeyPolicy`；写入结果不回显输入值，所有结果均不返回完整 TOML 文本或完整文件/备份路径。
- 服务错误和异常仅返回固定错误码与安全消息，不返回服务错误详情、异常文本或工具参数。
- 在 `LoomXHost` 中以 singleton 注册 `ITomlDocumentService`，并把六个工具接入现有 `ToolRegistry` factory；未修改数据库路径、SQLite 注册或 `AppDataPaths`。
- Write/Destructive 工具继续使用现有 `AgentLoop` 风险审批语义，没有新增审批机制。

## RED 证据

执行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlToolsTests|FullyQualifiedName~ToolRegistryTests"
```

结果：失败，退出码 `1`；共 `24` 项，`19` 项失败、`5` 项通过。失败原因是 `LoomX.Assistant.TomlTools` 尚不存在，`TomlToolsTestSupport.CreateRegistry` 的 `Assert.NotNull(type)` 失败，符合“工具尚未注册”的预期红灯。

红灯前先修正了测试源码中的一个 Windows 路径转义错误；修正后测试可编译并以缺少目标功能的断言失败，而不是因测试语法错误失败。

## GREEN 证据

实现后执行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlToolsTests|FullyQualifiedName~ToolRegistryTests|FullyQualifiedName~AgentLoopTests"
```

结果：通过，退出码 `0`；共 `41` 项，`41` 项通过、`0` 项失败、`0` 项跳过。

## 格式与静态检查

定向格式验证首次发现两个既有文件不符合 `.editorconfig` 的 UTF-8 BOM 要求：

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/TomlTools.cs LoomX/LoomXHost.cs LoomX.Tests/Assistant/TomlToolsTests.cs LoomX.Tests/Assistant/ToolRegistryTests.cs
```

随后仅对本任务四个 C# 文件执行定向格式化，再执行相同 `--verify-no-changes` 命令，结果通过，退出码 `0`。命令只报告“加载工作区时遇到警告”，未报告格式错误。

最终执行：

```powershell
git diff --check
```

结果：通过，退出码 `0`，没有空白错误。

## 测试覆盖

- 六个工具名称、风险等级和必填 `path`。
- `get/set/delete` 的非空、有限长度 `key_path: string[]`。
- `patch` 的非空、有限数量 `operations`，以及只允许 `set/delete`。
- 路径、键路径、字符串值和操作数量的运行时拒绝。
- `read/get/set/patch/delete` 的服务调用与安全 JSON 输出。
- 敏感路径二次脱敏；`plain-secret` 不进入 `ToolResult` 或捕获的 `AgentLoop` 日志。
- 服务失败与异常不泄漏 Secret、完整用户路径、TOML 内容或参数。
- `CancellationToken` 原样传递并保留取消语义。
- `toml.set`（Write）与 `toml.delete`（Destructive）在拒绝逐条审批时不调用服务。

## 自审

- 未返回完整 TOML 文本。
- 未返回完整源文件路径或备份路径，只返回 `backup_created` 布尔摘要。
- 未回显 `set/patch` 输入值。
- 未添加工具层日志；固定错误输出不包含底层异常或服务错误详情。
- 未修改计划文件、OpenSpec/Comet 状态文件、数据库路径、SQLite 注册或 `AppDataPaths`。
- 未处理 NU1903、CS8618、CA2024、CS8602 等既有警告。
- 未发现非本任务工作区改动。

## 仓库同步说明

开始时工作区干净且位于 `codex/structured-config-assistant-decisions`。`git pull --ff-only` 因该分支没有 upstream 而无法执行；同时 fetch 的 geometric repack 报告缺少对象 `e42a7d13307188ed6a5459b2a5c6ce4d1d47930d`。该问题未阻塞本任务编译、测试与提交，也未对仓库执行 reset、clean、stash 或其他清理操作。