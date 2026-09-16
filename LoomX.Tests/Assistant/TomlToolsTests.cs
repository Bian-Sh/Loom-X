using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Assistant.Configuration;
using LoomX.Tests.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class TomlToolsTests
{
    [Fact]
    public async Task Read_隐藏敏感顶层键并传递取消令牌()
    {
        var service = new RecordingTomlDocumentService
        {
            ReadHandler = (_, _) => Task.FromResult(new TomlReadResult(
                true,
                true,
                ["model", "api_key", "token", "plain-secret"],
                [])),
        };
        var tool = GetTool("toml.read", service);
        using var cancellation = new CancellationTokenSource();
        const string fullPath = @"C:\Users\Example User\config.toml";

        var result = await tool.Handler(new JsonObject { ["path"] = fullPath }, cancellation.Token);

        Assert.True(result.Success);
        Assert.Equal(fullPath, service.LastPath);
        Assert.Equal(cancellation.Token, service.LastCancellationToken);
        Assert.Contains("top_level_keys", result.Content);
        Assert.Contains("model", result.Content);
        Assert.DoesNotContain("api_key", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("token", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-secret", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(fullPath, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_敏感路径再次脱敏()
    {
        var service = new RecordingTomlDocumentService
        {
            GetHandler = (_, _, _) => Task.FromResult(new TomlValueResult(
                true,
                TomlValueKind.String,
                TomlValue.FromObject("plain-secret"),
                [])),
        };
        var tool = GetTool("toml.get", service);

        var result = await tool.Handler(new JsonObject
        {
            ["path"] = "config.toml",
            ["key_path"] = new JsonArray("api_key"),
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("***", result.Content);
        Assert.Contains("string", result.Content);
        Assert.DoesNotContain("plain-secret", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_读取父对象时隐藏敏感属性名和值()
    {
        const string secret = "parent-object-secret-sentinel";
        var service = new RecordingTomlDocumentService
        {
            GetHandler = (_, _, _) => Task.FromResult(new TomlValueResult(
                true,
                TomlValueKind.Object,
                TomlValue.FromObject(new Dictionary<string, object?>
                {
                    ["model"] = "loomx",
                    ["api_key"] = secret,
                    ["nested"] = new Dictionary<string, object?>
                    {
                        ["token"] = secret,
                        ["enabled"] = true,
                    },
                }),
                [])),
        };
        var tool = GetTool("toml.get", service);

        var result = await tool.Handler(new JsonObject
        {
            ["path"] = "config.toml",
            ["key_path"] = new JsonArray("provider"),
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("model", result.Content);
        Assert.Contains("nested", result.Content);
        Assert.Contains("enabled", result.Content);
        Assert.DoesNotContain("api_key", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("token", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Set_转换结构化值且不回显输入或备份路径()
    {
        var service = new RecordingTomlDocumentService
        {
            PatchHandler = (_, _, _) => Task.FromResult(new TomlWriteResult(
                true,
                true,
                @"C:\Users\Example User\config.toml.backup",
                false,
                [])),
        };
        var tool = GetTool("toml.set", service);

        var result = await tool.Handler(new JsonObject
        {
            ["path"] = "config.toml",
            ["key_path"] = new JsonArray("api_key"),
            ["value"] = "plain-secret",
        }, CancellationToken.None);

        Assert.True(result.Success);
        var operation = Assert.Single(service.LastOperations!);
        Assert.Equal(TomlPatchKind.Set, operation.Kind);
        Assert.Equal(["api_key"], operation.Path.Segments);
        Assert.Equal("plain-secret", operation.Value!.Value);
        Assert.Contains("backup_created", result.Content);
        Assert.DoesNotContain("plain-secret", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Example User", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_解析Set和Delete操作且不回显输入值()
    {
        var service = new RecordingTomlDocumentService();
        var tool = GetTool("toml.patch", service);
        var operations = new JsonArray
        {
            new JsonObject
            {
                ["op"] = "set",
                ["key_path"] = new JsonArray("provider", "headers"),
                ["value"] = new JsonObject { ["Authorization"] = "plain-secret" },
            },
            new JsonObject
            {
                ["op"] = "delete",
                ["key_path"] = new JsonArray("legacy"),
            },
        };

        var result = await tool.Handler(new JsonObject
        {
            ["path"] = "config.toml",
            ["operations"] = operations,
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Collection(
            service.LastOperations!,
            operation => Assert.Equal(TomlPatchKind.Set, operation.Kind),
            operation => Assert.Equal(TomlPatchKind.Delete, operation.Kind));
        Assert.DoesNotContain("plain-secret", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_生成Delete操作()
    {
        var service = new RecordingTomlDocumentService();
        var tool = GetTool("toml.delete", service);

        var result = await tool.Handler(new JsonObject
        {
            ["path"] = "config.toml",
            ["key_path"] = new JsonArray("legacy", "enabled"),
        }, CancellationToken.None);

        Assert.True(result.Success);
        var operation = Assert.Single(service.LastOperations!);
        Assert.Equal(TomlPatchKind.Delete, operation.Kind);
        Assert.Null(operation.Value);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task 运行时边界校验_拒绝无效参数(string toolName, JsonNode arguments)
    {
        var service = new RecordingTomlDocumentService();
        var tool = GetTool(toolName, service);

        var result = await tool.Handler(arguments, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("invalid_arguments", result.Content);
        Assert.Contains("工具参数无效", result.Content);
        Assert.Equal(0, service.TotalCalls);
        Assert.DoesNotContain("plain-secret", result.Content, StringComparison.Ordinal);
    }

    public static TheoryData<string, JsonNode> InvalidArguments => new()
    {
        {
            "toml.read",
            new JsonObject { ["path"] = new string('p', 4097) }
        },
        {
            "toml.get",
            new JsonObject
            {
                ["path"] = "config.toml",
                ["key_path"] = new JsonArray(Enumerable.Range(0, 33).Select(index => JsonValue.Create($"key-{index}")).ToArray()),
            }
        },
        {
            "toml.set",
            new JsonObject
            {
                ["path"] = "config.toml",
                ["key_path"] = new JsonArray("model"),
                ["value"] = new string('s', 16385) + "plain-secret",
            }
        },
        {
            "toml.patch",
            new JsonObject
            {
                ["path"] = "config.toml",
                ["operations"] = new JsonArray(),
            }
        },
        {
            "toml.patch",
            new JsonObject
            {
                ["path"] = "config.toml",
                ["operations"] = new JsonArray(Enumerable.Range(0, 65).Select(index => (JsonNode?)new JsonObject
                {
                    ["op"] = "delete",
                    ["key_path"] = new JsonArray($"key-{index}"),
                }).ToArray()),
            }
        },
        {
            "toml.patch",
            new JsonObject
            {
                ["path"] = "config.toml",
                ["operations"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["op"] = "replace",
                        ["key_path"] = new JsonArray("model"),
                    },
                },
            }
        },
    };

    [Fact]
    public async Task 服务异常_返回固定错误且不泄漏异常内容或路径()
    {
        const string secret = "api_key = \"plain-secret\"";
        const string fullPath = @"C:\Users\Example User\config.toml";
        var service = new RecordingTomlDocumentService
        {
            ReadHandler = (_, _) => throw new InvalidOperationException($"{secret} at {fullPath}"),
        };
        var tool = GetTool("toml.read", service);

        var result = await tool.Handler(new JsonObject { ["path"] = fullPath }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("toml_operation_failed", result.Content);
        Assert.Contains("TOML 操作未完成", result.Content);
        Assert.DoesNotContain("plain-secret", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(fullPath, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 服务失败结果_不返回服务错误详情()
    {
        var service = new RecordingTomlDocumentService
        {
            ReadHandler = (_, _) => Task.FromResult(new TomlReadResult(
                true,
                false,
                [],
                [@"api_key = ""plain-secret"" 位于 C:\Users\Example User\config.toml"])),
        };
        var tool = GetTool("toml.read", service);

        var result = await tool.Handler(new JsonObject { ["path"] = "config.toml" }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("toml_read_failed", result.Content);
        Assert.DoesNotContain("plain-secret", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Example User", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 取消令牌_传递到服务并保持取消语义()
    {
        var service = new RecordingTomlDocumentService
        {
            ReadHandler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new TomlReadResult(true, true, [], []);
            },
        };
        var tool = GetTool("toml.read", service);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tool.Handler(new JsonObject { ["path"] = "config.toml" }, cancellation.Token));

        Assert.Equal(cancellation.Token, service.LastCancellationToken);
    }

    [Theory]
    [InlineData("toml.set", "{\"path\":\"config.toml\",\"key_path\":[\"model\"],\"value\":\"x\"}")]
    [InlineData("toml.delete", "{\"path\":\"config.toml\",\"key_path\":[\"model\"]}")]
    public async Task 写入和删除工具_拒绝审批时不会调用服务(string toolName, string argumentsJson)
    {
        var service = new RecordingTomlDocumentService();
        var registry = TomlToolsTestSupport.CreateRegistry(service);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", toolName, argumentsJson)), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已取消。"), new ModelCompletedEvent("stop")]);
        var loop = new AgentLoop(
            model,
            registry,
            NullLogger<AgentLoop>.Instance,
            approvalGate: (_, _, _) => Task.FromResult(false));

        var events = await CollectAsync(loop.RunAsync(new AgentSession(), "修改配置"));

        Assert.Contains(events, item => item.Kind == AgentEventKind.ToolApprovalRequested && item.ToolName == toolName);
        Assert.Equal(0, service.TotalCalls);
    }

    [Fact]
    public async Task Set_工具结果和Agent日志不包含输入Secret()
    {
        const string secret = "plain-secret";
        var service = new RecordingTomlDocumentService();
        var registry = TomlToolsTestSupport.CreateRegistry(service);
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall(
                "call_1",
                "toml.set",
                "{\"path\":\"config.toml\",\"key_path\":[\"api_key\"],\"value\":\"plain-secret\"}")), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已完成。"), new ModelCompletedEvent("stop")]);
        var logger = new RecordingLogger<AgentLoop>();
        var loop = new AgentLoop(
            model,
            registry,
            logger,
            approvalGate: (_, _, _) => Task.FromResult(true));
        var session = new AgentSession();

        await CollectAsync(loop.RunAsync(session, "修改配置"));

        var toolResult = Assert.Single(session.Messages, message => message.Role == ChatRole.Tool);
        Assert.DoesNotContain(secret, toolResult.Content!, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join("\n", logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patch_AgentLoop结果和日志不包含敏感输入服务错误或异常文本()
    {
        const string patchSecret = "patch-json-secret-sentinel";
        const string userSecret = "user-text-secret-sentinel";
        const string serviceErrorSecret = "service-error-secret-sentinel";
        const string exceptionSecret = "exception-text-secret-sentinel";
        var logger = new RecordingLogger<AgentLoop>();

        var failedService = new RecordingTomlDocumentService
        {
            PatchHandler = (_, _, _) => Task.FromResult(new TomlWriteResult(
                false,
                false,
                null,
                false,
                [serviceErrorSecret])),
        };
        var failedSession = await RunPatchThroughAgentLoopAsync(failedService, logger, patchSecret, userSecret);

        var throwingService = new RecordingTomlDocumentService
        {
            PatchHandler = (_, _, _) => throw new InvalidOperationException(exceptionSecret),
        };
        var throwingSession = await RunPatchThroughAgentLoopAsync(throwingService, logger, patchSecret, userSecret);

        var toolResults = failedSession.Messages
            .Concat(throwingSession.Messages)
            .Where(message => message.Role == ChatRole.Tool)
            .Select(message => message.Content ?? string.Empty)
            .ToArray();
        Assert.Equal(2, toolResults.Length);

        var logged = string.Join("\n", logger.Messages);
        foreach (var sentinel in new[] { patchSecret, userSecret, serviceErrorSecret, exceptionSecret })
        {
            Assert.All(toolResults, content => Assert.DoesNotContain(sentinel, content, StringComparison.Ordinal));
            Assert.DoesNotContain(sentinel, logged, StringComparison.Ordinal);
        }
    }

    private static ToolDefinition GetTool(string name, ITomlDocumentService service)
    {
        var registry = TomlToolsTestSupport.CreateRegistry(service);
        Assert.True(registry.TryGet(name, out var tool));
        return Assert.IsType<ToolDefinition>(tool);
    }

    private static async Task<List<AgentEvent>> CollectAsync(IAsyncEnumerable<AgentEvent> source)
    {
        var events = new List<AgentEvent>();
        await foreach (var item in source)
        {
            events.Add(item);
        }

        return events;
    }

    private static async Task<AgentSession> RunPatchThroughAgentLoopAsync(
        ITomlDocumentService service,
        RecordingLogger<AgentLoop> logger,
        string patchSecret,
        string userSecret)
    {
        var arguments = new JsonObject
        {
            ["path"] = "config.toml",
            ["operations"] = new JsonArray
            {
                new JsonObject
                {
                    ["op"] = "set",
                    ["key_path"] = new JsonArray("provider", "headers"),
                    ["value"] = new JsonObject { ["Authorization"] = patchSecret },
                },
            },
        }.ToJsonString();
        var model = new ScriptedModelClient(
            [new ModelToolCallEvent(new ToolCall("call_1", "toml.patch", arguments)), new ModelCompletedEvent("tool_calls")],
            [new TextDeltaEvent("已完成。"), new ModelCompletedEvent("stop")]);
        var loop = new AgentLoop(
            model,
            TomlToolsTestSupport.CreateRegistry(service),
            logger,
            approvalGate: (_, _, _) => Task.FromResult(true));
        var session = new AgentSession();

        await CollectAsync(loop.RunAsync(session, userSecret));

        return session;
    }
}

internal static class TomlToolsTestSupport
{
    public static ToolRegistry CreateRegistry(ITomlDocumentService service)
    {
        var registry = new ToolRegistry();
        TomlTools.RegisterAll(registry, service);
        return registry;
    }
}

internal sealed class RecordingTomlDocumentService : ITomlDocumentService
{
    public Func<string, CancellationToken, Task<TomlReadResult>> ReadHandler { get; init; } =
        (_, _) => Task.FromResult(new TomlReadResult(true, true, [], []));

    public Func<string, TomlPath, CancellationToken, Task<TomlValueResult>> GetHandler { get; init; } =
        (_, _, _) => Task.FromResult(new TomlValueResult(false, null, null, []));

    public Func<string, CancellationToken, Task<TomlValidationResult>> ValidateHandler { get; init; } =
        (_, _) => Task.FromResult(new TomlValidationResult(true, null, null, []));

    public Func<string, IReadOnlyList<TomlPatchOperation>, CancellationToken, Task<TomlWriteResult>> PatchHandler { get; init; } =
        (_, _, _) => Task.FromResult(new TomlWriteResult(true, true, null, false, []));

    public int TotalCalls { get; private set; }

    public string? LastPath { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public IReadOnlyList<TomlPatchOperation>? LastOperations { get; private set; }

    public Task<TomlReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        Record(path, cancellationToken);
        return ReadHandler(path, cancellationToken);
    }

    public Task<TomlValueResult> GetAsync(
        string path,
        TomlPath keyPath,
        CancellationToken cancellationToken = default)
    {
        Record(path, cancellationToken);
        return GetHandler(path, keyPath, cancellationToken);
    }

    public Task<TomlValidationResult> ValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        Record(path, cancellationToken);
        return ValidateHandler(path, cancellationToken);
    }

    public Task<TomlWriteResult> PatchAsync(
        string path,
        IReadOnlyList<TomlPatchOperation> operations,
        CancellationToken cancellationToken = default)
    {
        Record(path, cancellationToken);
        LastOperations = operations;
        return PatchHandler(path, operations, cancellationToken);
    }

    private void Record(string path, CancellationToken cancellationToken)
    {
        TotalCalls++;
        LastPath = path;
        LastCancellationToken = cancellationToken;
    }
}
