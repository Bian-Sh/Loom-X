---
name: development-v1
description: 开发或修复 LoomX 可信 C# 源码插件时使用；覆盖公共 SDK 与开发工具，不用于修改宿主内部实现或处理真实凭据。
metadata:
  version: "1.0.0"
  plugin-api-major: "1"
  manifest-schema: runtime-v1
---

# LoomX 插件开发 v1

适用 **Plugin API 主版本 1**（`LoomX.Plugin.Abstractions`）、当前 `runtime` 联合结构（本 Skill 称 `runtime-v1`）、`storage.sqlite/v1`、`logging/v1`、`notifications/v1`。这些版本是 Skill 元数据，**不是新增的插件 Manifest 字段**。每次工具调用传 `plugin_api_major: 1`；出现 `LX_PLUGIN_API_INCOMPATIBLE` 就停止，获取匹配宿主的 Skill，不猜测版本或降级能力。

只依赖 `LoomX.Plugin.Abstractions` 与框架基础库。插件不得引用 `LoomX`、PluginHost、Avalonia、SQLite 提供程序、宿主 ViewModel 或私有实现；不得向宿主要 `IServiceProvider`、`ILoggerFactory`、`HttpClient`、数据库连接/事务或窗口。无需本机 SDK、`.csproj`、NuGet restore、源码 glob 或编译器参数。

## 权限与反馈边界

源码是**可信进程内 C#**，拥有当前用户/宿主进程权限。ALC、Bridge、临时数据根都不是 OS 沙箱；直接 `System.IO` 或网络代码仍可绕过 Bridge。只对用户明确批准的代码执行 test/reload。未授权时使用已有 `assistant.ask_user` 解释影响并取得决定，再传 `trust_code: true`；拒绝或取消就停止，不把 AI 自己设置的参数当用户授权。

日志、Toast、UI 命令结果、测试失败和工具结果只能放短安全摘要。禁止提交 API Key、Authorization/Header 值、请求/响应正文、prompt、图片、SQL 参数、工具参数或异常原值。日志使用 `PluginLogEntry` 的结构化字段（计数、耗时、状态），不要逐 token/chunk 写入。Logging v1 模板仅支持 `{Name}` 占位与 `{{`/`}}` 字面花括号，拒绝 format/alignment（如 `{Count:N2}` 或 `{Count,8}`）；占位必须对应合法安全字段。日志在交给 Serilog 前按字段值预展开，与通知摘要均采用包含宿主来源前缀的 512 字符总预算，不是插件正文可独占 512；日志的 512 是预展开预算，不保证 Serilog quoting/escaping 后的最终输出长度仍在 512 以内。`notifications/v1` 使用 `PluginNotification`，只能指定 Success/Info/Warning/Error 与摘要，不能控制窗口/导航。宿主过滤是纵深防御，不保证发现所有秘密。

## 工具与可执行修复循环

六个稳定工具均返回 JSON：`success`、`operation`、`plugin_id`、`diagnostics`，诊断含 `plugin_id/stage/severity/code/message/relative_path/line/column`。成功验证还报告 Plugin API、Manifest 结构及能力版本；编译返回缓存身份、命中状态、产物状态，不回传绝对缓存路径。工具不是 Shell/CLI 命令。

1. `loomx.plugin.create`：传 `plugin_id`（例如 `example.echo`）、可选 `entry_type`（例如 `Example.Plugin`）。只在主会话指定开发根的合法 id 子目录创建，不覆盖，不安装。已有目录时先读文件而不是重新创建。
2. 阅读该包的 `plugin.manifest.json`、显式 `runtime.sources`、四语言 `i18n` 与测试文件，再作最小修改。**sources 变化必须修改磁盘上的 Manifest**，不能只修改内存快照。
3. `loomx.plugin.validate`：不编译、不执行代码，复用宿主检查清单、能力版本、所有可安全报告的缺失源码/路径、本地化和禁止依赖。一次修复独立问题，再验证。
4. `loomx.plugin.compile`：写缓存，但不执行插件；无需 SDK。读取稳定错误码与相对行列，修改对应声明源码，再 validate/compile。消息已去除源码字面量，不尝试恢复秘密。
5. `loomx.plugin.diagnostics`：读取最近一次安全结果；首次调用仅验证，不隐式编译。修改文件后调用 validate/compile 才刷新诊断，缓存诊断不代表当前文件已验证。
6. 用户确认可信执行后，`loomx.plugin.test` 传 `trust_code: true`，可带 `cases`：`pipeline/payload/expected/outcome`（passed/modified/blocked）。只用合成非敏感数据。临时 Runtime/Bridge 检查入口、Extension、UI、自测试和用例；没有真实 Provider、生产 Vault 或核心配置服务，不安装、不 reload。`tests_passed` 只计实际执行用例与自测试，不代表任意恶意 C# 已被隔离。
7. 安装是独立用户操作，本工具不自动安装。对**已安装**插件且用户授权生产影响后调用 `loomx.plugin.reload`，传 `trust_code: true`。它只走注入 Runtime 的安装路径和原子候选替换；失败保留当前实例与在途租约，不回滚覆盖插件数据/Vault。

编译诊断的 `relative_path/line/column` 是修复锚点；不要回显正文来解释错误。修复到 validate、compile、test 成功为止；版本、可信授权、路径边界或 fail closed 失败时停止，不绕过检查。不要将开发成功等同于已生产启用。

## Manifest 与目录

包布局固定为 `plugins/<id>/plugin.manifest.json`、`i18n/{zh-CN,zh-TW,ja-JP,en-US}.json`。开发根也以每 id 一个目录存放。下面是可复制执行的完整示例，随后两个 C# 块分别保存为指定文件；四语言字典至少提供 `card_title` 文本。

<!-- example-manifest -->
```json
{
  "id": "example.echo",
  "version": "0.1.0",
  "runtime": {
    "kind": "csharp-source",
    "entry_type": "Example.Plugin",
    "sources": ["src/Plugin.cs", "tests/PluginTests.cs"]
  },
  "host_capabilities": ["storage.sqlite/v1", "logging/v1", "notifications/v1"],
  "capabilities": ["example.echo"],
  "extensions": [{
    "id": "echo", "kind": "request", "pipeline": "development.request",
    "failure_policy": "fail-closed", "capabilities": ["example.echo"]
  }],
  "ui": {"contributions": [{"id": "card", "slot": "card-body"}]}
}
```

`runtime.kind` 仅 `csharp-source` 或 `assembly`。source 需要 `entry_type` 与非空、无重复、显式 `.cs` 路径；拒绝绝对路径、穿越与 reparse 逃逸，未列源码不编译。assembly 需要 `assembly/entry_type`。旧顶层 `assembly/plugin_type` 仅在 runtime 缺失时可用；不能两种混用。id、版本、capabilities、extensions 遵循宿主清单；source 可声明空 capabilities/extensions，模板就是合法空扩展。`host_capabilities` 是向宿主申请的精确版本能力，`capabilities` 是插件自身行为，不要混用。未知能力版本执行前拒绝。

不要夹带 `Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*` 或 `e_sqlite3`；不要平铺 manifest/i18n，也不要把插件文本放入宿主资源。`PluginTextCatalog.For(typeof(Plugin), PluginId, culture)` 的插件 id 必须正确；编译器会把 en-US 字典嵌入。插件负载命名不能靠改名绕过冲突。

## 完整 Pipeline 与声明式 UI 示例

<!-- example-source -->
```csharp
// 文件：src/Plugin.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LoomX.Plugins;
namespace Example;

public sealed class Plugin : ILoomXPlugin, IPluginUiContributionProvider
{
    public const string PluginId = "example.echo";
    public string Id => PluginId;
    private IPluginBridge bridge = PluginBridgeDefaults.Unavailable;
    public event EventHandler? UiInvalidated;
    public void Initialize(PluginInitializationContext context) => bridge = context.Bridge;
    public IEnumerable<IPipelineExtension> CreateExtensions() => new IPipelineExtension[] { new Echo(bridge) };
    public IReadOnlyList<PluginUiContribution> GetUiContributions(PluginUiContext context)
    {
        var texts = PluginTextCatalog.For(typeof(Plugin), PluginId, context.CultureName);
        return new PluginUiContribution[] {
            new(2, "card", PluginUiSlot.CardBody, new PluginUiTextNode(texts.Get("card_title")))
        };
    }
}

public sealed class Echo(IPluginBridge bridge) : IRequestExtension
{
    public string ExtensionId => "echo";
    public ExtensionKind Kind => ExtensionKind.Request;
    public ExtensionFailurePolicy FailurePolicy => ExtensionFailurePolicy.FailClosed;
    public IReadOnlyList<string> Capabilities => new[] { "example.echo" };
    public async ValueTask<PipelineResult> ProcessRequestAsync(PipelineContext context, string payload, CancellationToken ct)
    {
        await bridge.Log.WriteAsync(new(PluginLogLevel.Information, "扩展执行完成"), ct);
        return PipelineResult.Modify(payload + "-已处理");
    }
}
```

Pipeline 的上一 Entry 输出传给下一 Entry，注册 Extension id/kind/failure_policy 必须与清单对应；顺序由宿主编排，不加入 before/after/requires 依赖。Request 是完整 Provider 请求构造后、出站前，Response 使用 `IResponseExtension.ProcessResponseAsync` 在上游返回后执行。Pass 原样，Modify 改写，Block 返回空正文；安全类必须 FailClosed，普通 ContinueOnError 失败可继续。示例故意是开发测试 Pipeline，不修改真实 Provider 请求。

UI 仅用 `PluginUiContribution/PluginUiNode`，不可生成 Avalonia Control/XAML 或获取 ViewModel。支持 CardBody/DetailBody、文本/图标/堆叠/网格/表面；当前 SchemaVersion 2，展示 v1 仍兼容，交互必须 v2。交互贡献清单设 `interactive: true`，实现 `IPluginUiCommandHandler.ExecuteUiCommandAsync`，校验命令参数，返回安全 `PluginUiCommandResult`；状态变化触发 `UiInvalidated`，由宿主重新索取声明式快照。运行时校验失败的 UI 不应被当作测试成功。

## Bridge 与插件自带测试示例

`Initialize` 保存 `context.Bridge`，`context.DataDirectory` 是本插件数据目录，不是核心配置目录。只有声明能力才可调用；未声明调用返回 `CapabilityNotDeclared`，未知版本不得降级。普通禁用和资源卸载有不同生命周期，尤其不得破坏历史凭据 placeholder 的持续恢复。

开发工具自测试约定：与入口同 namespace 的公开静态 `PluginTests`，方法 **`ValueTask RunAsync(PluginInitializationContext, CancellationToken)`**。文件必须显式列入 sources。这是开发工具文件约定，不是新增 Manifest 字段或必须实现的插件 SDK 接口。方法抛异常即失败，工具只返回稳定安全诊断；只用临时 Bridge 测试业务，不能测试真实用户数据。没有该类型时自测试计数为零，不虚报执行。

<!-- example-tests -->
```csharp
// 文件：tests/PluginTests.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using LoomX.Plugins;
namespace Example;

public static class PluginTests
{
    public static async ValueTask RunAsync(PluginInitializationContext context, CancellationToken ct)
    {
        await using var db = await context.Bridge.Storage.OpenSqliteAsync("tests/example.db", ct);
        await db.ExecuteAsync(new("CREATE TABLE IF NOT EXISTS Sample (Value TEXT NOT NULL)"), ct);
        await using (var transaction = await db.BeginTransactionAsync(ct))
        {
            await transaction.ExecuteAsync(new("INSERT INTO Sample(Value) VALUES ($value)",
                new[] { new PluginSqlParameter("$value", PluginSqlValue.FromText("合成测试数据")) }), ct);
            await transaction.CommitAsync(ct);
        }
        var row = await db.QuerySingleAsync(new("SELECT Value FROM Sample LIMIT 1"), ct);
        if (row?["Value"].Text != "合成测试数据") throw new InvalidOperationException("数据库断言失败。");
        await context.Bridge.Notifications.ShowAsync(new(PluginNotificationLevel.Success, "插件自测试完成"), ct);
    }
}
```

SQLite 路径相对本插件数据根，文件名/表结构由插件自行决定，不能访问生产配置/Vault或其他插件。每命令一语句、参数化 SQL；禁止拼接用户数据、多语句批处理、ATTACH、VACUUM INTO、扩展加载。事务句柄提供同形 Execute/Query、Commit/Rollback，未提交释放回滚，完成后不能复用，活动事务不混入非事务命令，不能用裸 BEGIN/COMMIT/SAVEPOINT 代替句柄。保留既有长期数据，迁移需独立授权，不自动建第二份空 Vault。

SQL 值是 Null/Integer/Real/Text/Blob，QuerySingle 返回第一行或 null，Query 返回受限缓冲行集，重复列名用索引。`PluginSqlException.Error` 稳定分类：ConstraintViolation、Busy、Locked、Corrupt、ReadOnly、PermissionDenied、InvalidPath、InvalidSql、Cancelled、ResourceLimit、Unknown；Busy/Locked 可按有限策略重试，其他失败不可伪装成功。取消传入各操作，显式释放数据库/事务；Runtime 最终兜底回收实例资源。

按 create → 写入上面 Manifest/两份源码/四语言字典 → validate → compile → 用户确认 → test 执行，测试参数可以带：`cases: [{pipeline: "development.request", payload: "合成输入", expected: "合成输入-已处理", outcome: "modified"}]`。结果必须成功，报告实际自测试与 Pipeline 计数；这完成开发验收，**不代表已安装或 reload 生产实例**。
