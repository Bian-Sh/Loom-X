using System.Text;

namespace LoomX.Assistant.Configuration;

internal interface ITomlFileOperations
{
    void Copy(string sourcePath, string destinationPath, bool overwrite);

    Task WriteAllTextAsync(
        string path,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken);

    void Replace(string sourcePath, string destinationPath);

    void Move(string sourcePath, string destinationPath);

    void Delete(string path);

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class TomlFileOperations : ITomlFileOperations
{
    public void Copy(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public Task WriteAllTextAsync(
        string path,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, encoding, cancellationToken);

    public void Replace(string sourcePath, string destinationPath) =>
        File.Replace(sourcePath, destinationPath, destinationBackupFileName: null);

    public void Move(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath);

    public void Delete(string path) => File.Delete(path);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
