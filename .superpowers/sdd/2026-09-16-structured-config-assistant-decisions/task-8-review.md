# Task 8 Thorough Review

日期：2026-09-17
审查分支：`codex/structured-config-assistant-decisions`
审查基线：`6bb38a0929785d1c34517d74ec6c89e514320e23`
实现提交：`04f3f906c348bd1ae06fdcc757c25e63ceace661`
审查范围：spec compliance、code quality、测试/构建配置、Task 8 交付证据。
约束：本次仅写入本报告，未修改生产代码、测试、OpenSpec、计划或 Comet 状态。

## 结论

**Changes requested。**

未发现 Critical；发现 **1 条 Important** 和 **1 条 Minor**。核心新增文案、两个 Client Skill 的构建复制链路、定向/全量测试以及 formatter 基线说明整体可信，但历史会话恢复路径不会获得 Task 8 的当前安全提示，且 standalone 启动报告把 bootstrap/重复实例链路描述成了正常存活的发布进程。

## Findings

### Critical

无。

### Important

#### [I1] 恢复旧会话时不会注入当前 Task 8 系统提示，历史会话可继续使用旧的资料与挑战处理规则

- **位置**：
  - `LoomX/Assistant/AssistantService.cs:120-125`
  - `LoomX/Assistant/AssistantSessionStore.cs:221-228`、`267-294`
  - `LoomX.Harness/AgentLoop.cs:75-78`
  - 覆盖缺口：`LoomX.Tests/Assistant/AssistantServiceTests.cs:34-54`、`130-145`
- **影响**：升级后加载在 `04f3f90` 之前保存的会话时，模型收到的仍是会话文件中持久化的旧系统消息。旧提示没有“模型原生/官方资料能力 → Browser Bridge → assistant.ask_user”的顺序，也没有 Cloudflare、JS challenge 与“禁止绕过网站安全机制”的完整约束。因此 Task 8 的安全规则只可靠覆盖新建会话，不能覆盖产品支持的“恢复历史会话后继续发送”路径。
- **证据**：
  1. `LoadSessionAsync` 将存储层返回的会话直接赋给 `CurrentSession`，没有应用当前 `SystemPrompt`。
  2. `AssistantSessionStore.LoadAsync` 使用无参数的 `new AgentSession()`，其 `Options.SystemPrompt` 为空；随后原样恢复持久化消息，包括旧的 System 消息。
  3. `AgentLoop` 构造模型请求时直接发送 `session.Messages`，不会再从 `AssistantService.SystemPrompt` 补入当前策略。
  4. 新增测试只检查服务构造后的当前新会话；现有加载测试只断言历史内容恢复，没有用 pre-Task-8 会话验证实际模型请求中的系统提示。
- **建议**：把“当前 LoomX 策略系统提示”作为运行时不可变策略，在历史会话恢复或每次运行前统一替换/注入；同时避免重复持久化多个 LoomX 策略 System 消息。新增一个以 `6bb38a0` 旧提示为 fixture 的回归测试：加载旧会话、继续发送，并断言实际 `ModelRequest.Messages` 中生效的是当前 Task 8 提示及正确通道顺序。

### Minor

#### [M1] Task 8 报告的 standalone 启动描述未证明发布程序完成正常初始化，PID 叙述与同时间日志不一致

- **位置**：`.superpowers/sdd/2026-09-16-structured-config-assistant-decisions/task-8-report.md:80-87`
- **影响**：发布目录和可执行文件确实存在，但现有证据只能证明启动链路被调用，不能支持“发布实例正常存活后由本轮结束”的完整结论，降低交付报告可复核性。
- **证据**：
  1. 发布目录创建于 2026-09-17 15:31，两个发布 Skill 与提交中的源 Skill SHA-256 完全一致；`LoomX.dll` 也包含新增系统提示字符串，因此发布内容本身与工作树实现相符。
  2. 同一时间的 `%LOCALAPPDATA%\LoomX\logs\loomx-20260917.log:7220-7223` 记录的是 PID `10828` 的 bootstrap 子进程失败，随后检测到既有 PID `35476` 并退出；日志中没有报告所写 PID `41724` 的正常“桌面应用启动”记录。
  3. `LoomX/App.axaml.cs:48-108` 表明首个进程可能先经 Explorer/快捷方式拉起子进程并退出，之后再执行单实例检查。因此 `Start-Process -PassThru` 返回的 PID 可能只是短生命周期 bootstrap 父进程，不足以证明实际发布应用完成初始化。
  4. 发布 DLL 的 `ProductVersion` 为 `0.12.6+6bb38a0929785d1c34517d74ec6c89e514320e23`，说明产物在实现提交生成前发布；这不否定工作树内容，但也无法用程序集版本把产物追溯到 `04f3f90`。
- **建议**：重新核验时对该进程单独传入 `--allow-multiple-instances`，或仅对该启动进程设置 `LOOMX_ALLOW_MULTIPLE_INSTANCES=1`；等待日志出现与发布路径、PID 匹配的“桌面应用启动”记录后再检查 `Path` 和存活状态，并在报告中区分 bootstrap PID 与实际应用 PID。不要结束已有的其他 LoomX 实例。

## Spec compliance 核对

1. **资料通道顺序**：新系统提示（`AssistantService.cs:24`）和 codex/claude-code 两个 Skill 均表达“原生/官方能力 → Browser Bridge → assistant.ask_user”，文案短且顺序一致。Skill 在 Browser 与 AskUser 之间插入挑战暂停规则，不改变资料通道优先级。
2. **登录与挑战交还**：系统提示与两个 Skill 均包含登录、CAPTCHA、Cloudflare、JS challenge，要求立即暂停并交还用户，并明确禁止绕过网站安全机制。
3. **禁止新增能力**：`6bb38a0..04f3f90` 仅修改系统提示、两个 Skill、两组测试和 Task 报告；未新增搜索 Provider、搜索 API Key/Secret、爬虫、WebView、Cookie 注入、TLS/浏览器指纹伪装或 challenge 绕过实现。
4. **真实运行时/构建输出测试**：
   - `AssistantServiceTests` 实例化真实 `AssistantService` 并读取其会话选项，不是复制生产常量。
   - `SkillStoreTests` 通过 `SkillStore.ForInstallDirectory()` 从测试运行目录的 `Skills` 构建输出加载两个内置 Skill；`LoomX.csproj:50-51` 的 Content/CopyToOutputDirectory 配置在定向测试与发布目录中均得到验证。
   - 但历史会话运行时提示未覆盖，见 I1。
5. **敏感信息边界**：本 diff 未修改日志、ToolResult、Session 序列化或 Toast；新增文本不含用户数据或 Secret，未发现新增泄漏路径。
6. **Formatter/编码**：复跑得到退出码 2、1258 条 error、2 条 warning、165 个文件、164 条 CHARSET、68 条 WHITESPACE，与 Task 报告完全一致。三个改动 C# 文件中只有 `SkillStoreTests.cs:1` 命中 CHARSET；其基线与实现提交首字节均为 `75 73 69`，因此不是本 diff 引入。未要求全仓转码。
7. **Task 8 报告证据**：定向 223、全量 873、构建、OpenSpec 与发布文件内容均可复核；standalone 正常启动叙述存在 M1 所述证据缺口。
8. **OpenSpec/计划勾选**：按审查要求，由协调者负责，不作为实现 finding。

## 独立验证记录

- `git diff --check 6bb38a0..04f3f90`：通过。
- 定向测试：通过 223，失败 0，跳过 0。
- `dotnet build LoomX.slnx --no-restore`：通过，0 error、2 warning（NU1903）。
- `dotnet test LoomX.slnx --no-build`：通过 873，失败 0，跳过 0。
- `dotnet format LoomX.slnx --verify-no-changes --no-restore`：退出码 2；诊断统计与报告一致，未发现本 diff 新增 formatter/编码诊断。
- `openspec status --change structured-config-assistant-decisions --json`：退出码 0，planning artifacts 均为 done，`isPlanningComplete=true`、`isComplete=true`。
- `openspec validate structured-config-assistant-decisions --strict`：退出码 0。
- 当前 HEAD 相对 `04f3f90` 的 Task 8 实现文件无差异；后续 `1a5e8b8` 仅修改 Comet progress，未纳入本实现审查范围。

## 最终决定

**Changes requested：先修复 I1，确保恢复旧会话时同样应用当前安全系统提示；同时补正或重新采集 M1 的 standalone 启动证据。**
