using LoomX.Assistant;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class ModelErrorFormatterTests
{
    [Fact]
    public void Format_ZhCn_ContainsLocalizedDescriptionAndMetaLines()
    {
        var detail = ModelErrorFormatter.Format(
            ModelErrorKind.Authentication,
            statusCode: 401,
            errorCode: "invalid_api_key",
            upstreamMessage: "Incorrect API key provided.");

        // 默认文化 zh-CN
        Assert.Contains("API Key 无效", detail);
        Assert.Contains("401", detail);
        Assert.Contains("invalid_api_key", detail);
        Assert.Contains("Incorrect API key provided.", detail);
        // 多行：描述 + 状态码 + 错误码 + 上游描述
        Assert.Equal(4, detail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Format_OmitsMissingMetaLines()
    {
        var detail = ModelErrorFormatter.Format(ModelErrorKind.ConnectionFailed);

        Assert.Single(detail.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("无法连接模型服务", detail);
    }

    [Fact]
    public void FormatException_ModelClientException_UsesStructuredFields()
    {
        var exception = new ModelClientException(
            "模型服务返回错误状态 404。",
            ModelErrorKind.ModelNotFound,
            statusCode: 404,
            errorCode: "model_not_found");

        var detail = ModelErrorFormatter.FormatException(exception);

        Assert.Contains("模型不存在或已下线", detail);
        Assert.Contains("model_not_found", detail);
    }

    [Fact]
    public void FormatException_GenericException_FallsBackToUnknownWithSanitizedMessage()
    {
        var exception = new InvalidOperationException("boom sk-secretvalue123 leaked");

        var detail = ModelErrorFormatter.FormatException(exception);

        Assert.Contains("模型请求失败", detail);
        Assert.DoesNotContain("secretvalue123", detail);
    }

    [Fact]
    public void FormatMaxStepsExceeded_InterpolatesStepCount()
    {
        var detail = ModelErrorFormatter.FormatMaxStepsExceeded(12);

        Assert.Contains("12", detail);
        Assert.Contains("最大步骤数", detail);
    }

    [Theory]
    [InlineData("en-US", "rate limit")]
    [InlineData("ja-JP", "レート制限")]
    [InlineData("zh-TW", "速率限制")]
    public void KindResourceKey_ResolvesInAllSatelliteCultures(string culture, string expectedFragment)
    {
        // 用显式 culture 解析，不切换全局 LocaleService（避免与并行测试互相干扰）
        var resolved = LoomX.Localization.ResourceLookup.Resolve(
            ModelErrorFormatter.KindResourceKey(ModelErrorKind.RateLimited),
            new System.Globalization.CultureInfo(culture));

        Assert.Contains(expectedFragment, resolved, StringComparison.OrdinalIgnoreCase);
    }
}
