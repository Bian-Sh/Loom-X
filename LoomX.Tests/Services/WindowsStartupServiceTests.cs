using LoomX.Services;
using Xunit;

namespace LoomX.Tests.Services;

public sealed class WindowsStartupServiceTests
{
    [Fact]
    public void ApplyEnabledWritesQuotedCurrentExecutableToCurrentUserRunValue()
    {
        var store = new RecordingStartupRegistryStore();
        var service = new WindowsStartupService(store, () => @"C:\Program Files\Loom-X\LoomX.exe");

        service.Apply(true);

        Assert.Equal("LoomX", store.WrittenName);
        Assert.Equal("\"C:\\Program Files\\Loom-X\\LoomX.exe\"", store.WrittenValue);
        Assert.Null(store.DeletedName);
    }

    [Fact]
    public void ApplyDisabledRemovesCurrentUserRunValue()
    {
        var store = new RecordingStartupRegistryStore();
        var service = new WindowsStartupService(store, () => @"C:\LoomX.exe");

        service.Apply(false);

        Assert.Equal("LoomX", store.DeletedName);
        Assert.Null(store.WrittenName);
    }

    [Fact]
    public void ApplyEnabledRejectsMissingExecutablePath()
    {
        var service = new WindowsStartupService(new RecordingStartupRegistryStore(), () => null);

        Assert.Throws<InvalidOperationException>(() => service.Apply(true));
    }

    private sealed class RecordingStartupRegistryStore : IStartupRegistryStore
    {
        public string? WrittenName { get; private set; }
        public string? WrittenValue { get; private set; }
        public string? DeletedName { get; private set; }

        public void SetValue(string name, string value)
        {
            WrittenName = name;
            WrittenValue = value;
        }

        public void DeleteValue(string name) => DeletedName = name;
    }
}
