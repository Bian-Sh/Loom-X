using System.Reflection;
using System.Text.Json;
using LoomX.CredentialProtection;
using LoomX.Plugins;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class CredentialStorageMigrationTests
{
    [Fact]
    public void Store_ConsumesPublicStorageBridgeInsteadOfDirectory()
    {
        Assert.NotNull(typeof(CredentialTokenStore).GetConstructor([typeof(IPluginStorageBridge), typeof(IPluginProtectionBridge)]));
        Assert.Null(typeof(CredentialTokenStore).GetConstructor([typeof(string)]));
        Assert.DoesNotContain(typeof(CredentialTokenStore).Assembly.GetReferencedAssemblies(), item => item.Name == "System.Security.Cryptography.ProtectedData");
    }

    [Fact]
    public void Store_ExposesAsyncOperationsAndAsyncLifetime()
    {
        Assert.NotNull(typeof(CredentialTokenStore).GetMethod("GetOrCreateTokenAsync"));
        Assert.NotNull(typeof(CredentialTokenStore).GetMethod("ResolveTokensAsync"));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(CredentialTokenStore)));
        Assert.Null(typeof(CredentialTokenStore).GetMethod("GetOrCreateToken"));
        Assert.Null(typeof(CredentialTokenStore).GetMethod("ResolveTokens"));
    }

    [Fact]
    public void Manifest_DeclaresSqliteHostCapability()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "plugins", CredentialProtectionPlugin.PluginId, "plugin.manifest.json")));
        Assert.True(manifest.RootElement.TryGetProperty("host_capabilities", out var capabilities));
        Assert.Contains(capabilities.EnumerateArray(), item => item.GetString() == "storage.sqlite/v1");
    }
}
