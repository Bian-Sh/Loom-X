namespace LoomX.Assistant.Configuration;

public interface ITomlDocumentService
{
    Task<TomlReadResult> ReadAsync(string path, CancellationToken cancellationToken = default);

    Task<TomlValueResult> GetAsync(
        string path,
        TomlPath keyPath,
        CancellationToken cancellationToken = default);

    Task<TomlValidationResult> ValidateAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<TomlWriteResult> PatchAsync(
        string path,
        IReadOnlyList<TomlPatchOperation> operations,
        CancellationToken cancellationToken = default);
}
