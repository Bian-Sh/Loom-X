using LoomX.Configuration;
using LoomX.Services;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class ProviderEditorViewModelTests
{
    [Fact]
    public void ExplicitModelListUrlPreservesTrailingSlashForSync()
    {
        var provider = new ProviderEditorViewModel { ModelListUrl = "https://www.baidu.com/" };

        Assert.Equal("https://www.baidu.com/", ProvidersViewModel.BuildModelListEndpoint(provider));
    }

    [Fact]
    public void FromResponse_PreservesProviderEnabledState()
    {
        var response = new ProviderResponse(
            Guid.NewGuid(),
            "disabled-provider",
            "已停用 Provider",
            "https://example.com",
            "openai",
            true,
            false,
            false,
            0,
            "{}",
            [],
            null,
            "responses",
            null);

        var viewModel = ProviderEditorViewModel.FromResponse(response);

        Assert.True(viewModel.Enabled);
    }

    [Fact]
    public void ToInput_ExcludesIncompleteHeaders()
    {
        var viewModel = new ProviderEditorViewModel();
        viewModel.AddHeader();
        viewModel.Headers[0].Name = "X-Incomplete";
        viewModel.AddHeader();
        viewModel.Headers[1].Name = "X-Complete";
        viewModel.Headers[1].Value = "ready";

        var input = viewModel.ToInput();

        Assert.NotNull(input.Headers);
        Assert.DoesNotContain("X-Incomplete", input.Headers!.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ready", input.Headers["X-Complete"]);
        Assert.Equal(1, viewModel.IncompleteHeaderCount);
    }

    [Fact]
    public void ApplyResponse_PreservesIncompleteHeaderDrafts()
    {
        var viewModel = new ProviderEditorViewModel();
        viewModel.AddHeader();
        viewModel.Headers[0].Name = "X-Draft";

        viewModel.ApplyResponse(new ProviderResponse(
            Guid.NewGuid(),
            "provider",
            "Provider",
            "https://example.com",
            "openai",
            true,
            false,
            false,
            0,
            "{\"X-Saved\":\"yes\"}",
            []));

        Assert.Equal(2, viewModel.Headers.Count);
        Assert.Contains(viewModel.Headers, header => header.Name == "X-Draft" && header.Value == "");
        Assert.Contains(viewModel.Headers, header => header.Name == "X-Saved" && header.Value == "yes");
    }

    [Fact]
    public void AddingHeaderRaisesProviderChangeEvenWhenPersistedDictionaryIsUnchanged()
    {
        var viewModel = new ProviderEditorViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.AddHeader();

        Assert.Contains(nameof(viewModel.Headers), changedProperties);
        Assert.True(viewModel.HasIncompleteHeaders);
    }

    [Fact]
    public void ProviderEditorTracksOnlyUserChangesAsUnsaved()
    {
        var viewModel = ProviderEditorViewModel.FromResponse(new ProviderResponse(
            Guid.NewGuid(),
            "provider",
            "Provider",
            "https://example.com",
            "openai",
            true,
            false,
            false,
            0,
            "{}",
            []));

        Assert.False(viewModel.HasUnsavedChanges);
        viewModel.DisplayName = "Provider";
        Assert.False(viewModel.HasUnsavedChanges);
        viewModel.DisplayName = "已修改";
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public void ApplySaveResult_BackfillsIdentityWithoutRewritingEditedText()
    {
        var viewModel = new ProviderEditorViewModel();
        viewModel.DisplayName = "草稿名称";
        viewModel.BaseUrl = "https://draft.example.com/";
        viewModel.AddHeader();
        viewModel.Headers[0].Name = "X-Draft";
        viewModel.Headers[0].Value = "draft";
        Assert.True(viewModel.HasUnsavedChanges);

        var id = Guid.NewGuid();
        viewModel.ApplySaveResult(new ProviderResponse(
            id,
            "provider",
            "规范化名称",
            "https://normalized.example.com",
            "openai",
            true,
            false,
            false,
            0,
            "{}",
            []));

        Assert.Equal(id, viewModel.Id);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.Equal("草稿名称", viewModel.DisplayName);
        Assert.Equal("https://draft.example.com/", viewModel.BaseUrl);
        Assert.Single(viewModel.Headers);
        Assert.Equal("X-Draft", viewModel.Headers[0].Name);
        Assert.Equal("draft", viewModel.Headers[0].Value);
    }

    [Theory]
    [InlineData("  grox  ")]
    [InlineData("PROVIDER-ID")]
    [InlineData("api.example.com")]
    [InlineData("ANTHROPIC")]
    public void ProviderSearchMatchesVisibleProviderFieldsIgnoringCase(string query)
    {
        var provider = new ProviderEditorViewModel
        {
            DisplayName = "Grox",
            BusinessId = "provider-id",
            BaseUrl = "https://api.example.com/v1",
            ApiMode = "anthropic"
        };

        Assert.True(ProvidersViewModel.MatchesProviderSearch(provider, query));
    }

    [Fact]
    public void ProviderSearchRejectsUnmatchedQuery()
    {
        var provider = new ProviderEditorViewModel
        {
            DisplayName = "Grox",
            BusinessId = "provider-id",
            BaseUrl = "https://api.example.com/v1",
            ApiMode = "openai"
        };

        Assert.False(ProvidersViewModel.MatchesProviderSearch(provider, "missing"));
    }

    [Fact]
    public void HealthResultBuildsUsefulStatusAndDetail()
    {
        var provider = new ProviderEditorViewModel { Enabled = true };

        provider.ApplyHealthResult(new ProviderHealthResult(
            ProviderHealthState.Healthy,
            StatusCode: 200,
            LatencyMs: 42,
            DiscoveredModelCount: 3));

        Assert.True(provider.IsHealthSuccess);
        Assert.Equal("正常", provider.HealthStatusText);
        Assert.Contains("HTTP 200", provider.HealthDetailText, StringComparison.Ordinal);
        Assert.Contains("3 个模型", provider.HealthDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationResetReturnsProviderToPendingState()
    {
        var provider = new ProviderEditorViewModel { Enabled = true };
        provider.ApplyHealthResult(new ProviderHealthResult(ProviderHealthState.Healthy, StatusCode: 200, LatencyMs: 10, DiscoveredModelCount: 1));

        provider.ResetHealthForConfigurationChange();

        Assert.Equal(ProviderHealthState.Unknown, provider.HealthState);
        Assert.True(provider.IsHealthUnknown);
        Assert.Contains("尚未验证", provider.HealthDetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyModelResultIsWarningButStillPassed()
    {
        var provider = new ProviderEditorViewModel { Enabled = true };

        provider.ApplyHealthResult(new ProviderHealthResult(ProviderHealthState.HealthyEmptyModels, StatusCode: 200, LatencyMs: 8, DiscoveredModelCount: 0));

        Assert.False(provider.IsHealthSuccess);
        Assert.True(provider.IsHealthWarning);
        Assert.True(provider.IsHealthPassed);
    }

    [Fact]
    public void DisabledProviderDoesNotRemainHealthy()
    {
        var provider = new ProviderEditorViewModel { Enabled = false };
        provider.ApplyHealthResult(new ProviderHealthResult(ProviderHealthState.Healthy, StatusCode: 200));

        provider.ResetHealthForConfigurationChange();

        Assert.Equal(ProviderHealthState.Disabled, provider.HealthState);
        Assert.True(provider.IsHealthDisabled);
    }

    [Fact]
    public void ModelEditorFromResponseStartsCleanAndTracksChanges()
    {
        var viewModel = ModelEditorViewModel.FromResponse(new ModelResponse(
            Guid.NewGuid(),
            "provider",
            "model",
            "模型",
            null,
            "unknown",
            null,
            null,
            128000,
            4096,
            false,
            null,
            null,
            true,
            false,
            "{}",
            "{}"));

        Assert.False(viewModel.HasUnsavedChanges);
        viewModel.Enabled = false;
        Assert.True(viewModel.HasUnsavedChanges);
    }
}
