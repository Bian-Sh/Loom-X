namespace OllamaHub;

public static class AppDataPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OllamaHub");

    public static string DatabasePath { get; } = Path.Combine(RootDirectory, "OllamaHub.db");

    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "logs");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
