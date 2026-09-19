# Task 4 Fix round 1 修复报告

## 范围与技术核验

- 仅处理 Task 4 首轮审查反馈，未推进 Task 5。
- Important 1 核验成立：原 `toml.read` 原样序列化 `TopLevelKeys`；原 `toml.get` 虽递归遮蔽敏感值，但读取父对象时仍保留 `api_key`、`token` 等敏感属性名。
- Important 2 核验成立：原 value Schema 只限制顶层字符串，数组缺少 `items`，对象缺少递归 `additionalProperties` 与 `propertyNames` 长度约束，与运行时递归校验不一致。
- Minor 日志项核验为测试覆盖缺口：真实 `AgentLoop` 当前已经不会把 Patch JSON、敏感用户文本、服务错误详情或 TOML 工具内部异常文本写入 ToolResult/捕获日志；新增聚焦测试在修复前即通过，因此没有修改 `AgentLoop`。
- 未修改 `SensitiveKeyPolicy`；修复限定在 `TomlTools` 安全序列化边界，避免影响 Task 1–3 既有契约。

## RED 证据

先只修改测试并执行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlToolsTests|FullyQualifiedName~ToolRegistryTests|FullyQualifiedName~AgentLoopTests"
```

结果：退出码 `1`，共 `45` 项，`4` 项失败、`41` 项通过。

预期失败：

1. `Read_隐藏敏感顶层键并传递取消令牌`：结果仍包含 `api_key`。
2. `Get_读取父对象时隐藏敏感属性名和值`：父对象结果仍包含 `api_key`。
3. `TomlTools_ValueSchema递归限制嵌套字符串和对象属性名(toml.set)`：value 缺少 `#/$defs/tomlValue` 引用。
4. `TomlTools_ValueSchema递归限制嵌套字符串和对象属性名(toml.patch)`：value 缺少 `#/$defs/tomlValue` 引用。

为避免把缺少节点表现为 `NullReferenceException`，随后把 Schema 测试调整为明确的 xUnit 断言并重跑聚焦测试：共 `5` 项，仍为上述 `4` 项预期失败，新增真实 `AgentLoop` 安全测试 `1` 项通过。

## 最小修复

- `toml.read`：敏感顶层键统一投影为固定占位 `[sensitive]`，保留 `top_level_keys` 数组结构，不返回原敏感键名。
- `toml.get`：在安全序列化时过滤对象中的敏感属性；数组和非敏感对象递归投影，直接读取敏感路径时继续保留既有 `***` 值遮蔽行为。
- set/patch Schema：在根 Schema 增加 `$defs.tomlValue`，value 使用 `$ref`；数组 `items` 递归引用，Object 使用 `propertyNames.maxLength` 和 `additionalProperties` 递归引用。长度上限与运行时 `MaxStringLength` 一致，Schema 不做无限展开。
- 测试：增加 read、读取父对象、set/patch 递归 Schema，以及真实 `AgentLoop` 的 Patch JSON/用户文本/服务错误/异常 sentinel 防泄漏覆盖。

## GREEN 与回归证据

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~TomlToolsTests|FullyQualifiedName~ToolRegistryTests|FullyQualifiedName~AgentLoopTests"
```

结果：退出码 `0`，`45/45` 通过。

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter "FullyQualifiedName~SensitiveKeyPolicyTests|FullyQualifiedName~TomlDocumentServiceTests"
```

结果：退出码 `0`，`59/59` 通过。`SensitiveKeyPolicy` 未修改，Task 1–3 相关回归全部通过。

```powershell
dotnet format LoomX.slnx --verify-no-changes --no-restore --include LoomX/Assistant/TomlTools.cs LoomX.Tests/Assistant/TomlToolsTests.cs LoomX.Tests/Assistant/ToolRegistryTests.cs
```

结果：退出码 `0`；仅提示“加载工作区时遇到警告”，未报告格式差异。

```powershell
git diff --check
```

结果：退出码 `0`，无空白错误。

## 风险与边界

- 未修改 OpenSpec tasks、plan、`.comet.yaml`、`.comet/subagent-progress.md`。
- 未修改数据库路径、SQLite、`AppDataPaths`、`AgentLoop` 或 `SensitiveKeyPolicy`。
- 未处理既有 NU1903、CS8618、CA2024、CS8602 警告。
- 开始时工作区干净，基线为 `5a0dcb21418f3ca4ece3d22463707bf54bfdc4be`，未发现非本任务改动。
- `git pull --ff-only` 显示 `Already up to date`，但同时报告 geometric repack 的既有坏对象 `e42a7d13307188ed6a5459b2a5c6ce4d1d47930d`；未执行 reset、clean、stash 或仓库清理。
