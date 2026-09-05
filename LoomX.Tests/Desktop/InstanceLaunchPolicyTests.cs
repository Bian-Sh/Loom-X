using OllamaHub.Desktop;
using Xunit;

namespace OllamaHub.Tests.Desktop;

public sealed class InstanceLaunchPolicyTests
{
    [Fact]
    public void AllowsMultipleInstancesForExplicitDebugArgument()
    {
        Assert.True(InstanceLaunchPolicy.AllowsMultipleInstances(["OllamaHub.Desktop.exe", "--allow-multiple-instances"], null));
    }

    [Fact]
    public void AllowsMultipleInstancesForDebugEnvironmentVariable()
    {
        Assert.True(InstanceLaunchPolicy.AllowsMultipleInstances([], "1"));
    }

    [Fact]
    public void KeepsSingleInstanceByDefault()
    {
        Assert.False(InstanceLaunchPolicy.AllowsMultipleInstances([], null));
        Assert.False(InstanceLaunchPolicy.AllowsMultipleInstances(["OllamaHub.Desktop.exe"], "0"));
    }
}
