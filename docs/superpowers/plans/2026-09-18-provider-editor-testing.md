---
change: enhance-provider-editor-testing
design-doc: docs/superpowers/specs/2026-09-18-provider-editor-testing-design.md
base-ref: 1aab9a75f9e698651f3797e57959c2cf47445a4b
---

# Provider 编辑器与真实请求测试器实施计划

> **供代理执行者使用：** 必须按任务逐项执行；推荐使用 `subagent-driven-development`，也可使用 `executing-plans`。所有步骤使用复选框追踪。

**目标：** 将 Provider 编辑器重构为“基础 / 高级 / 模型 / 测试”四个 Tab，并实现继承当前 Provider 配置的普通与流式真实模型请求测试器。

**架构：** 使用 `ProviderCompatibilityOption` 在 UI 与现有 `ApiMode/EndpointFormat` 字段之间映射，数据库结构不变。新增 `ProviderTestService` 负责三协议请求、代理、流式解析和安全结果，新增 `ProviderTestPanelViewModel` 负责测试交互状态；`ProvidersViewModel` 只协调 Provider 选择和生命周期。

**技术栈：** .NET 10、C#、Avalonia、xUnit、`HttpClient`、`System.Text.Json`、Microsoft.Extensions.Logging、OpenSpec/Comet。

**规格：** `docs/superpowers/specs/2026-09-18-provider-editor-testing-design.md`

## 全局约束

- 所有文档、代码注释和 Git 提交消息使用中文；技术标识保持原文。
- 不修改数据库 schema，不迁移或重写已有 Provider ID，不改变模型 Tab 和 Gateway 外部 API。
- 测试请求使用当前内存编辑快照，不要求先保存。
- 日志不得包含 API Key、Authorization、自定义 Header 值、Prompt、响应正文、图片或工具参数。
- 用户可见文案必须进入 `Strings.resx`、`Strings.en-US.resx`、`Strings.zh-TW.resx`；不得新增无本地化硬编码文案。
- 不触碰无关改动：`LoomX.Tests/Views/WindowAppearanceCoordinatorTests.cs`、`openspec/changes/incremental-config-edit/.comet/trajectory.jsonl`、`.zcode/`。
- 修改任何实现后必须先运行定向测试，再运行完整测试和 Release 构建；最终重新发布到带可读时间的 `outputs` 子目录。

## 文件结构

- 新建 `LoomX/ViewModels/ProviderCompatibilityOption.cs`：三种兼容类型及字段映射。
- 新建 `LoomX/Services/ProviderTestService.cs`：测试 DTO、接口、请求构造、代理和响应解析。
- 新建 `LoomX/ViewModels/ProviderTestPanelViewModel.cs`：测试 Tab 状态与命令。
- 修改 `LoomX/ViewModels/MainWindowViewModel.cs`：自动 ID、映射接入、测试面板生命周期。
- 修改 `LoomX/Views/ProvidersView.axaml` 与 `ProvidersView.axaml.cs`：四 Tab 和复制反馈。
- 修改 `LoomX/Resources/Strings*.resx`：兼容卡片、测试状态和错误文案。
- 新建/修改对应 `LoomX.Tests` 测试文件。

---

### 任务 1：兼容类型映射与自动 Provider ID

**文件：**
- 新建：`LoomX/ViewModels/ProviderCompatibilityOption.cs`
- 修改：`LoomX/ViewModels/MainWindowViewModel.cs`
- 新建：`LoomX.Tests/Desktop/ProviderCompatibilityOptionTests.cs`
- 修改：`LoomX.Tests/Desktop/ProviderEditorViewModelTests.cs`

**接口：**
- 产出：`ProviderCompatibilityOption.All`、`FromFields(string apiMode, string endpointFormat)`、`ApplyTo(ProviderEditorViewModel provider)`。
- 产出：`ProvidersViewModel.GenerateProviderBusinessId(IEnumerable<ProviderEditorViewModel>)`，返回 `provider-xxxxxxxx`。

- [ ] **步骤 1：写兼容映射失败测试**

`ProviderCompatibilityOptionTests` 至少固定以下断言：

```csharp
[Theory]
[InlineData("openai", "chat_completions", "openai-chat")]
[InlineData("openai", "responses", "openai-responses")]
[InlineData("anthropic", "responses", "anthropic-messages")]
public void FromFields_返回稳定兼容类型(string apiMode, string endpointFormat, string expected)
{
    Assert.Equal(expected, ProviderCompatibilityOption.FromFields(apiMode, endpointFormat).Value);
}

[Fact]
public void 应用兼容类型不会修改ProviderId()
{
    var provider = new ProviderEditorViewModel { BusinessId = "provider-fixed" };
    ProviderCompatibilityOption.OpenAiChat.ApplyTo(provider);
    Assert.Equal("provider-fixed", provider.BusinessId);
    Assert.Equal("openai", provider.ApiMode);
    Assert.Equal("chat_completions", provider.EndpointFormat);
}
```

- [ ] **步骤 2：运行映射测试并确认红灯**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ProviderCompatibilityOptionTests`

预期：编译失败，提示 `ProviderCompatibilityOption` 不存在。

- [ ] **步骤 3：实现最小映射类型**

实现三个静态选项和旧值回退：

```csharp
public sealed record ProviderCompatibilityOption(string Value, string ApiMode, string EndpointFormat, string TitleKey, string DescriptionKey)
{
    public static ProviderCompatibilityOption OpenAiChat { get; } = new("openai-chat", "openai", "chat_completions", "providers.compat.chat.title", "providers.compat.chat.description");
    public static ProviderCompatibilityOption OpenAiResponses { get; } = new("openai-responses", "openai", "responses", "providers.compat.responses.title", "providers.compat.responses.description");
    public static ProviderCompatibilityOption AnthropicMessages { get; } = new("anthropic-messages", "anthropic", "responses", "providers.compat.anthropic.title", "providers.compat.anthropic.description");
    public static IReadOnlyList<ProviderCompatibilityOption> All { get; } = [OpenAiChat, OpenAiResponses, AnthropicMessages];
}
```

在 `ProviderEditorViewModel` 暴露 `SelectedCompatibility`，setter 调用 `ApplyTo`，`ApiMode/EndpointFormat` 变化时通知该属性。

- [ ] **步骤 4：写自动 ID 失败测试**

覆盖：格式、当前集合冲突重试、名称和兼容类型变化不改 ID、已有 Provider 加载保持原值。

- [ ] **步骤 5：运行 ID 测试并确认红灯**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~ProviderEditorViewModelTests|FullyQualifiedName~ProviderCompatibilityOptionTests"`

预期：新建 Provider 的 `BusinessId` 为空或生成器不存在。

- [ ] **步骤 6：实现 ID 生成并改造 NewProvider**

`NewProvider` 创建时设置：

```csharp
BusinessId = GenerateProviderBusinessId(Providers),
ApiMode = "openai",
EndpointFormat = "responses"
```

生成器循环使用 `Guid.NewGuid().ToString("N")[..8]`，按 `OrdinalIgnoreCase` 检查冲突。

- [ ] **步骤 7：运行测试并提交**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~ProviderCompatibilityOptionTests|FullyQualifiedName~ProviderEditorViewModelTests"`

预期：PASS。

提交：`git add LoomX/ViewModels/ProviderCompatibilityOption.cs LoomX/ViewModels/MainWindowViewModel.cs LoomX.Tests/Desktop/ProviderCompatibilityOptionTests.cs LoomX.Tests/Desktop/ProviderEditorViewModelTests.cs && git commit -m "实现提供商兼容类型与自动编号"`。

---

### 任务 2：测试请求 DTO、普通请求与安全结果

**文件：**
- 新建：`LoomX/Services/ProviderTestService.cs`
- 新建：`LoomX.Tests/Services/ProviderTestServiceTests.cs`

**接口：**
- 产出：`ProviderTestMode`、`ProviderTestRequest`、`ProviderTestProgress`、`ProviderTestResult`、`ProviderTestSummary`。
- 产出：`IProviderTestService.ExecuteAsync(ProviderTestRequest, IProgress<ProviderTestProgress>?, CancellationToken)`。
- 消费：`IProviderExecutionPipeline.ExecuteAsync/ExecuteStreamingAsync`。

- [ ] **步骤 1：写三协议普通请求失败测试**

使用捕获请求的假 `IProviderExecutionPipeline`，分别断言：

- OpenAI Chat URL 以 `/chat/completions` 结束，body 含 `messages` 和 `stream=false`，使用 Bearer。
- OpenAI Responses URL 以 `/responses` 结束，body 含 `input` 和 `stream=false`，使用 Bearer。
- Anthropic URL 以 `/v1/messages` 结束，body 含 `messages/max_tokens/stream=false`，使用 `x-api-key` 与 `anthropic-version`。
- 自定义 Header 被发送，但 `ProviderTestSummary` 只保存数量。

- [ ] **步骤 2：运行测试并确认红灯**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ProviderTestServiceTests`

预期：编译失败，测试服务和 DTO 不存在。

- [ ] **步骤 3：实现请求构造和普通响应解析**

`ProviderTestRequest` 使用不可变 record，并包含 `RequestId`、Provider/Model、Base URL、协议字段、API Key、Header、`UseProxy`、Prompt、模式和展示上限。普通解析最少支持：

```csharp
private static string ParseOpenAiChat(JsonNode root) => root["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
private static string ParseAnthropic(JsonNode root) => string.Concat(root["content"]?.AsArray().Select(item => item?["text"]?.GetValue<string>() ?? "") ?? []);
```

Responses 同时支持顶层 `output_text` 与 `output[].content[].text`。

- [ ] **步骤 4：写错误、截断与日志安全失败测试**

覆盖 401、404、429、5xx、非 JSON、超长正文、超时和用户取消。使用内存 Logger 断言日志不含测试 API Key、Header 值、Prompt 和响应正文。

- [ ] **步骤 5：实现安全错误分类和截断**

错误结果只保存受限 UI 摘要；日志模板仅使用 Provider、Model、协议、路径、状态码、内容类型、字节数、代理状态和耗时。`OperationCanceledException` 根据调用方 Token 区分“用户取消”和“超时”。

- [ ] **步骤 6：运行测试并提交**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ProviderTestServiceTests`

预期：PASS。

提交：`git add LoomX/Services/ProviderTestService.cs LoomX.Tests/Services/ProviderTestServiceTests.cs && git commit -m "实现提供商普通请求测试服务"`。

---

### 任务 3：流式解析、代理与 CLI 身份

**文件：**
- 修改：`LoomX/Services/ProviderTestService.cs`
- 修改：`LoomX.Tests/Services/ProviderTestServiceTests.cs`
- 修改：`LoomX/Services/ProviderTestService.cs` 内部通过构造参数 `Func<CancellationToken, Task<UpdateProxySettings>>` 读取代理设置；生产接线直接传入 `AppDataStore.GetUpdateProxySettingsAsync`，不修改 `AppDataStore.cs`。

**接口：**
- 产出：三协议 SSE 文本增量归一化。
- 产出：可注入的代理设置读取器和 HttpClient 创建器，测试中无需真实网络。
- 消费：`CliIdentityService.DetectCliIdentity/DetectCliVersion`。

- [ ] **步骤 1：写流式失败测试**

为三协议提供内存 SSE：

```text
# Chat
data: {"choices":[{"delta":{"content":"你"}}]}
data: {"choices":[{"delta":{"content":"好"}}]}
data: [DONE]

# Responses
event: response.output_text.delta
data: {"type":"response.output_text.delta","delta":"你好"}

event: response.completed
data: {"type":"response.completed"}

# Anthropic
event: content_block_delta
data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"你好"}}

event: message_stop
data: {"type":"message_stop"}
```

断言进度回调按顺序收到文本、最终结果为完整文本、超过上限标记 `IsTruncated`。

- [ ] **步骤 2：运行流式测试并确认红灯**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~ProviderTestServiceTests&Name~流式"`

预期：FAIL，尚未调用 `ExecuteStreamingAsync` 或未解析增量。

- [ ] **步骤 3：实现 SSE 帧读取和协议分派**

使用逐行读取器累计 `event:` 和多行 `data:`，空行提交一帧；`[DONE]` 结束 OpenAI 流。解析器只返回文本增量，未知事件忽略，无效 JSON 返回协议错误。

- [ ] **步骤 4：写代理与 CLI 失败测试**

断言：`UseProxy=false` 直连；system/custom 模式创建正确 Handler；无效 custom 配置返回配置错误且不静默直连；CLI Header 实际进入请求；摘要只显示身份名、版本和 Header 数量。

- [ ] **步骤 5：实现代理租约和 CLI 摘要**

自定义代理客户端按请求创建并释放；系统代理设置 `UseProxy=true` 且不显式赋值 `Proxy`。调用 `CliIdentityService` 从最终 Header 字典检测身份与版本。代理密码只存在局部变量，不写日志和结果。

- [ ] **步骤 6：运行测试并提交**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ProviderTestServiceTests`

预期：PASS。

提交：`git add LoomX/Services/ProviderTestService.cs LoomX.Tests/Services/ProviderTestServiceTests.cs && git commit -m "完善流式测试与代理身份支持"`。

---

### 任务 4：测试面板 ViewModel

**文件：**
- 新建：`LoomX/ViewModels/ProviderTestPanelViewModel.cs`
- 新建：`LoomX.Tests/Desktop/ProviderTestPanelViewModelTests.cs`

**接口：**
- 产出：`BindProvider(ProviderEditorViewModel?)`、`SendCommand`、`StopCommand`、`RetryCommand`、`ClearCommand`。
- 产出：`SelectedModel`、`Prompt`、`SelectedMode`、`ResponseText`、`Summary`、`IsRunning`、`CanSend`、`HasResult`、`HasError`。

- [ ] **步骤 1：写默认状态失败测试**

断言默认 Prompt 为“每日一言”，默认模式为常规，第一个启用真实模型被选中；只有禁用模型或无模型时 `CanSend=false`。

- [ ] **步骤 2：写生命周期失败测试**

使用可控制完成的假服务，覆盖发送、停止、重试、清空，以及切换 Provider 后旧请求被取消且晚到进度被忽略。

- [ ] **步骤 3：运行测试并确认红灯**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ProviderTestPanelViewModelTests`

预期：编译失败，ViewModel 不存在。

- [ ] **步骤 4：实现最小 ViewModel**

每次发送递增 `requestVersion`；进度回调捕获版本号并在 UI Dispatcher 上批量追加。`Retry` 保存上次不可变 `ProviderTestRequest`，不得重新读取已切换 Provider。

- [ ] **步骤 5：补齐命令状态与本地化刷新**

所有影响 `CanExecute` 的属性变化后调用 `RaiseCanExecuteChanged`；`RefreshLocalization` 仅刷新资源派生文本，不修改响应正文。

- [ ] **步骤 6：运行测试并提交**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ProviderTestPanelViewModelTests`

预期：PASS。

提交：`git add LoomX/ViewModels/ProviderTestPanelViewModel.cs LoomX.Tests/Desktop/ProviderTestPanelViewModelTests.cs && git commit -m "实现提供商请求测试面板状态"`。

---

### 任务 5：接入 ProvidersViewModel 并移除旧连接测试块

**文件：**
- 修改：`LoomX/ViewModels/MainWindowViewModel.cs`
- 修改：`LoomX.Tests/Desktop/ProviderEditorViewModelTests.cs`
- 新建：`LoomX.Tests/Desktop/ProvidersViewModelTestPanelTests.cs`

**接口：**
- 消费：`IProviderTestService`、`ProviderTestPanelViewModel.BindProvider`。
- 产出：`ProvidersViewModel.TestPanel`。

- [ ] **步骤 1：写集成失败测试**

验证构造时创建测试面板、`SelectedProvider` 变化时绑定并取消旧请求、内存中未保存的 Base URL/API Key/Header/兼容类型进入服务请求快照、Dispose 会取消请求。

- [ ] **步骤 2：运行测试并确认红灯**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~ProvidersViewModel|FullyQualifiedName~ProviderEditorViewModelTests"`

预期：FAIL，`TestPanel` 不存在。

- [ ] **步骤 3：接入测试面板**

扩展构造函数可选参数 `IProviderTestService? providerTestService = null`，默认创建真实服务；在 `SelectedProvider` setter、文化变化和 Dispose 中转发生命周期。

- [ ] **步骤 4：删除旧编辑面板连接命令状态**

删除仅服务于旧区块的 `TestConnectionCommand`、`connectionCancellation`、`ConnectionStatus` 等成员和 `TestConnectionAsync`；保留 `ProviderHealthService`、顶部统计和 `VerifyAllProvidersCommand`。

- [ ] **步骤 5：运行现有 Provider 测试并提交**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~ProviderEditorViewModelTests|FullyQualifiedName~ProviderHealthServiceTests|FullyQualifiedName~ProvidersViewModel"`

预期：PASS。

提交：`git add LoomX/ViewModels/MainWindowViewModel.cs LoomX.Tests/Desktop && git commit -m "接入提供商测试面板生命周期"`，提交前用 `git diff --cached --name-only` 确认没有加入无关测试文件。

---

### 任务 6：四 Tab UI 与本地化

**文件：**
- 修改：`LoomX/Views/ProvidersView.axaml`
- 修改：`LoomX/Views/ProvidersView.axaml.cs`
- 修改：`LoomX/Resources/Strings.resx`
- 修改：`LoomX/Resources/Strings.en-US.resx`
- 修改：`LoomX/Resources/Strings.zh-TW.resx`
- 修改：`LoomX.Tests/Views/ProvidersViewContractTests.cs`
- 修改：`LoomX.Tests/LocalizationResourceParityTest.cs`。

**接口：**
- 消费：`SelectedProvider.SelectedCompatibility` 和 `TestPanel.*`。
- 产出：复制响应代码后置调用 `ToastService.Show`。

- [ ] **步骤 1：写 XAML 契约失败测试**

断言：恰有基础/高级/模型/测试四个 Tab；XAML 不再绑定 `SelectedProvider.BusinessId`；API Key 位于基础 Tab；高级 Tab 包含代理/Header/CLI 且不含 `TestConnectionCommand`；测试 Tab 绑定模型、模式、Prompt、发送/停止/重试/复制/清空和 Response 元数据。

- [ ] **步骤 2：运行契约测试并确认红灯**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter FullyQualifiedName~ProvidersViewContractTests`

预期：FAIL，仍为三个 Tab 且旧连接区块存在。

- [ ] **步骤 3：重构基础和高级 Tab**

基础 Tab 使用三张可选卡片绑定 `SelectedCompatibility`，保留名称、Base URL、API Key；删除 ID 输入框。请求 Tab 文案改为高级并只保留代理、自定义 Header、CLI/UA。

- [ ] **步骤 4：新增测试 Tab**

布局固定为请求配置、摘要条、深色终端式 Response 三段；所有颜色使用动态资源。进行中显示停止，完成/失败显示重试、复制和清空；无模型时显示本地化空态。

- [ ] **步骤 5：实现复制反馈**

在 `ProvidersView.axaml.cs` 中读取 `DataContext.TestPanel.ResponseText`，写入 Avalonia Clipboard，成功后调用 MainWindow 注入的 `ToastService`；不得复制请求摘要中的敏感信息。

- [ ] **步骤 6：补齐三套资源并运行测试**

运行：`dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~ProvidersViewContractTests|FullyQualifiedName~Localization"`

预期：PASS，且硬编码文案测试无新增失败。

- [ ] **步骤 7：提交 UI**

提交：`git add LoomX/Views/ProvidersView.axaml LoomX/Views/ProvidersView.axaml.cs LoomX/Resources/Strings*.resx LoomX.Tests/Views/ProvidersViewContractTests.cs && git commit -m "重构提供商面板并新增测试页"`。

---

### 任务 7：定向回归、完整构建与 OpenSpec 同步

**文件：**
- 修改：`openspec/changes/enhance-provider-editor-testing/tasks.md`
- 仅在发现小范围规格遗漏时修改：对应 delta spec 和 `design.md`。

- [ ] **步骤 1：运行定向测试**

运行：

```powershell
dotnet test LoomX.Tests/LoomX.Tests.csproj --filter "FullyQualifiedName~ProviderCompatibilityOptionTests|FullyQualifiedName~ProviderEditorViewModelTests|FullyQualifiedName~ProviderTestServiceTests|FullyQualifiedName~ProviderTestPanelViewModelTests|FullyQualifiedName~ProvidersViewContractTests|FullyQualifiedName~ProviderHealthServiceTests"
```

预期：PASS。

- [ ] **步骤 2：运行完整测试和 Release 构建**

运行：

```powershell
dotnet test LoomX.sln -c Release
dotnet build LoomX.sln -c Release --no-restore
```

预期：退出码 0；不以终端中文显示异常推断源文件编码损坏。

- [ ] **步骤 3：运行 OpenSpec 严格验证**

运行：`openspec validate enhance-provider-editor-testing --strict`

预期：`Change 'enhance-provider-editor-testing' is valid`。

- [ ] **步骤 4：勾选已验证任务并提交**

只有对应测试证据存在时才把 `tasks.md` 的项目改为 `[x]`。提交：`git add openspec/changes/enhance-provider-editor-testing/tasks.md && git commit -m "记录提供商测试器实施验证"`。

---

### 任务 8：CUA 验证与发布包

**文件：**
- 新建：`outputs/<YYYY-MM-DD_HH-mm-ss>/` 发布目录。
- 按项目既有方式生成发布文件，不删除其他 Session 的 outputs。

- [ ] **步骤 1：发布桌面应用**

先从项目现有发布脚本或 csproj RuntimeIdentifier 确认正式命令；若无专用脚本，运行：

```powershell
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
dotnet publish LoomX/LoomX.csproj -c Release -r win-x64 --self-contained false -o "outputs/$stamp"
```

预期：发布目录包含可执行文件和依赖。

- [ ] **步骤 2：隐藏启动并校验进程路径**

使用 `Start-Process -FilePath <绝对exe路径> -WindowStyle Hidden -PassThru`，再读取进程 `Path`，确认路径位于本次 `outputs/<stamp>`。

- [ ] **步骤 3：使用 CUA 仅截取应用验证**

后台验证：四个 Tab；三种兼容卡片；ID 不可见；API Key 在基础页；高级页无旧测试连接；选择模型；默认“每日一言”；普通/流式发送；停止；401/429 或可控错误展示；重试、复制、清空；透明主题下不根据截图颜色武断判定配色。

- [ ] **步骤 4：记录验证结果并提交交付元数据**

如验证发现实现问题，先按 systematic-debugging 找根因并补失败测试；修复后重新执行任务 7 和本任务。不得提交二进制发布目录，除非仓库现有规则明确跟踪 outputs。

- [ ] **步骤 5：推送实现分支**

确认 `git status` 仅保留用户/其他 Session 的既有无关改动，随后 `git push`。不要 reset、stash、clean 或删除无关未跟踪项。
