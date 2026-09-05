using LoomX;
using Xunit;

namespace LoomX.Tests.Desktop;

public sealed class InstanceLaunchPolicyTests
{
    [Fact]
    public void AllowsMultipleInstancesForExplicitDebugArgument()
    {
        Assert.True(InstanceLaunchPolicy.AllowsMultipleInstances(["LoomX.exe", "--allow-multiple-instances"], null));
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
        Assert.False(InstanceLaunchPolicy.AllowsMultipleInstances(["LoomX.exe"], "0"));
    }
}
