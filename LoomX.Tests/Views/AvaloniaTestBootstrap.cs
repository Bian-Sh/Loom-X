using Avalonia;
using LoomX;
using Xunit;

namespace LoomX.Tests.Views;

[CollectionDefinition("Avalonia UI", DisableParallelization = true)]
public sealed class AvaloniaUiCollectionDefinition { }

internal static class AvaloniaTestBootstrap
{
    private static readonly object SetupLock = new();
    private static bool setupComplete;

    public static void Ensure()
    {
        lock (SetupLock)
        {
            if (setupComplete || Application.Current is not null)
            {
                setupComplete = true;
                return;
            }

            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .SetupWithoutStarting();
            setupComplete = true;
        }
    }
}
