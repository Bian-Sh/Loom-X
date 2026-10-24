using System.Text.Json.Nodes;
using LoomX.Assistant;
using LoomX.Assistant.ClientConfig;
using LoomX.Assistant.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Assistant.ClientConfig;

/// <summary>
/// 各客户端适配器的黄金用例（场景移植自 ai-switch route_config adapters 的测试）。
/// 全部写入 fake home root（Temp 下随机目录），绝不触碰真实 ~/.codex、~/.zcode 等。
/// </summary>
public sealed class ClientConfigAdapterTests : IDisposable
{
    private const string BaseUrl = "http://127.0.0.1:43111";
    private const string ApiKey = "sk-loomx-test-key";

    private readonly string fakeHome = Path.Combine(Path.GetTempPath(), $"codextest-home-{Guid.NewGuid():N}");
    private readonly CodexConfigAdapter codex;

    public ClientConfigAdapterTests()
    {
        codex = new CodexConfigAdapter(new TomlDocumentService(NullLogger<TomlDocumentService>.Instance));
    }

    public void Dispose()
    {
        if (Directory.Exists(fakeHome))
        {
            Directory.Delete(fakeHome, recursive: true);
        }
    }

    private static ClientWriteInput Input(params string[] models) => WriteInput(models);

    private static ClientWriteInput WriteInput(string[]? models = null, string baseUrl = BaseUrl, string apiKey = ApiKey) => new(
        baseUrl,
        apiKey,
        (models is { Length: > 0 } ? models : ["gpt-5.6-sol"]).Select((id, index) => new ClientModelInfo(
            id,
            ContextWindow: 200_000,
            MaxOutputTokens: 128_000,
            ReasoningLevels: index == 0 ? ["minimal", "low", "medium", "high"] : [],
            SupportsImageInput: true)).ToArray());

    private string WriteFresh(IClientConfigAdapter adapter, ClientWriteInput? input = null)
    {
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var result = adapter.WriteAsync(path, null, input ?? Input(), CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(result.RenderedContent);
        File.WriteAllText(path.FullName, result.RenderedContent!, ClientConfigFiles.Utf8NoBom);
        return path.FullName;
    }

    /// <summary>写入并落盘渲染结果，返回文件内容（供后续断言读取磁盘上的最终状态）。</summary>
    private string WriteExisting(IClientConfigAdapter adapter, string existing, ClientWriteInput? input = null, string? subDir = null)
    {
        var path = adapter.ResolvePath(subDir is null ? fakeHome : Path.Combine(fakeHome, subDir));
        Directory.CreateDirectory(path.Directory!.FullName);
        if (existing.Length > 0)
        {
            File.WriteAllText(path.FullName, existing, ClientConfigFiles.Utf8NoBom);
        }

        var result = adapter.WriteAsync(path, existing.Length > 0 ? existing : null, input ?? Input(), CancellationToken.None).GetAwaiter().GetResult();
        Assert.NotNull(result.RenderedContent);
        File.WriteAllText(path.FullName, result.RenderedContent!, ClientConfigFiles.Utf8NoBom);
        return result.RenderedContent!;
    }

    private static JsonNode ParseJson(string path) => JsonNode.Parse(File.ReadAllText(path))!;

    /// <summary>Tomlyn patch 引擎生成带引号的键（合法 TOML）；断言按归一化匹配。</summary>
    private static string TomlNorm(string text) =>
        text.Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);

    // ---------- codex（TOML，经 TomlDocumentService） ----------

    [Fact]
    public async Task Codex_全新写入_指向Loomx并生成Catalog()
    {
        var path = codex.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var outcome = await codex.WriteAsync(path, null, Input("gpt-5.6-sol", "glm-5.3"), CancellationToken.None);

        Assert.True(outcome.Changed);
        Assert.Null(outcome.BackupPath);
        Assert.NotNull(outcome.CatalogPath);

        var text = TomlNorm(await File.ReadAllTextAsync(path.FullName));
        Assert.Contains(TomlNorm("model_provider = \"loomx\""), text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("model_catalog_json = \"loomx-model-catalog.json\""), text, StringComparison.Ordinal);
        Assert.Contains("[model_providers.loomx]", text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("base_url = \"http://127.0.0.1:43111/v1\""), text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("experimental_bearer_token = \"sk-loomx-test-key\""), text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("wire_api = \"responses\""), text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("model = \"gpt-5.6-sol\""), text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("model_reasoning_effort = \"high\""), text, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-loomx-test-key", outcome.CatalogPath!, StringComparison.Ordinal);

        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(outcome.CatalogPath!))!;
        var models = (JsonArray)catalog["models"]!;
        Assert.Equal(2, models.Count);
        Assert.Equal("gpt-5.6-sol", models[0]!["slug"]!.GetValue<string>());
        Assert.Equal(1, models[0]!["priority"]!.GetValue<int>());
        Assert.Equal("high", models[0]!["default_reasoning_level"]!.GetValue<string>());
        Assert.Equal("glm-5.3", models[1]!["slug"]!.GetValue<string>());
        // 未声明档位的模型回落 LoomX Endpoint 档位表（minimal/low/medium/high），默认最高档。
        Assert.Equal("high", models[1]!["default_reasoning_level"]!.GetValue<string>());
        Assert.Equal(["text", "image"], models[0]!["input_modalities"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
    }

    [Fact]
    public async Task Codex_保留用户手写的非托管配置()
    {
        var path = codex.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        await File.WriteAllTextAsync(path.FullName, """
            approval_policy = "never"

            [model_providers.keep]
            name = "Keep"
            base_url = "https://keep.example/v1"
            wire_api = "chat"
            api_key_env_var = "KEEP_KEY"

            [mcp_servers.filesystem]
            command = "npx"
            """);

        var outcome = await codex.WriteAsync(path, await File.ReadAllTextAsync(path.FullName), Input(), CancellationToken.None);
        Assert.NotNull(outcome.BackupPath);
        var text = TomlNorm(await File.ReadAllTextAsync(path.FullName));

        Assert.Contains(TomlNorm("approval_policy = \"never\""), text, StringComparison.Ordinal);
        Assert.Contains("[model_providers.keep]", text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("api_key_env_var = \"KEEP_KEY\""), text, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.filesystem]", text, StringComparison.Ordinal);
        Assert.Contains(TomlNorm("model_provider = \"loomx\""), text, StringComparison.Ordinal);
        Assert.Contains("[model_providers.loomx]", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codex_替换历史api_key写法()
    {
        var path = codex.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var existing = """
            model_provider = "loomx"

            [model_providers.loomx]
            name = "LoomX"
            base_url = "http://127.0.0.1:43111/v1"
            wire_api = "responses"
            api_key = "legacy-key"
            """;
        await File.WriteAllTextAsync(path.FullName, existing);

        await codex.WriteAsync(path, existing, Input(), CancellationToken.None);
        var text = TomlNorm(await File.ReadAllTextAsync(path.FullName));

        Assert.Contains(TomlNorm("experimental_bearer_token = \"sk-loomx-test-key\""), text, StringComparison.Ordinal);
        Assert.DoesNotContain(TomlNorm("api_key = \"legacy-key\""), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codex_保留仍在目录中的既有model选择()
    {
        var path = codex.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        await File.WriteAllTextAsync(path.FullName, "model = \"glm-5.3\"\n");

        await codex.WriteAsync(path, await File.ReadAllTextAsync(path.FullName), Input("gpt-5.6-sol", "glm-5.3"), CancellationToken.None);
        var text = TomlNorm(await File.ReadAllTextAsync(path.FullName));

        Assert.Contains(TomlNorm("model = \"glm-5.3\""), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codex_检查_缺失_未托管_已托管_非法()
    {
        var path = codex.ResolvePath(fakeHome);
        Assert.Equal("missing", (await codex.InspectAsync(path, null, CancellationToken.None)).FileStatus);

        Assert.Equal("unmanaged", (await codex.InspectAsync(path, "approval_policy = \"never\"", CancellationToken.None)).FileStatus);

        var managedPath = codex.ResolvePath(Path.Combine(fakeHome, "managed"));
        Directory.CreateDirectory(managedPath.Directory!.FullName);
        await codex.WriteAsync(managedPath, null, Input(), CancellationToken.None);
        var managed = await codex.InspectAsync(managedPath, await File.ReadAllTextAsync(managedPath.FullName), CancellationToken.None);
        Assert.Equal("managed", managed.FileStatus);
        Assert.True(managed.Managed);

        Assert.Equal("invalid", (await codex.InspectAsync(path, "model_provider = [invalid", CancellationToken.None)).FileStatus);
    }

    // ---------- settings.json 型（claude-code / gemini-cli / grok） ----------

    [Fact]
    public async Task ClaudeCode_写入Env与托管标记并保留用户配置()
    {
        var adapter = JsonAgentConfigAdapter.ClaudeCode();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var existing = """{"permissions":{"allow":["Bash(ls)"]},"env":{"EXISTING_FLAG":"1"}}""";
        await File.WriteAllTextAsync(path.FullName, existing);

        var rendered = (await adapter.WriteAsync(path, existing, Input(), CancellationToken.None)).RenderedContent!;
        File.WriteAllText(path.FullName, rendered, ClientConfigFiles.Utf8NoBom);
        var json = ParseJson(path.FullName);

        Assert.Equal("Bash(ls)", json["permissions"]!["allow"]![0]!.GetValue<string>());
        Assert.Equal("1", json["env"]!["EXISTING_FLAG"]!.GetValue<string>());
        Assert.Equal(BaseUrl, json["env"]!["ANTHROPIC_BASE_URL"]!.GetValue<string>());
        Assert.Equal(ApiKey, json["env"]!["ANTHROPIC_AUTH_TOKEN"]!.GetValue<string>());
        Assert.Equal(BaseUrl, json["loomx"]!["gateway"]!["baseUrl"]!.GetValue<string>());
        Assert.Equal("claude", json["loomx"]!["gateway"]!["platform"]!.GetValue<string>());
        Assert.Equal(ApiKey, json["loomx"]!["gateway"]!["apiKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task GeminiGrok_绝不写Anthropic鉴权键()
    {
        foreach (var adapter in new[] { JsonAgentConfigAdapter.GeminiCli(), JsonAgentConfigAdapter.Grok() })
        {
            var path = adapter.ResolvePath(fakeHome);
            Directory.CreateDirectory(path.Directory!.FullName);
            var rendered = (await adapter.WriteAsync(path, null, Input(), CancellationToken.None)).RenderedContent!;
            var json = JsonNode.Parse(rendered)!;
            var env = json["env"]!;

            Assert.Null(env["ANTHROPIC_BASE_URL"]);
            Assert.Null(env["ANTHROPIC_AUTH_TOKEN"]);
            Assert.Equal(BaseUrl, env[adapter.Descriptor.TargetKey switch
            {
                "gemini-cli" => "GEMINI_API_BASE_URL",
                _ => "XAI_API_BASE_URL",
            }]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task JsonAgent_检查状态与拒绝非法文件()
    {
        var adapter = JsonAgentConfigAdapter.ClaudeCode();
        var path = adapter.ResolvePath(fakeHome);

        Assert.Equal("missing", adapter.Inspect(path, null).FileStatus);
        Assert.Equal("unmanaged", adapter.Inspect(path, """{"theme":"dark"}""").FileStatus);

        var rendered = (await adapter.WriteAsync(path, null, Input(), CancellationToken.None)).RenderedContent!;
        var managed = adapter.Inspect(path, rendered);
        Assert.Equal("managed", managed.FileStatus);
        Assert.True(managed.Managed);

        Assert.Equal("invalid", adapter.Inspect(path, "{invalid").FileStatus);
        await Assert.ThrowsAsync<ClientConfigException>(() => adapter.WriteAsync(path, "{invalid", Input(), CancellationToken.None));
        // 根节点非对象同样拒绝。
        Assert.Equal("invalid", adapter.Inspect(path, "[]").FileStatus);
        await Assert.ThrowsAsync<ClientConfigException>(() => adapter.WriteAsync(path, "[]", Input(), CancellationToken.None));
    }

    // ---------- zcode（版本化 provider store） ----------

    [Fact]
    public async Task ZCode_全新store带版本与完整条目()
    {
        var adapter = ZCodeConfigAdapter.Codex();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var rendered = (await adapter.WriteAsync(path, null, Input(["gpt-5.6-sol", "glm-5.3"]), CancellationToken.None)).RenderedContent!;
        var json = JsonNode.Parse(rendered)!;
        Assert.Equal(1, json["schemaVersion"]!.GetValue<int>());
        var entry = json["config"]!["providerConfigRules"]!["providerRules"]![0]!;
        Assert.Equal("loomx-codex", entry["providerId"]!.GetValue<string>());
        Assert.Equal("LoomX (Codex)", entry["providerName"]!.GetValue<string>());
        Assert.Equal("standard-personal", entry["config"]!["group"]!.GetValue<string>());
        Assert.Equal(ApiKey, entry["config"]!["access"]!["apiKey"]!.GetValue<string>());
        Assert.Equal("openai-responses", entry["config"]!["api"]!["type"]!.GetValue<string>());
        Assert.Equal($"{BaseUrl}/v1", entry["config"]!["api"]!["baseUrl"]!.GetValue<string>());
        Assert.Equal("codex", entry["config"]!["api"]!["headers"]!["x-loomx-managed"]!.GetValue<string>());
        Assert.Equal(["gpt-5.6-sol", "glm-5.3"], entry["config"]!["personalModelIds"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());

        var rules = json["config"]!["modelConfigRules"]!["providerModelRules"]!.AsArray();
        Assert.Equal(2, rules.Count);
        Assert.Equal(200_000, rules[0]!["config"]!["properties"]!["contextWindow"]!.GetValue<int>());
        Assert.Equal(128_000, rules[0]!["config"]!["optionSpecs"]!["maxOutputTokens"]!["max"]!.GetValue<int>());
        Assert.Equal(["minimal", "low", "medium", "high"], rules[0]!["config"]!["optionSpecs"]!["reasoningLevel"]!["values"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        // 无档位模型不写 reasoning 块。
        Assert.Null(rules[1]!["config"]!["optionSpecs"]!["reasoningLevel"]);
    }

    [Fact]
    public async Task ZCode_claude模式不带v1后缀()
    {
        var adapter = ZCodeConfigAdapter.Claude();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var rendered = (await adapter.WriteAsync(path, null, Input(["claude-alias"]), CancellationToken.None)).RenderedContent!;
        var json = JsonNode.Parse(rendered)!;

        var entry = json["config"]!["providerConfigRules"]!["providerRules"]![0]!;
        Assert.Equal("anthropic-messages", entry["config"]!["api"]!["type"]!.GetValue<string>());
        Assert.Equal(BaseUrl, entry["config"]!["api"]!["baseUrl"]!.GetValue<string>());
        Assert.Equal("claude", entry["config"]!["api"]!["headers"]!["x-loomx-managed"]!.GetValue<string>());
    }

    [Fact]
    public async Task ZCode_保留其他provider与兄弟平台条目()
    {
        var adapter = ZCodeConfigAdapter.Codex();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var existing = """
            {
              "schemaVersion": 1,
              "config": {
                "providerOrder": ["loomx-claude", "someone-elses-proxy"],
                "providerConfigRules": {
                  "providerRules": [
                    {
                      "providerId": "someone-elses-proxy",
                      "providerName": "Other Proxy",
                      "config": {
                        "group": "standard-personal",
                        "access": { "type": "api-key", "apiKey": "sk-not-ours" },
                        "api": { "type": "openai-responses", "baseUrl": "http://127.0.0.1:8080/v1" }
                      }
                    },
                    {
                      "providerId": "loomx-claude",
                      "providerName": "LoomX (Claude)",
                      "config": {
                        "group": "standard-personal",
                        "access": { "type": "api-key", "apiKey": "sk-claude-key" },
                        "api": {
                          "type": "anthropic-messages",
                          "baseUrl": "http://127.0.0.1:43111",
                          "headers": { "x-loomx-managed": "claude" }
                        }
                      }
                    }
                  ]
                },
                "modelConfigRules": {
                  "providerModelRules": [
                    {
                      "modelId": "GLM-5.3",
                      "providerId": "someone-elses-proxy",
                      "config": { "properties": { "contextWindow": 128000 } }
                    }
                  ],
                  "manualProviderModelRules": []
                }
              }
            }
            """;
        var rendered = WriteExisting(adapter, existing, Input());
        var json = JsonNode.Parse(rendered)!;

        var rules = json["config"]!["providerConfigRules"]!["providerRules"]!.AsArray();
        Assert.Equal(3, rules.Count);
        Assert.Equal("sk-not-ours", rules.First(node => node!["providerId"]!.GetValue<string>() == "someone-elses-proxy")["config"]!["access"]!["apiKey"]!.GetValue<string>());
        Assert.Equal("sk-claude-key", rules.First(node => node!["providerId"]!.GetValue<string>() == "loomx-claude")["config"]!["access"]!["apiKey"]!.GetValue<string>());
        Assert.Equal(["loomx-claude", "someone-elses-proxy"], json["config"]!["providerOrder"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        var otherRules = json["config"]!["modelConfigRules"]!["providerModelRules"]!.AsArray()
            .Where(node => node!["providerId"]!.GetValue<string>() == "someone-elses-proxy").ToArray();
        Assert.Single(otherRules);
    }

    [Fact]
    public async Task ZCode_收养带托管标记的改名条目()
    {
        var adapter = ZCodeConfigAdapter.Codex();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var existing = """
            {
              "schemaVersion": 1,
              "config": {
                "providerConfigRules": {
                  "providerRules": [{
                    "providerId": "3c109843-30ed-4307-a74e-ac537218d8be",
                    "providerName": "My Renamed Pool",
                    "config": {
                      "group": "standard-personal",
                      "access": { "type": "api-key", "apiKey": "sk-stale" },
                      "api": {
                        "type": "openai-responses",
                        "baseUrl": "http://127.0.0.1:1/v1",
                        "headers": { "x-loomx-managed": "codex" }
                      }
                    }
                  }]
                },
                "modelConfigRules": { "providerModelRules": [], "manualProviderModelRules": [] }
              }
            }
            """;
        var rendered = WriteExisting(adapter, existing, Input());
        var json = JsonNode.Parse(rendered)!;

        var rules = json["config"]!["providerConfigRules"]!["providerRules"]!.AsArray();
        Assert.Single(rules);
        Assert.Equal("3c109843-30ed-4307-a74e-ac537218d8be", rules[0]!["providerId"]!.GetValue<string>());
        Assert.Equal("My Renamed Pool", rules[0]!["providerName"]!.GetValue<string>());
        Assert.Equal(ApiKey, rules[0]!["config"]!["access"]!["apiKey"]!.GetValue<string>());
    }

    [Fact]
    public async Task ZCode_按base_url和key收养手写条目并拒绝无关条目()
    {
        var adapter = ZCodeConfigAdapter.Codex();
        var handmade = """
            {
              "schemaVersion": 1,
              "config": {
                "providerConfigRules": {
                  "providerRules": [{
                    "providerId": "hand-made",
                    "providerName": "LoomX",
                    "config": {
                      "group": "standard-personal",
                      "access": { "type": "api-key", "apiKey": "sk-loomx-test-key" },
                      "api": { "type": "openai-responses", "baseUrl": "http://127.0.0.1:43111/v1" }
                    }
                  }]
                },
                "modelConfigRules": { "providerModelRules": [], "manualProviderModelRules": [] }
              }
            }
            """;

        var path = adapter.ResolvePath(Path.Combine(fakeHome, "adopt"));
        var rendered = WriteExisting(adapter, handmade, Input(), subDir: "adopt");
        var json = JsonNode.Parse(rendered)!;
        var rules = json["config"]!["providerConfigRules"]!["providerRules"]!.AsArray();
        Assert.Single(rules);
        Assert.Equal("hand-made", rules[0]!["providerId"]!.GetValue<string>());
        Assert.Equal("codex", rules[0]!["config"]!["api"]!["headers"]!["x-loomx-managed"]!.GetValue<string>());

        var unrelated = handmade.Replace("http://127.0.0.1:43111/v1", "http://127.0.0.1:8080/v1", StringComparison.Ordinal)
            .Replace("sk-loomx-test-key", "sk-not-ours", StringComparison.Ordinal);
        var otherRendered = WriteExisting(adapter, unrelated, Input(), subDir: "unrelated");
        var otherJson = JsonNode.Parse(otherRendered)!;
        Assert.Equal(2, otherJson["config"]!["providerConfigRules"]!["providerRules"]!.AsArray().Count);
    }

    [Fact]
    public async Task ZCode_检查按平台区分_非法文件拒绝覆写()
    {
        var codexAdapter = ZCodeConfigAdapter.Codex();
        var claudeAdapter = ZCodeConfigAdapter.Claude();
        var path = codexAdapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);

        Assert.Equal("missing", codexAdapter.Inspect(path, null).FileStatus);

        const string claudeOnly = """
            {
              "schemaVersion": 1,
              "config": {
                "providerConfigRules": {
                  "providerRules": [{
                    "providerId": "loomx-claude",
                    "config": {
                      "api": {
                        "type": "anthropic-messages",
                        "baseUrl": "http://127.0.0.1:43111",
                        "headers": { "x-loomx-managed": "claude" }
                      }
                    }
                  }]
                },
                "modelConfigRules": { "providerModelRules": [], "manualProviderModelRules": [] }
              }
            }
            """;
        Assert.Equal("unmanaged", codexAdapter.Inspect(path, claudeOnly).FileStatus);
        Assert.Equal("managed", claudeAdapter.Inspect(path, claudeOnly).FileStatus);

        Assert.Equal("invalid", codexAdapter.Inspect(path, "{not json").FileStatus);
        Assert.Equal("invalid", codexAdapter.Inspect(path, "[]").FileStatus);
        Assert.Equal("invalid", codexAdapter.Inspect(path, """{"schemaVersion":1,"config":[]}""").FileStatus);
        await Assert.ThrowsAsync<ClientConfigException>(() => codexAdapter.WriteAsync(path, "{not json", Input(), CancellationToken.None));
        await Assert.ThrowsAsync<ClientConfigException>(() => codexAdapter.WriteAsync(path, "[]", Input(), CancellationToken.None));

        // 空文件不算损坏。
        var rendered = (await codexAdapter.WriteAsync(path, "   ", Input(), CancellationToken.None)).RenderedContent!;
        Assert.Contains("\"schemaVersion\": 1", rendered, StringComparison.Ordinal);
    }

    // ---------- opencode ----------

    [Fact]
    public async Task OpenCode_写入托管条目与默认model指针()
    {
        var adapter = new OpenCodeConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var rendered = (await adapter.WriteAsync(path, null, Input(["glm-5.3"]), CancellationToken.None)).RenderedContent!;
        File.WriteAllText(path.FullName, rendered, ClientConfigFiles.Utf8NoBom);
        var json = ParseJson(path.FullName);

        var entry = json["provider"]!["loomx"]!;
        Assert.Equal("@ai-sdk/openai-compatible", entry["npm"]!.GetValue<string>());
        Assert.Equal($"{BaseUrl}/v1", entry["options"]!["baseURL"]!.GetValue<string>());
        Assert.Equal(ApiKey, entry["options"]!["apiKey"]!.GetValue<string>());
        Assert.Equal(200_000, entry["models"]!["glm-5.3"]!["limit"]!["context"]!.GetValue<int>());
        Assert.Equal("loomx/glm-5.3", json["model"]!.GetValue<string>());
        Assert.Equal("https://opencode.ai/config.json", json["$schema"]!.GetValue<string>());
    }

    [Fact]
    public async Task OpenCode_保留其他provider与固定schema_二次写入原地更新()
    {
        var adapter = new OpenCodeConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var existing = """
            {
              "$schema": "https://example.invalid/pinned.json",
              "theme": "gruvbox",
              "model": "anthropic/claude-opus-5",
              "provider": {
                "myprovider": {
                  "npm": "@ai-sdk/openai-compatible",
                  "options": { "baseURL": "https://api.example.com/v1", "apiKey": "sk-theirs" }
                }
              },
              "mcp": { "filesystem": { "type": "local", "command": ["npx"] } }
            }
            """;
        var json = JsonNode.Parse(WriteExisting(adapter, existing, Input()))!;

        Assert.Equal("gruvbox", json["theme"]!.GetValue<string>());
        Assert.Equal("https://example.invalid/pinned.json", json["$schema"]!.GetValue<string>());
        Assert.Equal("sk-theirs", json["provider"]!["myprovider"]!["options"]!["apiKey"]!.GetValue<string>());
        Assert.Equal("loomx/gpt-5.6-sol", json["model"]!.GetValue<string>());

        // 二次写入：用户改过的名字保留，models 整表替换，未知 option 保留。
        var renamed = """{"provider":{"loomx":{"npm":"@ai-sdk/openai-compatible","name":"My Pool","options":{"baseURL":"http://127.0.0.1:1/v1","apiKey":"sk-stale","headers":{"X-Trace":"1"}},"models":{"gone":{}}}}}""";
        var second = JsonNode.Parse(WriteExisting(adapter, renamed, Input()))!;
        var entry = second["provider"]!["loomx"]!;
        Assert.Equal("My Pool", entry["name"]!.GetValue<string>());
        Assert.Equal("1", entry["options"]!["headers"]!["X-Trace"]!.GetValue<string>());
        Assert.Null(entry["models"]!["gone"]);
        Assert.Equal($"{BaseUrl}/v1", entry["options"]!["baseURL"]!.GetValue<string>());
    }

    // ---------- openclaw ----------

    [Fact]
    public async Task OpenClaw_写入托管provider与primary指针()
    {
        var adapter = new OpenClawConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var rendered = (await adapter.WriteAsync(path, null, Input(["glm-5.3", "kimi-k3"]), CancellationToken.None)).RenderedContent!;
        File.WriteAllText(path.FullName, rendered, ClientConfigFiles.Utf8NoBom);
        var json = ParseJson(path.FullName);

        var entry = json["models"]!["providers"]!["loomx"]!;
        Assert.Equal($"{BaseUrl}/v1", entry["baseUrl"]!.GetValue<string>());
        Assert.Equal(ApiKey, entry["apiKey"]!.GetValue<string>());
        Assert.Equal("openai-completions", entry["api"]!.GetValue<string>());
        Assert.Equal("merge", json["models"]!["mode"]!.GetValue<string>());
        Assert.Equal("loomx/glm-5.3", json["agents"]!["defaults"]!["model"]!["primary"]!.GetValue<string>());
        Assert.Equal(200_000, entry["models"]![0]!["contextWindow"]!.GetValue<int>());
        Assert.Equal(128_000, entry["models"]![0]!["maxTokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task OpenClaw_保留显式replace与用户fallback链_非法形状拒绝()
    {
        var adapter = new OpenClawConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var existing = """
            {
              "models": {
                "mode": "replace",
                "providers": {
                  "lmstudio": { "baseUrl": "http://localhost:1234/v1", "api": "openai-completions" }
                }
              },
              "agents": { "defaults": { "workspace": "~/.openclaw/workspace", "model": { "primary": "anthropic/claude-opus-5", "fallbacks": ["openai/gpt-5.6"] } } }
            }
            """;
        var json = JsonNode.Parse(WriteExisting(adapter, existing, Input()))!;

        Assert.Equal("replace", json["models"]!["mode"]!.GetValue<string>());
        Assert.Equal("http://localhost:1234/v1", json["models"]!["providers"]!["lmstudio"]!["baseUrl"]!.GetValue<string>());
        Assert.Equal("~/.openclaw/workspace", json["agents"]!["defaults"]!["workspace"]!.GetValue<string>());
        Assert.Equal(["openai/gpt-5.6"], json["agents"]!["defaults"]!["model"]!["fallbacks"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        Assert.Equal("loomx/gpt-5.6-sol", json["agents"]!["defaults"]!["model"]!["primary"]!.GetValue<string>());

        foreach (var corrupt in new[]
        {
            """{"models": "merge"}""",
            """{"models": {"providers": []}}""",
            """{"agents": {"defaults": {"model": "anthropic/claude-opus-5"}}}""",
            "{\n  // pool\n  models: { mode: 'merge' },\n}\n",
        })
        {
            await Assert.ThrowsAsync<ClientConfigException>(() => adapter.WriteAsync(path, corrupt, Input(), CancellationToken.None));
        }
    }

    // ---------- hermes（YAML 文本级 section 替换） ----------

    [Fact]
    public async Task Hermes_写入provider条目与model段()
    {
        var adapter = new HermesConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var rendered = (await adapter.WriteAsync(path, null, Input(["glm-5.3"]), CancellationToken.None)).RenderedContent!;
        File.WriteAllText(path.FullName, rendered, ClientConfigFiles.Utf8NoBom);

        Assert.Contains("name: loomx", rendered, StringComparison.Ordinal);
        Assert.Contains("base_url: http://127.0.0.1:43111/v1", rendered, StringComparison.Ordinal);
        Assert.Contains("api_key: sk-loomx-test-key", rendered, StringComparison.Ordinal);
        Assert.Contains("api_mode: chat_completions", rendered, StringComparison.Ordinal);
        Assert.Contains("model: glm-5.3", rendered, StringComparison.Ordinal);
        Assert.Contains("context_length: 200000", rendered, StringComparison.Ordinal);
        Assert.Contains("provider: loomx", rendered, StringComparison.Ordinal);
        Assert.Contains("default: glm-5.3", rendered, StringComparison.Ordinal);
        Assert.Equal("managed", adapter.Inspect(path, rendered).FileStatus);
    }

    [Fact]
    public async Task Hermes_只重写owned段_注释与外部provider逐字节保留()
    {
        var adapter = new HermesConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var existing = """
            # Hermes Agent CLI Configuration
            database:
              journal_mode: "wal"  # keep WAL

            model:
              # hand-picked
              default: "anthropic/claude-opus-4.6"
              provider: "openrouter"
              base_url: "https://openrouter.ai/api/v1"
              max_tokens: 8192

            custom_providers:
              - name: openrouter
                base_url: https://openrouter.ai/api/v1
                api_key: sk-or-theirs

            mcp_servers:
              filesystem:
                command: npx
            """;
        await File.WriteAllTextAsync(path.FullName, existing);
        var rendered = (await adapter.WriteAsync(path, existing, Input(), CancellationToken.None)).RenderedContent!;

        Assert.Contains("# Hermes Agent CLI Configuration", rendered, StringComparison.Ordinal);
        Assert.Contains("journal_mode: \"wal\"  # keep WAL", rendered, StringComparison.Ordinal);
        Assert.Contains("command: npx", rendered, StringComparison.Ordinal);
        Assert.Contains("max_tokens: 8192", rendered, StringComparison.Ordinal);
        Assert.Contains("api_key: sk-or-theirs", rendered, StringComparison.Ordinal);
        Assert.Equal("managed", adapter.Inspect(path, rendered).FileStatus);
    }

    [Fact]
    public async Task Hermes_二次写入保留未知字段且渲染稳定()
    {
        var adapter = new HermesConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        var first = (await adapter.WriteAsync(path, null, Input(["glm-5.3"]), CancellationToken.None)).RenderedContent!;
        var newline = first.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var withExtra = first.Replace(
            $"api_mode: chat_completions{newline}",
            $"api_mode: chat_completions{newline}  request_timeout_seconds: 600{newline}",
            StringComparison.Ordinal);
        Assert.Contains("request_timeout_seconds", withExtra, StringComparison.Ordinal);

        var second = (await adapter.WriteAsync(path, withExtra, Input(["kimi-k3"]), CancellationToken.None)).RenderedContent!;
        Assert.Contains("request_timeout_seconds: 600", second, StringComparison.Ordinal);
        Assert.Contains("model: kimi-k3", second, StringComparison.Ordinal);
        Assert.DoesNotContain("glm-5.3", second, StringComparison.Ordinal);

        // 渲染稳定：写入结果再写一次结果不变。
        var third = (await adapter.WriteAsync(path, second, Input(["kimi-k3"]), CancellationToken.None)).RenderedContent!;
        Assert.Equal(second, third);
    }

    [Fact]
    public async Task Hermes_保留CRLF_拒绝非法缩进_空文件可写()
    {
        var adapter = new HermesConfigAdapter();
        var path = adapter.ResolvePath(fakeHome);
        Directory.CreateDirectory(path.Directory!.FullName);
        const string crlf = "model:\r\n  default: old\r\n\r\ndatabase:\r\n  journal_mode: wal\r\n";
        var rendered = (await adapter.WriteAsync(path, crlf, Input(), CancellationToken.None)).RenderedContent!;
        Assert.Contains("provider: loomx\r\n", rendered, StringComparison.Ordinal);
        Assert.Contains("journal_mode: wal\r\n", rendered, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ClientConfigException>(() => adapter.WriteAsync(path, "model:\n\tdefault: x\n", Input(), CancellationToken.None));
        await Assert.ThrowsAsync<ClientConfigException>(() => adapter.WriteAsync(path, "- one\n- two\n", Input(), CancellationToken.None));

        var blank = (await adapter.WriteAsync(path, "\n  \n", Input(), CancellationToken.None)).RenderedContent!;
        Assert.Contains("provider: loomx", blank, StringComparison.Ordinal);
        Assert.Equal("unmanaged", adapter.Inspect(path, "custom_providers:\n  - name: openrouter\n").FileStatus);
    }
}
