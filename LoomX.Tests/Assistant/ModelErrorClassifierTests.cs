using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class ModelErrorClassifierTests
{
    [Theory]
    // OpenAI 官方错误码
    [InlineData("invalid_api_key", ModelErrorKind.Authentication)]
    [InlineData("insufficient_quota", ModelErrorKind.InsufficientQuota)]
    [InlineData("rate_limit_exceeded", ModelErrorKind.RateLimited)]
    [InlineData("model_not_found", ModelErrorKind.ModelNotFound)]
    [InlineData("context_length_exceeded", ModelErrorKind.ContextLengthExceeded)]
    [InlineData("content_filter", ModelErrorKind.ContentFiltered)]
    [InlineData("server_error", ModelErrorKind.ServerError)]
    [InlineData("invalid_request_error", ModelErrorKind.InvalidRequest)]
    // Anthropic 错误类型
    [InlineData("authentication_error", ModelErrorKind.Authentication)]
    [InlineData("permission_error", ModelErrorKind.PermissionDenied)]
    [InlineData("not_found_error", ModelErrorKind.ModelNotFound)]
    [InlineData("rate_limit_error", ModelErrorKind.RateLimited)]
    [InlineData("overloaded_error", ModelErrorKind.ServerOverloaded)]
    [InlineData("api_error", ModelErrorKind.ServerError)]
    // 中转站常见变体
    [InlineData("令牌无效", ModelErrorKind.Unknown)] // 非英文码不强行猜测
    [InlineData("quota_exceeded", ModelErrorKind.InsufficientQuota)]
    [InlineData("too_many_requests", ModelErrorKind.RateLimited)]
    [InlineData("prompt_too_long", ModelErrorKind.ContextLengthExceeded)]
    [InlineData("group rate limit exceeded", ModelErrorKind.RateLimited)]
    public void ClassifyCode_MapsKnownProviderCodes(string code, ModelErrorKind expected)
    {
        Assert.Equal(expected, ModelErrorClassifier.ClassifyCode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some_unlisted_code")]
    public void ClassifyCode_UnknownForUnrecognized(string? code)
    {
        Assert.Equal(ModelErrorKind.Unknown, ModelErrorClassifier.ClassifyCode(code));
    }

    [Theory]
    [InlineData(400, ModelErrorKind.InvalidRequest)]
    [InlineData(401, ModelErrorKind.Authentication)]
    [InlineData(402, ModelErrorKind.InsufficientQuota)]
    [InlineData(403, ModelErrorKind.PermissionDenied)]
    [InlineData(404, ModelErrorKind.ModelNotFound)]
    [InlineData(408, ModelErrorKind.Timeout)]
    [InlineData(413, ModelErrorKind.ContextLengthExceeded)]
    [InlineData(422, ModelErrorKind.InvalidRequest)]
    [InlineData(429, ModelErrorKind.RateLimited)]
    [InlineData(500, ModelErrorKind.ServerError)]
    [InlineData(502, ModelErrorKind.ServerError)]
    [InlineData(503, ModelErrorKind.ServerOverloaded)]
    [InlineData(529, ModelErrorKind.ServerOverloaded)]
    [InlineData(418, ModelErrorKind.Unknown)]
    public void ClassifyStatusCode_MapsHttpSemantics(int statusCode, ModelErrorKind expected)
    {
        Assert.Equal(expected, ModelErrorClassifier.ClassifyStatusCode(statusCode));
    }

    [Fact]
    public void Classify_PrefersProviderCodeOverStatusCode()
    {
        // 400 + invalid_api_key：以错误码语义为准（部分中转站鉴权失败也返回 400）
        Assert.Equal(ModelErrorKind.Authentication, ModelErrorClassifier.Classify(400, "invalid_api_key"));
        // 错误码无法识别时回落到状态码
        Assert.Equal(ModelErrorKind.RateLimited, ModelErrorClassifier.Classify(429, "weird_code"));
        Assert.Equal(ModelErrorKind.Unknown, ModelErrorClassifier.Classify(null, null));
    }

    [Fact]
    public void ParseErrorBody_OpenAiStyle()
    {
        var body = """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error","code":"invalid_api_key"}}""";
        var (code, message) = ModelErrorClassifier.ParseErrorBody(body);
        Assert.Equal("invalid_api_key", code);
        Assert.Equal("Incorrect API key provided", message);
    }

    [Fact]
    public void ParseErrorBody_AnthropicStyleFallsBackToType()
    {
        var body = """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";
        var (code, message) = ModelErrorClassifier.ParseErrorBody(body);
        Assert.Equal("overloaded_error", code);
        Assert.Equal("Overloaded", message);
    }

    [Fact]
    public void ParseErrorBody_ToleratesNonStandardShapes()
    {
        // error 为字符串
        var (code1, message1) = ModelErrorClassifier.ParseErrorBody("""{"error":"bad key"}""");
        Assert.Null(code1);
        Assert.Equal("bad key", message1);

        // 顶层 message/code
        var (code2, message2) = ModelErrorClassifier.ParseErrorBody("""{"code":"rate_limit","message":"slow down"}""");
        Assert.Equal("rate_limit", code2);
        Assert.Equal("slow down", message2);

        // 非 JSON / 非对象
        Assert.Equal((null, null), ModelErrorClassifier.ParseErrorBody("not json at all"));
        Assert.Equal((null, null), ModelErrorClassifier.ParseErrorBody("""["a"]"""));
        Assert.Equal((null, null), ModelErrorClassifier.ParseErrorBody(null));
    }

    [Fact]
    public void SanitizeUpstreamMessage_RedactsSecretsAndCollapsesWhitespace()
    {
        var sanitized = ModelErrorClassifier.SanitizeUpstreamMessage(
            "Incorrect API key provided: sk-abc123XYZ789.\nAuthorization: Bearer tok_live_123456");

        Assert.NotNull(sanitized);
        Assert.DoesNotContain("abc123XYZ789", sanitized);
        Assert.DoesNotContain("tok_live_123456", sanitized);
        Assert.DoesNotContain("\n", sanitized);
        Assert.Contains("sk-***", sanitized);
    }

    [Fact]
    public void SanitizeUpstreamMessage_TruncatesLongMessages()
    {
        var sanitized = ModelErrorClassifier.SanitizeUpstreamMessage(new string('x', 1000));
        Assert.NotNull(sanitized);
        Assert.True(sanitized.Length <= ModelErrorClassifier.MaxUpstreamMessageLength + 1);
        Assert.Null(ModelErrorClassifier.SanitizeUpstreamMessage("  "));
    }
}
