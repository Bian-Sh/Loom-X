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
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest(
            apiMode: "anthropic",
            endpointFormat: "messages"));

        Assert.EndsWith("/v1/messages", pipeline.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Null(pipeline.Authorization);
        Assert.Equal("api-secret", pipeline.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", pipeline.Headers["anthropic-version"]);

        var body = JsonNode.Parse(pipeline.Body!)!;
        Assert.Equal("每日一言", body["messages"]![0]!["content"]!.GetValue<string>());
        Assert.Equal(1024, body["max_tokens"]!.GetValue<int>());
        Assert.False(body["stream"]!.GetValue<bool>());
        Assert.Equal("hello world", result.ResponseText);
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
        Assert.Empty(result.ResponseText);
        Assert.DoesNotContain("sensitive-upstream-response", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 非Json成功响应返回协议错误且不暴露正文()
    {
        var pipeline = new CapturingPipeline(Result(HttpStatusCode.OK, "sensitive-non-json-response", "text/plain"));
        var service = CreateService(pipeline);

        var result = await service.ExecuteAsync(CreateRequest("openai", "chat_completions"));

        Assert.Equal(ProviderTestStatus.Failed, result.Status);
        Assert.Equal("invalid_json", result.ErrorCode);
        Assert.False(result.CanRetry);
        Assert.Empty(result.ResponseText);
        Assert.DoesNotContain("sensitive-non-json-response", result.ToString(), StringComparison.Ordinal);
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
        Assert.Empty(result.ResponseText);
        var observable = string.Join("\n", logger.Entries.Append(result.ToString()));
        Assert.DoesNotContain("sensitive-upstream-response", observable, StringComparison.Ordinal);
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
    public async Task 日志和安全结果不包含请求及响应敏感信息()
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

        var observable = string.Join("\n", logger.Entries.Append(result.ToString()));
        Assert.DoesNotContain(apiKey, observable, StringComparison.Ordinal);
        Assert.DoesNotContain(headerValue, observable, StringComparison.Ordinal);
        Assert.DoesNotContain(prompt, observable, StringComparison.Ordinal);
        Assert.DoesNotContain(responseBody, observable, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", observable, StringComparison.OrdinalIgnoreCase);
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
        int maxDisplayCharacters = 1000) =>
        new(
            RequestId: "request-1",
            ProviderId: "provider-1",
            ModelId: "model-1",
            BaseUrl: "https://provider.example/v1/",
            ApiMode: apiMode,
            EndpointFormat: endpointFormat,
            ApiKey: apiKey,
            Headers: headers ?? new Dictionary<string, string>(),
            UseProxy: false,
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
        private readonly Func<CancellationToken, Task<ProviderExecutionResult>> execute;

        public CapturingPipeline(ProviderExecutionResult result)
            : this(_ => Task.FromResult(result))
        {
        }

        public CapturingPipeline(Func<CancellationToken, Task<ProviderExecutionResult>> execute)
        {
            this.execute = execute;
        }

        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Body { get; private set; }

        public async Task<ProviderExecutionResult> ExecuteAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(",", header.Value);
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return await execute(cancellationToken);
        }

        public Task<ProviderStreamingResult> ExecuteStreamingAsync(
            HttpClient httpClient,
            HttpRequestMessage request,
            ProviderExecutionContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("普通请求测试不应调用流式管线。");
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