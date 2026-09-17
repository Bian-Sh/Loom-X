# Task 8 资料通道约束、安全回归与完整交付报告

日期：2026-09-17
基线：`6bb38a0929785d1c34517d74ec6c89e514320e23`

## 实现范围

- 在 `AssistantService` 的真实系统提示中固定资料顺序：模型原生或已有官方资料能力 → Browser Bridge 的 `browser.open` / `browser.read` / `browser.wait` 读取用户授权页面 → 无可用通道时调用 `assistant.ask_user` 请求用户提供资料或结论。
- 明确登录、CAPTCHA、Cloudflare、JS challenge 必须立即暂停并交还用户，禁止绕过网站安全机制。
- 在既有 `clients/codex` 与 `clients/claude-code` Skill 中加入同一短约束；未创建额外 Skill。
- 未新增搜索 Provider、搜索 API Key / Secret 配置、爬虫、WebView、Cookie 注入、TLS / 浏览器指纹伪装或挑战绕过代码。
- 未修改 OpenSpec 勾选、`.comet/subagent-progress.md` 或其他 Session 产物。

## 测试设计

- `AssistantServiceTests` 通过 `CurrentSession.Options.SystemPrompt` 检查实际进入新会话的系统提示，而不是复制常量。
- `SkillStoreTests` 通过 `SkillStore.ForInstallDirectory()` 加载构建输出中的两个内置 Client Skill，验证真实运行时资源复制与读取行为。
- 两组契约均检查通道顺序、三个 Browser Bridge 工具、四类网站挑战、立即交还用户、禁止绕过，并拒绝常见搜索 Secret 配置键。

## TDD 证据

### RED

1. 先新增 Skill 运行时资源契约，执行：

   `dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~SkillStoreTests`

   - 退出码：1。
   - 失败：2，通过：5，总计：7。
   - `codex` 与 `claude-code` 两个用例均在资料顺序断言处按预期失败。

2. 先新增系统提示契约，执行：

   `dotnet test LoomX.Tests/LoomX.Tests.csproj --no-restore --filter FullyQualifiedName~AssistantServiceTests.NewSession_SystemPrompt_DeclaresResearchChannelsAndChallengeHandoff`

   - 退出码：1。
   - 失败：1，通过：0，总计：1。
   - 在资料顺序断言处按预期失败。

说明：第一次并行运行第二条命令时遇到共享 `obj` 文件锁；随后改为串行重跑，得到上述准确 RED。没有以文件锁错误充当 RED 证据。

### GREEN

- Skill 契约：退出码 0；通过 7，失败 0，跳过 0。
- 系统提示契约：退出码 0；通过 1，失败 0，跳过 0。

## 验证结果

### 定向测试

`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~Toml|FullyQualifiedName~UserDecision|FullyQualifiedName~AssistantTools|FullyQualifiedName~AssistantServiceTests|FullyQualifiedName~AssistantViewModelTests|FullyQualifiedName~AskUserDialogContractTests|FullyQualifiedName~SkillStoreTests"`

- 退出码：0。
- 通过：223，失败：0，跳过：0。

### 完整构建与测试

- `dotnet build LoomX.slnx --no-restore`
  - 退出码：0；错误 0，警告 2。
- `dotnet test LoomX.slnx --no-build`
  - 退出码：0；通过 873，失败 0，跳过 0。

既有警告包括 `SQLitePCLRaw.lib.e_sqlite3 2.1.11` 的 NU1903；还原/发布路径可见既有 CS8618、CA2024，定向首次构建可见既有测试 CS8602。本任务不扩大范围修复。

### Formatter

`dotnet format LoomX.slnx --verify-no-changes --no-restore`

- 退出码：2。
- 精确汇总：1258 条 error 诊断、2 条 warning，涉及 165 个文件；其中 164 条 CHARSET、68 条 WHITESPACE，其余为既有分析器/格式诊断。
- 本轮三个 C# 修改文件中仅 `LoomX.Tests/Assistant/SkillStoreTests.cs(1,1)` 命中 CHARSET；其 HEAD 与工作区首字节均为 `75 73 69`（无 BOM），证明该问题在基线已存在且本轮没有改动编码。
- `AssistantService.cs` 与 `AssistantServiceTests.cs` 无 formatter 诊断。按要求未对 165 个文件做无关格式化或批量转码。

### OpenSpec

- `openspec status --change structured-config-assistant-decisions --json`：退出码 0；planning artifacts 均为 `done`，`isPlanningComplete=true`、`isComplete=true`。
- `openspec validate structured-config-assistant-decisions --strict`：退出码 0；`Change 'structured-config-assistant-decisions' is valid`。
- `tasks.md` 的 6.x / 7.x 按协调约定保持未勾选，本轮未修改。

### Standalone 发布与启动核验

- 发布命令：`dotnet publish LoomX/LoomX.csproj -c Release -r win-x64 --self-contained true -o <目录>`
- 退出码：0。
- 输出目录：`outputs/2026-09-17-1531-structured-config-assistant-decisions/`。
- 通过 `Start-Process -FilePath <绝对 LoomX.exe> -WindowStyle Hidden -PassThru` 启动 PID `41724`。
- 进程 `Path` 与 `D:\AppData\Github\Loom-X - Copy\outputs\2026-09-17-1531-structured-config-assistant-decisions\LoomX.exe` 完全一致。
- 核验时进程仍存活，随后只结束本轮启动的 PID 41724；未结束已有的其他 LoomX 进程。

## 变更文件

- `LoomX/Assistant/AssistantService.cs`
- `LoomX/Skills/clients/codex/SKILL.md`
- `LoomX/Skills/clients/claude-code/SKILL.md`
- `LoomX.Tests/Assistant/AssistantServiceTests.cs`
- `LoomX.Tests/Assistant/SkillStoreTests.cs`
- `.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-8-report.md`

## 交付检查与残留风险

- `git diff --check`：退出码 0；本轮变更文件冲突标记检查：0 处。
- `outputs/` 按仓库规则受 ignore 管理，未强行提交发布产物，也未删除任何既有输出。
- 全量 formatter 仍受仓库既有格式与编码基线阻断；本轮修改没有引入新的 formatter 诊断。
- 开始时 `git pull --ff-only` 报既有 `bad tree object e42a7d13307188ed6a5459b2a5c6ce4d1d47930d` geometric repack 错误，同时输出 `Already up to date.`；未修复或改写共享 `.git`。
