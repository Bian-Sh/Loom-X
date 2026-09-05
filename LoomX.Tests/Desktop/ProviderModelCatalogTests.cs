using System.Text.Json;
using LoomX.Configuration;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class ProviderModelCatalogTests
{
    [Fact]
    public void ExtractModelDescriptorsReadsOpenAiCompatibleMetadata()
    {
        using var document = JsonDocument.Parse("""
            {"data":[{"id":"gpt-5.6-sol","owned_by":"openai","context_length":400000,"top_provider":{"max_completion_tokens":128000},"capabilities":["vision"]}]}
            """);

        var descriptor = Assert.Single(ProvidersViewModel.ExtractModelDescriptors(document.RootElement));

        Assert.Equal("gpt-5.6-sol", descriptor.ModelId);
        Assert.Equal("openai", descriptor.OwnedBy);
        Assert.Equal(400000, descriptor.ContextLength);
        Assert.Equal(128000, descriptor.MaxTokens);
        Assert.True(descriptor.Vision);
    }

    [Fact]
    public void ExtractModelDescriptorsReadsGeminiLimitsAndKeepsMissingValuesUnknown()
    {
        using var document = JsonDocument.Parse("""
            {"models":[{"name":"models/gemini-2.5-pro","inputTokenLimit":1048576,"outputTokenLimit":65536},{"id":"opaque-model"}]}
            """);

        var descriptors = ProvidersViewModel.ExtractModelDescriptors(document.RootElement).ToArray();

        Assert.Equal(2, descriptors.Length);
        Assert.Equal("models/gemini-2.5-pro", descriptors[0].ModelId);
        Assert.Equal(1048576, descriptors[0].ContextLength);
        Assert.Equal(65536, descriptors[0].MaxTokens);
        Assert.Null(descriptors[0].Vision);
        Assert.Null(descriptors[1].ContextLength);
        Assert.Null(descriptors[1].MaxTokens);
    }

    [Fact]
    public void ModelViewModelDisplaysProviderMetadataWithoutLocalDefaults()
    {
        var model = ModelEditorViewModel.FromResponse(new ModelResponse(
            Guid.NewGuid(), "provider", "model", "model", null, "unknown", null, null,
            128000, 4096, false, null, null, true, false, "{}", "{}",
            "relay", null, null, null, null, 0));

        Assert.Equal("relay", model.OwnedBy);
        Assert.Equal("未提供", model.ContextDisplay);
        Assert.Equal("未提供", model.MaxTokensDisplay);
        Assert.Equal("未提供", model.CapabilitiesDisplay);
    }
}
