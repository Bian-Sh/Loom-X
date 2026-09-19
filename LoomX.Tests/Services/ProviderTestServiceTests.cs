using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using LoomX.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class ProviderTestServiceTests
{
    public static TheoryData<string, string, string, string> 流式协议样例 => new()
    {
        {
            "openai",
            "chat_completions",
            """
            event: ignored.event
            data: not-json

            data: {"choices":[
            data: {"delta":{"content":"你"}}]}

            data: {"choices":[{"delta":{"content":"好"}}]}

            data: [DONE]

            data: {invalid-after-done}

            """,
            "[DONE]"
        },
        {
            "openai",
            "responses",
            """
            event: ignored.event
            data: not-json

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","delta":"你"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","delta":"好"}

            event: response.completed
            data: {"type":"response.completed"}

            event: response.output_text.delta
            data: {invalid-after-completed}

            """,
            "response.completed"
        },
        {
            "anthropic",
            "messages",
            """
            event: ignored.event
            data: not-json

            event: content_block_delta
            data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"你"}}

            event: content_block_delta
            data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"好"}}

            event: message_stop
            data: {"type":"message_stop"}

            event: content_block_delta
            data: {invalid-after-stop}

            """,
            "message_stop"
        },
    };

    public static TheoryData<string, string, string> 流式无效Json样例 => new()
    {
        {
            "openai",
            "chat_completions",
            """
            data: {invalid-json}

            """
        },
        {
            "openai",
            "responses",
            """
            event: response.output_text.delta
            data: {invalid-json}

            """
        },
        {
            "anthropic",
            "messages",
            """
            event: content_block_delta
            data: {invalid-json}

            """
        },
    };

    [Fact]
    public async Task OpenAiChat普通请求携带Bearer与自定义Header并解析响应()
    {
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"chat-ok"}}]}"""));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            apiMode: "openai",
            endpointFormat: "chat_completions",
            headers: new Dictionary<string, string> { ["X-Custom"] = "header-secret" }));

        Assert.EndsWith("/chat/completions", pipeline.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("Bearer", pipeline.Authorization!.Scheme);
        Assert.Equal("api-secret", pipeline.Authorization.Parameter);
        Assert.Equal("header-secret", pipeline.Headers["X-Custom"]);

        var body = JsonNode.Parse(pipeline.Body!)!;
        Assert.Equal("model-1", body["model"]!.GetValue<string>());
        Assert.Equal("每日一言", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.False(body["stream"]!.GetValue<bool>());

        Assert.True(result.IsSuccess);
        Assert.Equal("chat-ok", result.ResponseText);
        Assert.Equal(1, result.Summary.CustomHeaderCount);
        Assert.DoesNotContain("header-secret", result.Summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http失败展示格式化后的原始Json响应()
    {
        var pipeline = new CapturingPipeline(new ProviderExecutionResult(
            HttpStatusCode.BadRequest,
            "application/json",
            new Dictionary<string, string[]>(),
            Encoding.UTF8.GetBytes("{\"error\":{\"message\":\"bad request\",\"code\":\"invalid_input\"}}"),
            false));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest("openai", "responses"));

        Assert.False(result.IsSuccess);
        Assert.Equal("request_rejected", result.ErrorCode);
        Assert.Contains("\"message\": \"bad request\"", result.ResponseText, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine, result.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 无上游响应体的网络异常展示安全错误Json()
    {
        var pipeline = new CapturingPipeline(_ => throw new HttpRequestException("连接被拒绝"));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest("openai", "responses"));

        Assert.False(result.IsSuccess);
        Assert.Equal("network_error", result.ErrorCode);
        Assert.Contains("\"code\": \"network_error\"", result.ResponseText, StringComparison.Ordinal);
        Assert.Contains("连接被拒绝", result.ResponseText, StringComparison.Ordinal);
        Assert.DoesNotContain("api-secret", result.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 未预期异常转换为安全错误Json并写入错误日志()
    {
        var logger = new RecordingLogger<ProviderTestService>();
        var pipeline = new CapturingPipeline(_ => throw new InvalidOperationException("内部执行故障"));
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(CreateRequest("openai", "responses"));

        Assert.Equal(ProviderTestStatus.Failed, result.Status);
        Assert.Equal("internal_error", result.ErrorCode);
        Assert.Contains("\"code\": \"internal_error\"", result.ResponseText, StringComparison.Ordinal);
        Assert.Contains("Provider 测试未处理异常", string.Join("\n", logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 普通请求生命周期写入结构化日志且不记录正文()
    {
        var logger = new RecordingLogger<ProviderTestService>();
        var pipeline = new CapturingPipeline(JsonResult("{\"choices\":[{\"message\":{\"content\":\"sensitive-response\"}}]}"));
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        await service.ExecuteAsync(CreateRequest("openai", "chat_completions") with { Prompt = "sensitive-prompt" });

        var logs = string.Join("\n", logger.Entries);
        Assert.Contains("Provider 测试准备", logs, StringComparison.Ordinal);
        Assert.Contains("Provider 测试发送", logs, StringComparison.Ordinal);
        Assert.Contains("Provider 测试收到响应", logs, StringComparison.Ordinal);
        Assert.Contains("Provider 测试解析完成", logs, StringComparison.Ordinal);
        Assert.Contains("Provider 测试完成", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-prompt", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-response", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("api-secret", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAiResponses普通请求构造Input并解析嵌套文本()
    {
        var pipeline = new CapturingPipeline(JsonResult(
            """{"output":[{"content":[{"type":"output_text","text":"first"},{"type":"output_text","text":" second"}]}]}"""));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            apiMode: "openai",
            endpointFormat: "responses"));

        Assert.EndsWith("/responses", pipeline.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("Bearer", pipeline.Authorization!.Scheme);

        var body = JsonNode.Parse(pipeline.Body!)!;
        Assert.Equal("每日一言", body["input"]!.GetValue<string>());
        Assert.False(body["stream"]!.GetValue<bool>());
        Assert.Equal("first second", result.ResponseText);
    }

    [Fact]
    public async Task OpenAiResponses普通请求优先解析顶层OutputText()
    {
        var pipeline = new CapturingPipeline(JsonResult("""{"output_text":"top-level"}"""));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            apiMode: "openai",
            endpointFormat: "responses"));

        Assert.True(result.IsSuccess);
        Assert.Equal("top-level", result.ResponseText);
    }

    [Fact]
    public async Task Anthropic普通请求携带协议Header并拼接响应文本()
    {
        var pipeline = new CapturingPipeline(JsonResult(
            """{"content":[{"type":"text","text":"hello"},{"type":"text","text":" world"}]}"""));
        var logger = new RecordingLogger<ProviderTestService>();
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            apiMode: "anthropic",
            endpointFormat: "messages"));

        Assert.Equal("https://provider.example/v1/v1/messages", pipeline.RequestUri!.AbsoluteUri);
        Assert.Contains(logger.Entries, entry => entry.Contains("重复版本段", StringComparison.Ordinal)
            && entry.Contains("/v1/v1/messages", StringComparison.Ordinal));
        Assert.Null(pipeline.Authorization);
        Assert.Equal("api-secret", pipeline.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", pipeline.Headers["anthropic-version"]);

        var body = JsonNode.Parse(pipeline.Body!)!;
        Assert.Equal("每日一言", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(1024, body["max_tokens"]!.GetValue<int>());
        Assert.False(body["stream"]!.GetValue<bool>());
        Assert.Equal("hello world", result.ResponseText);
    }

    [Theory]
    [MemberData(nameof(流式协议样例))]
    public async Task 三协议流式按顺序报告增量并由完成事件结束(
        string apiMode,
        string endpointFormat,
        string sse,
        string completionMarker)
    {
        var pipeline = CapturingPipeline.ForStreaming(sse);
        var service = CreateService(pipeline);
        var deltas = new List<string>();
        var statuses = new List<ProviderTestStatus>();

        var result = await service.ExecuteAsync(
            CreateRequest(apiMode, endpointFormat) with { Mode = ProviderTestMode.Streaming },
            new InlineProgress<ProviderTestProgress>(item =>
            {
                statuses.Add(item.Status);
                if (item.TextDelta.Length > 0)
                    deltas.Add(item.TextDelta);
            }));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "你", "好" }, deltas);
        Assert.Equal("你好", result.ResponseText);
        Assert.False(result.IsTruncated);
        Assert.Equal(ProviderTestStatus.Completed, statuses[^1]);
        Assert.True(pipeline.UsedStreaming);
        Assert.False(pipeline.UsedRegular);

        var body = JsonNode.Parse(pipeline.Body!)!;
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.DoesNotContain(completionMarker, result.ResponseText, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(流式协议样例))]
    public async Task 三协议流式超过展示上限时只报告可展示增量并标记截断(
        string apiMode,
        string endpointFormat,
        string sse,
        string _)
    {
        var pipeline = CapturingPipeline.ForStreaming(sse);
        var service = CreateService(pipeline);
        var deltas = new List<string>();
        var truncationStates = new List<bool>();

        var result = await service.ExecuteAsync(
            CreateRequest(apiMode, endpointFormat, maxDisplayCharacters: 1) with { Mode = ProviderTestMode.Streaming },
            new InlineProgress<ProviderTestProgress>(item =>
            {
                if (item.TextDelta.Length > 0)
                    deltas.Add(item.TextDelta);
                truncationStates.Add(item.IsTruncated);
            }));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "你" }, deltas);
        Assert.Equal("你", result.ResponseText);
        Assert.True(result.IsTruncated);
        Assert.Contains(true, truncationStates);
    }

    [Theory]
    [MemberData(nameof(流式无效Json样例))]
    public async Task 三协议流式无效Json返回安全协议错误(
        string apiMode,
        string endpointFormat,
        string sse)
    {
        var logger = new RecordingLogger<ProviderTestService>();
        var pipeline = CapturingPipeline.ForStreaming(sse);
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(
            CreateRequest(apiMode, endpointFormat) with { Mode = ProviderTestMode.Streaming });

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_json", result.ErrorCode);
        Assert.Contains("invalid-json", result.ResponseText, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid-json", string.Join("\n", logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 关闭代理时使用注入的直连客户端且不读取全局代理设置()
    {
        using var directClient = new HttpClient(new ThrowingHandler());
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"ok"}}]}"""));
        var factoryCalled = false;
        var service = new ProviderTestService(
            directClient,
            NullLogger<ProviderTestService>.Instance,
            pipeline,
            proxySettingsReader: _ => throw new InvalidOperationException("直连不应读取代理设置。"),
            proxyHttpClientFactory: _ =>
            {
                factoryCalled = true;
                throw new InvalidOperationException("直连不应创建代理客户端。");
            });

        var result = await service.ExecuteAsync(CreateRequest("openai", "chat_completions"));

        Assert.True(result.IsSuccess);
        Assert.Same(directClient, pipeline.HttpClient);
        Assert.False(factoryCalled);
        Assert.False(result.Summary.UseProxy);
        Assert.Equal("direct", result.Summary.ProxySummary);
    }

    [Fact]
    public async Task System代理只启用UseProxy且不显式设置Proxy()
    {
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"ok"}}]}"""));
        HttpClientHandler? capturedHandler = null;
        var service = new ProviderTestService(
            new HttpClient(new ThrowingHandler()),
            NullLogger<ProviderTestService>.Instance,
            pipeline,
            proxySettingsReader: _ => Task.FromResult(new UpdateProxySettings(
                true, "system", string.Empty, 0, null, null)),
            proxyHttpClientFactory: handler =>
            {
                capturedHandler = handler;
                return new HttpClient(handler);
            });

        var result = await service.ExecuteAsync(CreateRequest(
            "openai",
            "chat_completions",
            useProxy: true));

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedHandler);
        Assert.True(capturedHandler.UseProxy);
        Assert.Null(capturedHandler.Proxy);
        Assert.True(result.Summary.UseProxy);
        Assert.Equal("system", result.Summary.ProxySummary);
    }

    [Fact]
    public async Task Custom代理应用地址和凭据并按请求释放客户端()
    {
        const string proxyPassword = "proxy-password-secret";
        var logger = new RecordingLogger<ProviderTestService>();
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"ok"}}]}"""));
        HttpClientHandler? capturedHandler = null;
        TrackingHttpClient? proxyClient = null;
        var service = new ProviderTestService(
            new HttpClient(new ThrowingHandler()),
            logger,
            pipeline,
            proxySettingsReader: _ => Task.FromResult(new UpdateProxySettings(
                true, "custom", "https://proxy.example", 7890, "proxy-user", proxyPassword)),
            proxyHttpClientFactory: handler =>
            {
                capturedHandler = handler;
                proxyClient = new TrackingHttpClient(handler);
                return proxyClient;
            });

        var result = await service.ExecuteAsync(CreateRequest(
            "openai",
            "chat_completions",
            useProxy: true));

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedHandler);
        Assert.True(capturedHandler.UseProxy);
        Assert.Equal(new Uri("https://proxy.example:7890/"), capturedHandler.Proxy!.GetProxy(new Uri("https://provider.example")));
        var credential = capturedHandler.Proxy.Credentials!.GetCredential(
            new Uri("https://proxy.example:7890/"),
            "Basic");
        Assert.Equal("proxy-user", credential!.UserName);
        Assert.Equal(proxyPassword, credential.Password);
        Assert.True(proxyClient!.IsDisposed);
        Assert.Equal("custom", result.Summary.ProxySummary);
        Assert.DoesNotContain(proxyPassword, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(proxyPassword, string.Join("\n", logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 无效Custom代理返回安全配置错误且不静默直连()
    {
        const string proxyPassword = "invalid-proxy-password-secret";
        var logger = new RecordingLogger<ProviderTestService>();
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"unexpected"}}]}"""));
        var factoryCalled = false;
        var service = new ProviderTestService(
            new HttpClient(new ThrowingHandler()),
            logger,
            pipeline,
            proxySettingsReader: _ => Task.FromResult(new UpdateProxySettings(
                true, "custom", "not-a-proxy-uri", 70000, "proxy-user", proxyPassword)),
            proxyHttpClientFactory: _ =>
            {
                factoryCalled = true;
                throw new InvalidOperationException("无效配置不应创建客户端。");
            });

        var result = await service.ExecuteAsync(CreateRequest(
            "openai",
            "chat_completions",
            useProxy: true));

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid_proxy_configuration", result.ErrorCode);
        Assert.False(result.CanRetry);
        Assert.False(pipeline.UsedRegular);
        Assert.False(factoryCalled);
        Assert.DoesNotContain(proxyPassword, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(proxyPassword, string.Join("\n", logger.Entries), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli身份Header真实进入请求且摘要只保留身份版本和Header数量()
    {
        const string customHeaderValue = "custom-header-secret";
        const string prompt = "prompt-secret";
        const string apiKey = "api-key-secret";
        var headers = CliIdentityService.BuildCliIdentityHeaders(CliIdentityType.Codex, "0.153.4");
        headers["X-Custom"] = customHeaderValue;
        var logger = new RecordingLogger<ProviderTestService>();
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"response-secret"}}]}"""));
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            "openai",
            "chat_completions",
            headers,
            apiKey,
            prompt));

        Assert.Equal("codex_cli_rs/0.153.4", pipeline.Headers["User-Agent"]);
        Assert.Equal("codex_cli_rs", pipeline.Headers["originator"]);
        Assert.Equal("0.153.4", pipeline.Headers["version"]);
        Assert.Equal(customHeaderValue, pipeline.Headers["X-Custom"]);
        Assert.Equal("Codex CLI 0.153.4", result.Summary.CliSummary);
        Assert.Equal(4, result.Summary.CustomHeaderCount);

        var summary = result.Summary.ToString();
        Assert.DoesNotContain(apiKey, summary, StringComparison.Ordinal);
        Assert.DoesNotContain(customHeaderValue, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("codex_cli_rs/0.153.4", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(prompt, summary, StringComparison.Ordinal);
        Assert.DoesNotContain("response-secret", summary, StringComparison.Ordinal);

        var logs = string.Join("\n", logger.Entries);
        Assert.DoesNotContain(apiKey, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(customHeaderValue, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(prompt, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("response-secret", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAi自定义Authorization不能覆盖标准Bearer且不进入摘要或日志()
    {
        const string conflictValue = "Bearer custom-authorization-secret";
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"ok"}}]}"""));
        var logger = new RecordingLogger<ProviderTestService>();
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            "openai",
            "chat_completions",
            new Dictionary<string, string> { ["Authorization"] = conflictValue }));

        Assert.Equal("Bearer", pipeline.Authorization!.Scheme);
        Assert.Equal("api-secret", pipeline.Authorization.Parameter);
        Assert.Equal(1, result.Summary.CustomHeaderCount);
        var observable = string.Join("\n", logger.Entries.Append(result.Summary.ToString()));
        Assert.DoesNotContain(conflictValue, observable, StringComparison.Ordinal);
        Assert.DoesNotContain("custom-authorization-secret", observable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anthropic自定义认证Header不能覆盖标准值且不进入摘要或日志()
    {
        const string conflictApiKey = "custom-anthropic-key-secret";
        const string conflictVersion = "custom-anthropic-version-secret";
        var pipeline = new CapturingPipeline(JsonResult("""{"content":[{"type":"text","text":"ok"}]}"""));
        var logger = new RecordingLogger<ProviderTestService>();
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            "anthropic",
            "messages",
            new Dictionary<string, string>
            {
                ["x-api-key"] = conflictApiKey,
                ["anthropic-version"] = conflictVersion,
            }));

        Assert.Equal("api-secret", pipeline.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", pipeline.Headers["anthropic-version"]);
        Assert.Equal(2, result.Summary.CustomHeaderCount);
        var observable = string.Join("\n", logger.Entries.Append(result.Summary.ToString()));
        Assert.DoesNotContain(conflictApiKey, observable, StringComparison.Ordinal);
        Assert.DoesNotContain(conflictVersion, observable, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "auth_failed", false)]
    [InlineData(HttpStatusCode.NotFound, "endpoint_error", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited", true)]
    [InlineData(HttpStatusCode.BadGateway, "upstream_error", true)]
    public async Task Http错误返回安全分类(HttpStatusCode statusCode, string errorCode, bool canRetry)
    {
        var pipeline = new CapturingPipeline(Result(
            statusCode,
            """{"error":{"message":"sensitive-upstream-response"}}"""));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest("openai", "chat_completions"));

        Assert.False(result.IsSuccess);
        Assert.Equal((int)statusCode, result.StatusCode);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal(canRetry, result.CanRetry);
        Assert.Contains("sensitive-upstream-response", result.ResponseText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 非Json成功响应返回协议错误并展示原始正文()
    {
        var pipeline = new CapturingPipeline(Result(HttpStatusCode.OK, "sensitive-non-json-response", "text/plain"));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest("openai", "chat_completions"));

        Assert.Equal(ProviderTestStatus.Failed, result.Status);
        Assert.Equal("invalid_json", result.ErrorCode);
        Assert.False(result.CanRetry);
        Assert.Equal("sensitive-non-json-response", result.ResponseText);
    }

    [Theory]
    [InlineData("openai", "chat_completions", "{}")]
    [InlineData("openai", "chat_completions", "{\"choices\":[]}")]
    [InlineData("openai", "chat_completions", "{\"error\":{\"message\":\"sensitive-upstream-response\"}}")]
    [InlineData("openai", "chat_completions", "{\"choices\":[{\"message\":{\"content\":123}}]}")]
    [InlineData("openai", "responses", "{}")]
    [InlineData("openai", "responses", "{\"output\":[]}")]
    [InlineData("openai", "responses", "{\"error\":{\"message\":\"sensitive-upstream-response\"}}")]
    [InlineData("openai", "responses", "{\"output\":[{\"content\":[{\"text\":123}]}]}")]
    [InlineData("anthropic", "messages", "{}")]
    [InlineData("anthropic", "messages", "{\"content\":[]}")]
    [InlineData("anthropic", "messages", "{\"error\":{\"message\":\"sensitive-upstream-response\"}}")]
    [InlineData("anthropic", "messages", "{\"content\":[{\"text\":123}]}")]
    public async Task 合法Json但协议结构无效统一返回安全InvalidResponse(
        string apiMode,
        string endpointFormat,
        string responseBody)
    {
        var pipeline = new CapturingPipeline(JsonResult(responseBody));
        var logger = new RecordingLogger<ProviderTestService>();
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(CreateRequest(apiMode, endpointFormat));

        Assert.Equal(ProviderTestStatus.Failed, result.Status);
        Assert.Equal("invalid_response", result.ErrorCode);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        Assert.NotEmpty(result.ResponseText);
        if (responseBody.Contains("sensitive-upstream-response", StringComparison.Ordinal))
            Assert.Contains("sensitive-upstream-response", result.ResponseText, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-upstream-response", string.Join("\n", logger.Entries), StringComparison.Ordinal);
    }
    [Fact]
    public async Task 超长普通响应按展示上限截断并报告进度()
    {
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"1234567890"}}]}"""));
        var service = CreateService(pipeline);
        var progressItems = new List<ProviderTestProgress>();

        var result = await service.ExecuteAsync(
            CreateRequest("openai", "chat_completions", maxDisplayCharacters: 5),
            new InlineProgress<ProviderTestProgress>(progressItems.Add));

        Assert.True(result.IsSuccess);
        Assert.Equal("12345", result.ResponseText);
        Assert.True(result.IsTruncated);
        Assert.Collection(
            progressItems,
            item => Assert.Equal(ProviderTestStatus.Sending, item.Status),
            item =>
            {
                Assert.Equal(ProviderTestStatus.Completed, item.Status);
                Assert.Equal(5, item.AccumulatedCharacters);
                Assert.True(item.IsTruncated);
            });
    }

    [Fact]
    public async Task 未由调用方取消的OperationCanceledException分类为超时()
    {
        var pipeline = new CapturingPipeline(_ =>
            Task.FromException<ProviderExecutionResult>(new OperationCanceledException("timeout")));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest("openai", "chat_completions"));

        Assert.Equal(ProviderTestStatus.Failed, result.Status);
        Assert.Equal("timeout", result.ErrorCode);
        Assert.True(result.CanRetry);
        Assert.False(result.IsCancelled);
    }

    [Fact]
    public async Task 调用方取消返回已取消结果而不抛出异常()
    {
        using var cancellation = new CancellationTokenSource();
        var pipeline = new CapturingPipeline(token =>
        {
            cancellation.Cancel();
            return Task.FromException<ProviderExecutionResult>(new OperationCanceledException(token));
        });
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(
            CreateRequest("openai", "chat_completions"),
            cancellationToken: cancellation.Token);

        Assert.Equal(ProviderTestStatus.Cancelled, result.Status);
        Assert.Equal("cancelled", result.ErrorCode);
        Assert.True(result.IsCancelled);
        Assert.False(result.CanRetry);
    }

    [Fact]
    public async Task 日志不包含请求及响应敏感信息但结果保留可见响应()
    {
        const string apiKey = "sensitive-api-key";
        const string headerValue = "sensitive-header-value";
        const string prompt = "sensitive-user-prompt";
        const string responseBody = "sensitive-upstream-response";
        var pipeline = new CapturingPipeline(Result(
            HttpStatusCode.InternalServerError,
            "{\"error\":{\"message\":\"" + responseBody + "\"}}"));
        var logger = new RecordingLogger<ProviderTestService>();
        var service = new ProviderTestService(new HttpClient(new ThrowingHandler()), logger, pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            "openai",
            "chat_completions",
            new Dictionary<string, string> { ["X-Secret"] = headerValue },
            apiKey,
            prompt));

        var observable = string.Join("\n", logger.Entries);
        Assert.DoesNotContain(apiKey, observable, StringComparison.Ordinal);
        Assert.DoesNotContain(headerValue, observable, StringComparison.Ordinal);
        Assert.DoesNotContain(prompt, observable, StringComparison.Ordinal);
        Assert.DoesNotContain(responseBody, observable, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", observable, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(responseBody, result.ResponseText, StringComparison.Ordinal);
        Assert.Equal(1, result.Summary.CustomHeaderCount);
    }

    [Fact]
    public async Task 请求Header在构造时复制且不受源字典后续修改影响()
    {
        var sourceHeaders = new Dictionary<string, string>
        {
            ["X-Snapshot"] = "original-value",
        };
        var request = CreateRequest("openai", "chat_completions", sourceHeaders);
        sourceHeaders["X-Snapshot"] = "mutated-value";
        sourceHeaders["X-Late"] = "late-value";
        sourceHeaders.Clear();
        var pipeline = new CapturingPipeline(JsonResult("""{"choices":[{"message":{"content":"ok"}}]}"""));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(request);

        Assert.Equal("original-value", request.Headers["X-Snapshot"]);
        Assert.Single(request.Headers);
        Assert.Equal(1, result.Summary.CustomHeaderCount);
        Assert.Equal("original-value", pipeline.Headers["X-Snapshot"]);
        Assert.False(pipeline.Headers.ContainsKey("X-Late"));
    }
    [Fact]
    public void 请求DTO使用不可变Record并支持安全快照复制()
    {
        var original = CreateRequest("openai", "chat_completions");

        var changed = original with { ModelId = "model-2" };

        Assert.Equal("model-1", original.ModelId);
        Assert.Equal("model-2", changed.ModelId);
        Assert.NotEqual(original, changed);
    }

    private static ProviderTestService CreateService(IProviderExecutionPipeline pipeline) =>
        new(new HttpClient(new ThrowingHandler()), NullLogger<ProviderTestService>.Instance, pipeline);

    private static ProviderTestRequest CreateRequest(
        string apiMode,
        string endpointFormat,
        IReadOnlyDictionary<string, string>? headers = null,
        string apiKey = "api-secret",
        string prompt = "每日一言",
        int maxDisplayCharacters = 1000,
        bool useProxy = false) =>
        new(
            RequestId: "request-1",
            ProviderId: "provider-1",
            ModelId: "model-1",
            BaseUrl: "https://provider.example/v1/",
            ApiMode: apiMode,
            EndpointFormat: endpointFormat,
            ApiKey: apiKey,
            Headers: headers ?? new Dictionary<string, string>(),
            UseProxy: useProxy,
            Prompt: prompt,
            Mode: ProviderTestMode.Regular,
            MaxDisplayCharacters: maxDisplayCharacters);

    private static ProviderExecutionResult JsonResult(string body) => new(
        HttpStatusCode.OK,
        "application/json",
        new Dictionary<string, string[]>(),
        Encoding.UTF8.GetBytes(body),
        false);

    private static ProviderExecutionResult Result(
        HttpStatusCode statusCode,
        string body,
        string contentType = "application/json") => new(
            statusCode,
            contentType,
            new Dictionary<string, string[]>(),
            Encoding.UTF8.GetBytes(body),
            false);

    private sealed class CapturingPipeline : IProviderExecutionPipeline
    {
        private readonly Func<CancellationToken, Task<ProviderExecutionResult>>? execute;
        private readonly Func<CancellationToken, Task<ProviderStreamingResult>>? executeStreaming;

        public CapturingPipeline(ProviderExecutionResult result)
            : this(_ => Task.FromResult(result))
        {
        }

        public CapturingPipeline(Func<CancellationToken, Task<ProviderExecutionResult>> execute)
        {
            this.execute = execute;
        }

        private CapturingPipeline(Func<CancellationToken, Task<ProviderStreamingResult>> executeStreaming)
        {
            this.executeStreaming = executeStreaming;
        }

        public static CapturingPipeline ForStreaming(string body) => new(_ =>
            Task.FromResult(CreateStreamingResult(body)));

        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Body { get; private set; }
        public HttpClient? HttpClient { get; private set; }
        public bool UsedRegular { get; private set; }
        public bool UsedStreaming { get; private set; }

        public async Task<ProviderExecutionResult> ExecuteAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken)
        {
            UsedRegular = true;
            HttpClient = httpClient;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(",", header.Value);
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return execute is null
                ? throw new InvalidOperationException("流式测试不应调用普通管线。")
                : await execute(cancellationToken);
        }

        public async Task<ProviderStreamingResult> ExecuteStreamingAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken)
        {
            UsedStreaming = true;
            HttpClient = httpClient;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(",", header.Value);
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return executeStreaming is null
                ? throw new InvalidOperationException("普通请求测试不应调用流式管线。")
                : await executeStreaming(cancellationToken);
        }

        private static ProviderStreamingResult CreateStreamingResult(string body)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(body), writable: false);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return new ProviderStreamingResult(response, stream);
        }
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler) : HttpClient(handler)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
                Entries.Add(string.Join(" | ", properties.Select(property => $"{property.Key}={property.Value}")));
            if (exception is not null)
                Entries.Add(exception.ToString());
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("测试不应直接发送 HTTP 请求。");
    }
}