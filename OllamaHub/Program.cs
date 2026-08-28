using Microsoft.EntityFrameworkCore;
using OllamaHub.Configuration;
using OllamaHub.Interop;

namespace OllamaHub;

public static class Program
{
    public static async Task Main(string[] args)
    {
        AppDataPaths.EnsureCreated();
        if (TryHandleCommand(args, AppDataPaths.DatabasePath))
        {
            return;
        }

        await using var app = await OllamaHubHost.CreateAsync();
        await app.RunAsync();
    }

    private static bool TryHandleCommand(string[] args, string databasePath)
    {
        if (args.Length == 0 || !string.Equals(args[0], "SetApiKey", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        WindowsConsoleManager.EnsureConsole();
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: OllamaHub SetApiKey <providerOrModelId> <apiKey>");
            return true;
        }

        try
        {
            SetProtectedApiKey(databasePath, args[1], args[2]);
            Console.WriteLine($"API Key for '{args[1]}' has been stored securely.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
        }

        return true;
    }

    private static void SetProtectedApiKey(string databasePath, string target, string apiKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("SetApiKey is only supported on Windows.");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Target provider or model id is required.", nameof(target));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        var protectedApiKey = ProtectedApiKeyStore.Protect(apiKey);
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        using var db = new ConfigurationDbContext(options);
        ConfigurationDatabase.InitializeAsync(db).GetAwaiter().GetResult();

        var provider = db.Providers.SingleOrDefault(item => item.BusinessId == target);
        if (provider is not null)
        {
            provider.ProtectedApiKey = protectedApiKey;
        }
        else
        {
            var model = db.Models.SingleOrDefault(item => item.ModelId == target);
            if (model is null)
            {
                throw new InvalidOperationException($"未找到 Provider 或 Model：{target}");
            }

            model.ProtectedApiKey = protectedApiKey;
        }

        db.SaveChanges();
    }
}
