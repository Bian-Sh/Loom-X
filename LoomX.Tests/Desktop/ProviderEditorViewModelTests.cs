using LoomX.Configuration;
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
