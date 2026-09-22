namespace LoomX;

public static class AppDataPaths
{
    private static string LocalAppDataDirectory { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string RootDirectory { get; } = Path.Combine(LocalAppDataDirectory, "LoomX");

    public static string DatabasePath { get; } = Path.Combine(RootDirectory, "LoomX.db");

    public static string ActivityDatabasePath { get; } = Path.Combine(RootDirectory, "LoomX.Activity.db");

    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

    public static string ConfigurationInitializationLockPath { get; } = Path.Combine(RootDirectory, "LoomX.db.init.lock");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
