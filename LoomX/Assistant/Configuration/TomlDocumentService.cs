using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace LoomX.Assistant.Configuration;

public sealed class TomlDocumentService : ITomlDocumentService
{
    internal const long MaxFileSizeBytes = 1024 * 1024;

    private const string AtomicReplaceStage = "AtomicReplace";
    private const string BackupStage = "Backup";
    private const string ConvertValueStage = "ConvertValue";
    private const int FileOperationAttempts = 3;
    private const string PatchCandidateStage = "PatchCandidate";
    private const string ValidateCandidateStage = "ValidateCandidate";
    private const string ParseDocumentStage = "ParseDocument";
    private const string ReadFileStage = "ReadFile";
    private const string RestoreStage = "Restore";
    private const string TempWriteStage = "TempWrite";
    private const string ValidateTargetStage = "ValidateTarget";
    private const string ValidateTempStage = "ValidateTemp";
    private static readonly TimeSpan FileOperationRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ITomlFileOperations fileOperations;
    private readonly ILogger<TomlDocumentService> logger;

    public TomlDocumentService(ILogger<TomlDocumentService> logger)
        : this(logger, new TomlFileOperations())
    {
    }

    internal TomlDocumentService(
        ILogger<TomlDocumentService> logger,
        ITomlFileOperations fileOperations)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
    }

    public async Task<TomlReadResult> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var parsed = await ParseDocumentAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!parsed.IsValid)
        {
            LogFailure(
                "Read",
                path,
                parsed.Stage,
                parsed.ErrorType,
                parsed.Exception,
                stopwatch.ElapsedMilliseconds);
            return new TomlReadResult(parsed.Exists, false, [], parsed.Errors);
        }

        var topLevelKeys = new List<string>(parsed.Root!.Properties.Count);
        foreach (var key in parsed.Root.Properties.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            topLevelKeys.Add(key);
        }

        cancellationToken.ThrowIfCancellationRequested();
        LogSuccess("Read", path, stopwatch.ElapsedMilliseconds);
        return new TomlReadResult(true, true, topLevelKeys, []);
    }

    public async Task<TomlValueResult> GetAsync(
        string path,
        TomlPath keyPath,
        CancellationToken cancellationToken = default)
    {
        if (keyPath.Segments is null || keyPath.Segments.Count == 0)
        {
            throw new ArgumentException("TOML 查询路径不能为空。", nameof(keyPath));
        }

        var stopwatch = Stopwatch.StartNew();
        var parsed = await ParseDocumentAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!parsed.IsValid)
        {
            LogFailure(
                "Get",
                path,
                parsed.Stage,
                parsed.ErrorType,
                parsed.Exception,
                stopwatch.ElapsedMilliseconds);
            return new TomlValueResult(false, null, null, parsed.Errors);
        }

        if (!TryFindNode(parsed.Root!, keyPath.Segments, cancellationToken, out var node))
        {
            LogSuccess("Get", path, stopwatch.ElapsedMilliseconds);
            return new TomlValueResult(false, null, null, []);
        }

        if (!TryConvertValue(node, cancellationToken, out var value, out var conversionError))
        {
            var errors = new[] { conversionError };
            LogFailure(
                "Get",
                path,
                ConvertValueStage,
                "UnsupportedTomlType",
                null,
                stopwatch.ElapsedMilliseconds);
            return new TomlValueResult(true, null, null, errors);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var redacted = SensitiveKeyPolicy.Redact(value!, keyPath.Segments);
        cancellationToken.ThrowIfCancellationRequested();
        LogSuccess("Get", path, stopwatch.ElapsedMilliseconds);
        return new TomlValueResult(true, redacted.Kind, redacted, []);
    }

    public async Task<TomlValidationResult> ValidateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var parsed = await ParseDocumentAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!parsed.IsValid)
        {
            LogFailure(
                "Validate",
                path,
                parsed.Stage,
                parsed.ErrorType,
                parsed.Exception,
                stopwatch.ElapsedMilliseconds);
            return new TomlValidationResult(false, parsed.Line, parsed.Column, parsed.Errors);
        }

        cancellationToken.ThrowIfCancellationRequested();
        LogSuccess("Validate", path, stopwatch.ElapsedMilliseconds);
        return new TomlValidationResult(true, null, null, []);
    }

    public async Task<TomlWriteResult> PatchAsync(
        string path,
        IReadOnlyList<TomlPatchOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogDebug(
            "TOML 操作开始 {Operation} {FileSummary}",
            "Patch",
            GetFileSummary(path));
        var stopwatch = Stopwatch.StartNew();
        var sourceResult = await ReadPatchSourceAsync(path, cancellationToken);
        if (!sourceResult.IsValid)
        {
            LogFailure(
                "Patch",
                path,
                sourceResult.Stage,
                sourceResult.ErrorType,
                sourceResult.Exception,
                stopwatch.ElapsedMilliseconds);
            return new TomlWriteResult(false, false, null, false, sourceResult.Errors);
        }

        var candidate = sourceResult.Content!;
        for (var index = 0; index < operations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationResult = ApplyPatchOperation(candidate, operations[index], cancellationToken);
            if (!operationResult.Success)
            {
                var errors = operationResult.Errors
                    .Select(error => $"Patch 第 {index + 1} 项失败：{error}")
                    .ToArray();
                LogFailure(
                    "Patch",
                    path,
                    PatchCandidateStage,
                    operationResult.ErrorType,
                    null,
                    stopwatch.ElapsedMilliseconds);
                return new TomlWriteResult(false, false, null, false, errors);
            }

            candidate = operationResult.Content!;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidateDocument = SyntaxParser.Parse(candidate, GetFileSummary(path), validate: true);
        if (candidateDocument.HasErrors)
        {
            var failure = CreateSyntaxFailure(candidateDocument, cancellationToken);
            LogFailure(
                "Patch",
                path,
                ValidateCandidateStage,
                failure.ErrorType,
                null,
                stopwatch.ElapsedMilliseconds);
            return new TomlWriteResult(false, false, null, false, failure.Errors);
        }

        if (string.Equals(candidate, sourceResult.Content, StringComparison.Ordinal))
        {
            LogSuccess("Patch", path, stopwatch.ElapsedMilliseconds);
            return new TomlWriteResult(true, false, null, false, []);
        }

        var writeResult = await WriteCandidateAsync(path, candidate, cancellationToken);
        if (!writeResult.Success)
        {
            LogFailure(
                "Patch",
                path,
                writeResult.Stage,
                writeResult.ErrorType,
                writeResult.Exception,
                stopwatch.ElapsedMilliseconds);
            return new TomlWriteResult(false, false, writeResult.BackupPath, false, writeResult.Errors);
        }

        LogSuccess("Patch", path, stopwatch.ElapsedMilliseconds);
        return new TomlWriteResult(true, true, writeResult.BackupPath, false, []);
    }

    private async Task<PatchSourceResult> ReadPatchSourceAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                return PatchSourceResult.Success(string.Empty);
            }

            if (fileInfo.Length > MaxFileSizeBytes)
            {
                return PatchSourceResult.Failure(
                    [$"TOML 文件大小不能超过 {MaxFileSizeBytes} 字节。"],
                    ReadFileStage,
                    "FileTooLarge");
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.LongLength > MaxFileSizeBytes)
            {
                return PatchSourceResult.Failure(
                    [$"TOML 文件大小不能超过 {MaxFileSizeBytes} 字节。"],
                    ReadFileStage,
                    "FileTooLarge");
            }

            if (HasUnsupportedUnicodeBom(bytes))
            {
                return PatchSourceResult.Failure(
                    ["TOML 文件必须使用 UTF-8 编码。"],
                    ReadFileStage,
                    "UnsupportedEncoding");
            }

            var content = DecodeUtf8(bytes);
            var document = SyntaxParser.Parse(content, GetFileSummary(path), validate: true);
            if (document.HasErrors)
            {
                var failure = CreateSyntaxFailure(document, cancellationToken);
                return PatchSourceResult.Failure(
                    failure.Errors,
                    failure.Stage,
                    failure.ErrorType);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return PatchSourceResult.Success(content);
        }
        catch (DecoderFallbackException exception)
        {
            return PatchSourceResult.Failure(
                ["TOML 文件不是有效的 UTF-8 文本。"],
                ReadFileStage,
                "InvalidUtf8",
                exception);
        }
        catch (FileNotFoundException)
        {
            return PatchSourceResult.Failure(["TOML 文件不存在。"], ReadFileStage, "FileNotFound");
        }
        catch (DirectoryNotFoundException)
        {
            return PatchSourceResult.Failure(["TOML 文件不存在。"], ReadFileStage, "FileNotFound");
        }
        catch (IOException exception)
        {
            return PatchSourceResult.Failure(
                ["读取 TOML 文件失败。"],
                ReadFileStage,
                "IOException",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return PatchSourceResult.Failure(
                ["没有权限读取 TOML 文件。"],
                ReadFileStage,
                "UnauthorizedAccess",
                exception);
        }
    }

    private static PatchOperationResult ApplyPatchOperation(
        string content,
        TomlPatchOperation operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = SyntaxParser.Parse(content, "patch-candidate.toml", validate: true);
        if (document.HasErrors)
        {
            return PatchOperationResult.Failure("候选 TOML 语法无效。", "TomlSyntaxError");
        }

        var root = BuildTree(document, cancellationToken);
        var path = operation.Path.Segments;
        if (!TryResolveParent(root, path, cancellationToken, out var parent, out var traversalError))
        {
            return PatchOperationResult.Failure(traversalError, "InvalidTraversal");
        }

        ConfigNode? target = null;
        var targetExists = parent is not null && parent.Properties.TryGetValue(path[^1], out target);
        if (operation.Kind == TomlPatchKind.Set)
        {
            if (targetExists
                && TryConvertValue(target!, cancellationToken, out var currentValue, out _)
                && TomlValuesEqual(currentValue!, operation.Value!))
            {
                return PatchOperationResult.CreateSuccess(content);
            }

            KeyValueLocation? existingLocation = null;
            if (targetExists && !TryFindKeyValue(document, path, cancellationToken, out existingLocation))
            {
                return PatchOperationResult.Failure(
                    target!.Kind == ConfigNodeKind.TableArray
                        ? "set 目标是数组表，不能替换为普通值。"
                        : "set 目标与现有普通表冲突。",
                    "DuplicateKey");
            }

            if (targetExists)
            {
                var replacement = CreateValueSyntax(operation.Value!);
                CopyValueTrivia(existingLocation!.Node.Value!, replacement);
                existingLocation.Node.Value = replacement;
            }
            else
            {
                AddMissingValue(document, root, path, operation.Value!, cancellationToken);
            }
        }
        else
        {
            if (!targetExists)
            {
                return PatchOperationResult.CreateSuccess(content);
            }

            if (!TryFindKeyValue(document, path, cancellationToken, out var existingLocation))
            {
                return PatchOperationResult.Failure(
                    "delete 只支持删除键值，不能删除普通表或数组表。",
                    "UnsupportedDeleteTarget");
            }

            existingLocation!.Remove();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return PatchOperationResult.CreateSuccess(document.ToString());
    }

    private static bool TryResolveParent(
        ConfigNode root,
        IReadOnlyList<string> path,
        CancellationToken cancellationToken,
        out ConfigNode? parent,
        out string error)
    {
        var current = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.Kind != ConfigNodeKind.Object)
            {
                parent = null;
                error = GetTraversalError(current.Kind);
                return false;
            }

            if (!current.Properties.TryGetValue(path[index], out var child))
            {
                parent = null;
                error = string.Empty;
                return true;
            }

            current = child;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (current.Kind != ConfigNodeKind.Object)
        {
            parent = null;
            error = GetTraversalError(current.Kind);
            return false;
        }

        parent = current;
        error = string.Empty;
        return true;
    }

    private static string GetTraversalError(ConfigNodeKind kind) => kind switch
    {
        ConfigNodeKind.TableArray => "TOML 路径不能穿越数组表。",
        ConfigNodeKind.Array => "TOML 路径不能穿越数组。",
        ConfigNodeKind.Scalar => "TOML 路径不能穿越标量。",
        ConfigNodeKind.Unsupported => "TOML 路径不能穿越不支持的值类型。",
        _ => "TOML 路径不能穿越非表值。",
    };

    private static void CopyValueTrivia(ValueSyntax source, ValueSyntax target)
    {
        var sourceTokens = source.Tokens(includeCommentsAndWhitespaces: true).OfType<SyntaxToken>().ToArray();
        var targetTokens = target.Tokens(includeCommentsAndWhitespaces: true).OfType<SyntaxToken>().ToArray();
        if (sourceTokens.Length == 0 || targetTokens.Length == 0)
        {
            return;
        }

        targetTokens[0].LeadingTrivia = CloneTrivia(sourceTokens[0].LeadingTrivia);
        targetTokens[^1].TrailingTrivia = CloneTrivia(sourceTokens[^1].TrailingTrivia);
    }

    private static List<SyntaxTrivia> CloneTrivia(IEnumerable<SyntaxTrivia>? source) =>
        source?.Select(trivia => new SyntaxTrivia(trivia.Kind, trivia.Text ?? string.Empty)).ToList() ?? [];

    private static bool TomlValuesEqual(TomlValue left, TomlValue right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            TomlValueKind.Array => ((IReadOnlyList<TomlValue>)left.Value)
                .SequenceEqual((IReadOnlyList<TomlValue>)right.Value, TomlValueComparer.Instance),
            TomlValueKind.Object => ObjectValuesEqual(
                (IReadOnlyDictionary<string, TomlValue>)left.Value,
                (IReadOnlyDictionary<string, TomlValue>)right.Value),
            _ => Equals(left.Value, right.Value),
        };
    }

    private static bool ObjectValuesEqual(
        IReadOnlyDictionary<string, TomlValue> left,
        IReadOnlyDictionary<string, TomlValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var property in left)
        {
            if (!right.TryGetValue(property.Key, out var rightValue)
                || !TomlValuesEqual(property.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddMissingValue(
        DocumentSyntax document,
        ConfigNode root,
        IReadOnlyList<string> path,
        TomlValue value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (path.Count == 1)
        {
            document.KeyValues.Add(CreateKeyValue(path, value));
            return;
        }

        var parentPath = path.Take(path.Count - 1).ToArray();
        if (TryFindKeyValue(document, parentPath, cancellationToken, out var parentLocation)
            && parentLocation!.Node.Value is InlineTableSyntax inlineTable)
        {
            AddInlineTableValue(inlineTable, path[^1], value);
            return;
        }

        var table = FindOrdinaryTable(document, parentPath, cancellationToken);
        if (table is not null)
        {
            table.Items.Add(CreateKeyValue([path[^1]], value));
            return;
        }

        if (TryFindImplicitOwner(document, parentPath, cancellationToken, out var owner))
        {
            owner!.Items.Add(CreateKeyValue(path.Skip(owner.BasePath.Count).ToArray(), value));
            return;
        }

        var newTable = CreateTable(parentPath, path[^1], value);
        document.Tables.Add(newTable);
    }

    private static TableSyntax? FindOrdinaryTable(
        DocumentSyntax document,
        IReadOnlyList<string> path,
        CancellationToken cancellationToken)
    {
        foreach (var table in document.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (table is TableSyntax ordinary
                && PathEquals(GetKeySegments(ordinary.Name!, cancellationToken), path))
            {
                return ordinary;
            }
        }

        return null;
    }

    private static bool TryFindImplicitOwner(
        DocumentSyntax document,
        IReadOnlyList<string> parentPath,
        CancellationToken cancellationToken,
        out KeyValueOwner? owner)
    {
        if (ContainsDescendantPath(document.KeyValues, [], parentPath, cancellationToken))
        {
            owner = new KeyValueOwner(document.KeyValues, []);
            return true;
        }

        foreach (var table in document.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (table is not TableSyntax ordinary)
            {
                continue;
            }

            var tablePath = GetKeySegments(ordinary.Name!, cancellationToken);
            if (ContainsDescendantPath(ordinary.Items, tablePath, parentPath, cancellationToken))
            {
                owner = new KeyValueOwner(ordinary.Items, tablePath);
                return true;
            }
        }

        owner = null;
        return false;
    }

    private static bool ContainsDescendantPath(
        SyntaxList<KeyValueSyntax> items,
        IReadOnlyList<string> basePath,
        IReadOnlyList<string> parentPath,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemPath = basePath.Concat(GetKeySegments(item.Key!, cancellationToken)).ToArray();
            if (PathStartsWith(itemPath, parentPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindKeyValue(
        DocumentSyntax document,
        IReadOnlyList<string> targetPath,
        CancellationToken cancellationToken,
        out KeyValueLocation? location)
    {
        if (TryFindInKeyValues(document.KeyValues, [], targetPath, cancellationToken, out location))
        {
            return true;
        }

        foreach (var table in document.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tablePath = GetKeySegments(table.Name!, cancellationToken);
            if (TryFindInKeyValues(table.Items, tablePath, targetPath, cancellationToken, out location))
            {
                return true;
            }
        }

        location = null;
        return false;
    }

    private static bool TryFindInKeyValues(
        SyntaxList<KeyValueSyntax> items,
        IReadOnlyList<string> basePath,
        IReadOnlyList<string> targetPath,
        CancellationToken cancellationToken,
        out KeyValueLocation? location)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemPath = basePath.Concat(GetKeySegments(item.Key!, cancellationToken)).ToArray();
            if (PathEquals(itemPath, targetPath))
            {
                location = new KeyValueLocation(item, () => items.RemoveChild(item));
                return true;
            }

            if (item.Value is InlineTableSyntax inlineTable
                && PathStartsWith(targetPath, itemPath)
                && TryFindInInlineTable(
                    inlineTable,
                    itemPath,
                    targetPath,
                    cancellationToken,
                    out location))
            {
                return true;
            }
        }

        location = null;
        return false;
    }

    private static bool TryFindInInlineTable(
        InlineTableSyntax inlineTable,
        IReadOnlyList<string> basePath,
        IReadOnlyList<string> targetPath,
        CancellationToken cancellationToken,
        out KeyValueLocation? location)
    {
        foreach (var item in inlineTable.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keyValue = item.KeyValue!;
            var itemPath = basePath.Concat(GetKeySegments(keyValue.Key!, cancellationToken)).ToArray();
            if (PathEquals(itemPath, targetPath))
            {
                location = new KeyValueLocation(
                    keyValue,
                    () => RemoveInlineTableItem(inlineTable, item));
                return true;
            }

            if (keyValue.Value is InlineTableSyntax nestedInlineTable
                && PathStartsWith(targetPath, itemPath)
                && TryFindInInlineTable(
                    nestedInlineTable,
                    itemPath,
                    targetPath,
                    cancellationToken,
                    out location))
            {
                return true;
            }
        }

        location = null;
        return false;
    }

    private static void AddInlineTableValue(
        InlineTableSyntax inlineTable,
        string key,
        TomlValue value)
    {
        var existingItems = inlineTable.Items.ToArray();
        if (existingItems.Length > 0 && existingItems[^1].Comma is null)
        {
            existingItems[^1].Comma = new SyntaxToken(TokenKind.Comma, ",")
            {
                TrailingTrivia = [new SyntaxTrivia(TokenKind.Whitespaces, " ")],
            };
        }

        inlineTable.Items.Add(CreateInlineTableItem(key, value));
    }

    private static void RemoveInlineTableItem(
        InlineTableSyntax inlineTable,
        InlineTableItemSyntax item)
    {
        var items = inlineTable.Items.ToArray();
        var index = Array.IndexOf(items, item);
        if (index < 0)
        {
            return;
        }

        if (index == items.Length - 1 && index > 0)
        {
            items[index - 1].Comma = null!;
        }

        inlineTable.Items.RemoveChild(item);
    }
    private static bool PathEquals(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.SequenceEqual(right, StringComparer.Ordinal);

    private static bool PathStartsWith(IReadOnlyList<string> path, IReadOnlyList<string> prefix)
    {
        if (path.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(path[index], prefix[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static KeyValueSyntax CreateKeyValue(IReadOnlyList<string> path, TomlValue value)
    {
        var snippet = $"{FormatKey(path)} = {FormatValue(value)}\n";
        var document = SyntaxParser.Parse(snippet, "patch-value.toml", validate: true);
        if (document.HasErrors || document.KeyValues.ChildrenCount != 1)
        {
            throw new InvalidOperationException("无法创建 TOML 键值候选。");
        }

        var keyValue = document.KeyValues.GetChild(0)!;
        document.KeyValues.RemoveChild(keyValue);
        return keyValue;
    }

    private static TableSyntax CreateTable(
        IReadOnlyList<string> tablePath,
        string key,
        TomlValue value)
    {
        var snippet = $"[{FormatKey(tablePath)}]\n{FormatKey([key])} = {FormatValue(value)}\n";
        var document = SyntaxParser.Parse(snippet, "patch-table.toml", validate: true);
        if (document.HasErrors || document.Tables.ChildrenCount != 1)
        {
            throw new InvalidOperationException("无法创建 TOML 普通表候选。");
        }

        var table = (TableSyntax)document.Tables.GetChild(0)!;
        document.Tables.RemoveChild(table);
        return table;
    }

    private static InlineTableItemSyntax CreateInlineTableItem(string key, TomlValue value)
    {
        var snippet = $"holder = {{ {FormatKey([key])} = {FormatValue(value)} }}\n";
        var document = SyntaxParser.Parse(snippet, "patch-inline-table.toml", validate: true);
        if (document.HasErrors || document.KeyValues.ChildrenCount != 1)
        {
            throw new InvalidOperationException("无法创建 TOML 内联表键值候选。");
        }

        var inlineTable = (InlineTableSyntax)document.KeyValues.GetChild(0)!.Value!;
        var item = inlineTable.Items.GetChild(0)!;
        inlineTable.Items.RemoveChild(item);
        return item;
    }
    private static ValueSyntax CreateValueSyntax(TomlValue value)
    {
        var snippet = $"value = {FormatValue(value)}\n";
        var document = SyntaxParser.Parse(snippet, "patch-value.toml", validate: true);
        if (document.HasErrors || document.KeyValues.ChildrenCount != 1)
        {
            throw new InvalidOperationException("无法创建 TOML 值候选。");
        }

        var keyValue = document.KeyValues.GetChild(0)!;
        var valueSyntax = keyValue.Value!;
        keyValue.Value = null!;
        return valueSyntax;
    }

    private static string FormatKey(IReadOnlyList<string> path) =>
        string.Join('.', path.Select(segment => new StringValueSyntax(segment).ToString()));

    private static string FormatValue(TomlValue value) => value.Kind switch
    {
        TomlValueKind.String => new StringValueSyntax((string)value.Value).ToString(),
        TomlValueKind.Integer => ((long)value.Value).ToString(CultureInfo.InvariantCulture),
        TomlValueKind.Float => ((double)value.Value).ToString("R", CultureInfo.InvariantCulture),
        TomlValueKind.Boolean => (bool)value.Value ? "true" : "false",
        TomlValueKind.Array => $"[{string.Join(", ", ((IReadOnlyList<TomlValue>)value.Value).Select(FormatValue))}]",
        TomlValueKind.Object => $"{{ {string.Join(", ", ((IReadOnlyDictionary<string, TomlValue>)value.Value).Select(
            property => $"{FormatKey([property.Key])} = {FormatValue(property.Value)}"))} }}",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "未定义的 TOML 值类型。"),
    };

    private async Task<WriteCandidateResult> WriteCandidateAsync(
        string path,
        string candidate,
        CancellationToken cancellationToken)
    {
        var targetExists = File.Exists(path);
        var backupPath = targetExists
            ? $"{path}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.bak"
            : null;
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (backupPath is not null)
            {
                try
                {
                    fileOperations.Copy(path, backupPath, overwrite: false);
                }
                catch (IOException exception)
                {
                    return WriteCandidateResult.Failure(
                        File.Exists(backupPath) ? backupPath : null,
                        ["创建 TOML 备份失败，原文件未修改。"],
                        BackupStage,
                        "IOException",
                        exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    return WriteCandidateResult.Failure(
                        File.Exists(backupPath) ? backupPath : null,
                        ["没有权限创建 TOML 备份，原文件未修改。"],
                        BackupStage,
                        "UnauthorizedAccess",
                        exception);
                }
            }

            try
            {
                await fileOperations.WriteAllTextAsync(
                    tempPath,
                    candidate,
                    StrictUtf8,
                    cancellationToken);
            }
            catch (IOException exception)
            {
                return WriteCandidateResult.Failure(
                    backupPath,
                    ["写入临时 TOML 文件失败，原文件或备份仍可用于恢复。"],
                    TempWriteStage,
                    "IOException",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return WriteCandidateResult.Failure(
                    backupPath,
                    ["没有权限写入临时 TOML 文件，原文件或备份仍可用于恢复。"],
                    TempWriteStage,
                    "UnauthorizedAccess",
                    exception);
            }

            var tempValidation = await ParseDocumentAsync(tempPath, cancellationToken);
            if (!tempValidation.IsValid)
            {
                return WriteCandidateResult.Failure(
                    backupPath,
                    ["临时 TOML 文件验证失败，原文件未修改。"],
                    ValidateTempStage,
                    tempValidation.ErrorType,
                    tempValidation.Exception);
            }

            var replaceFailure = await TryAtomicReplaceAsync(
                tempPath,
                path,
                targetExists,
                cancellationToken);
            if (replaceFailure is not null)
            {
                return WriteCandidateResult.Failure(
                    backupPath,
                    ["原子替换 TOML 文件失败，原文件或备份仍可用于恢复。"],
                    AtomicReplaceStage,
                    replaceFailure.GetType().Name,
                    replaceFailure);
            }

            var targetValidation = await ParseDocumentAsync(path, cancellationToken);
            if (targetValidation.IsValid)
            {
                return WriteCandidateResult.CreateSuccess(backupPath);
            }

            return await RestoreAfterValidationFailureAsync(
                path,
                backupPath,
                targetValidation,
                cancellationToken);
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath, path);
        }
    }

    private async Task<Exception?> TryAtomicReplaceAsync(
        string tempPath,
        string path,
        bool targetExists,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= FileOperationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (targetExists)
                {
                    fileOperations.Replace(tempPath, path);
                }
                else
                {
                    fileOperations.Move(tempPath, path);
                }

                return null;
            }
            catch (Exception exception) when (IsRetryableFileException(exception))
            {
                if (attempt == FileOperationAttempts)
                {
                    return exception;
                }

                logger.LogWarning(
                    CreateSafeLogException(exception),
                    "TOML 文件操作重试 {Operation} {FileSummary} {Stage} {ErrorType} {Attempt} {MaxAttempts}",
                    targetExists ? "Replace" : "Move",
                    GetFileSummary(path),
                    AtomicReplaceStage,
                    exception.GetType().Name,
                    attempt,
                    FileOperationAttempts);
                await fileOperations.DelayAsync(FileOperationRetryDelay, cancellationToken);
            }
        }

        return new IOException("TOML 原子替换未完成。");
    }

    private async Task<WriteCandidateResult> RestoreAfterValidationFailureAsync(
        string path,
        string? backupPath,
        ParseResult targetValidation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (backupPath is not null)
        {
            try
            {
                fileOperations.Copy(backupPath, path, overwrite: true);
                var restoredValidation = await ParseDocumentAsync(path, cancellationToken);
                if (!restoredValidation.IsValid)
                {
                    return WriteCandidateResult.Failure(
                        backupPath,
                        ["写入后的 TOML 文件验证失败，备份恢复后的目标仍无效。"],
                        RestoreStage,
                        restoredValidation.ErrorType,
                        restoredValidation.Exception);
                }

                return WriteCandidateResult.Failure(
                    backupPath,
                    ["写入后的 TOML 文件验证失败，已从备份恢复原文件。"],
                    ValidateTargetStage,
                    targetValidation.ErrorType,
                    targetValidation.Exception);
            }
            catch (IOException exception)
            {
                return WriteCandidateResult.Failure(
                    backupPath,
                    ["写入后的 TOML 文件验证失败，且从备份恢复失败；备份仍可用于恢复。"],
                    RestoreStage,
                    "IOException",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                return WriteCandidateResult.Failure(
                    backupPath,
                    ["写入后的 TOML 文件验证失败，且从备份恢复失败；备份仍可用于恢复。"],
                    RestoreStage,
                    "UnauthorizedAccess",
                    exception);
            }
        }

        try
        {
            if (File.Exists(path))
            {
                fileOperations.Delete(path);
            }

            return WriteCandidateResult.Failure(
                null,
                ["新 TOML 文件写后验证失败，已移除无效目标。"],
                ValidateTargetStage,
                targetValidation.ErrorType,
                targetValidation.Exception);
        }
        catch (IOException exception)
        {
            return WriteCandidateResult.Failure(
                null,
                ["新 TOML 文件写后验证失败，且移除无效目标失败。"],
                RestoreStage,
                "IOException",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return WriteCandidateResult.Failure(
                null,
                ["新 TOML 文件写后验证失败，且移除无效目标失败。"],
                RestoreStage,
                "UnauthorizedAccess",
                exception);
        }
    }

    private void TryDeleteTemporaryFile(string tempPath, string targetPath)
    {
        if (!File.Exists(tempPath))
        {
            return;
        }

        try
        {
            fileOperations.Delete(tempPath);
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                CreateSafeLogException(exception),
                "TOML 临时文件清理失败 {Operation} {FileSummary} {Stage} {ErrorType}",
                "Delete",
                GetFileSummary(targetPath),
                "CleanupTemp",
                "IOException");
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(
                CreateSafeLogException(exception),
                "TOML 临时文件清理失败 {Operation} {FileSummary} {Stage} {ErrorType}",
                "Delete",
                GetFileSummary(targetPath),
                "CleanupTemp",
                "UnauthorizedAccess");
        }
    }

    private static bool IsRetryableFileException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private async Task<ParseResult> ParseDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaxFileSizeBytes)
            {
                return CreateFileTooLargeFailure();
            }

            var bytes = await ReadFileBytesAsync(stream, cancellationToken);
            if (bytes is null)
            {
                return CreateFileTooLargeFailure();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (HasUnsupportedUnicodeBom(bytes))
            {
                return ParseResult.Failure(
                    true,
                    ["TOML 文件必须使用 UTF-8 编码。"],
                    ReadFileStage,
                    "UnsupportedEncoding");
            }

            var content = DecodeUtf8(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            var document = SyntaxParser.Parse(content, GetFileSummary(path), validate: true);
            cancellationToken.ThrowIfCancellationRequested();
            if (document.HasErrors)
            {
                return CreateSyntaxFailure(document, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return ParseResult.Success(BuildTree(document, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return ParseResult.Failure(
                false,
                ["TOML 文件不存在。"],
                ReadFileStage,
                "FileNotFound");
        }
        catch (DirectoryNotFoundException)
        {
            return ParseResult.Failure(
                false,
                ["TOML 文件不存在。"],
                ReadFileStage,
                "FileNotFound");
        }
        catch (DecoderFallbackException exception)
        {
            return ParseResult.Failure(
                true,
                ["TOML 文件不是有效的 UTF-8 文本。"],
                ReadFileStage,
                "InvalidUtf8",
                exception: exception);
        }
        catch (IOException exception)
        {
            return ParseResult.Failure(
                File.Exists(path),
                ["读取 TOML 文件失败。"],
                ReadFileStage,
                "IOException",
                exception: exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return ParseResult.Failure(
                File.Exists(path),
                ["没有权限读取 TOML 文件。"],
                ReadFileStage,
                "UnauthorizedAccess",
                exception: exception);
        }
    }

    private static ParseResult CreateFileTooLargeFailure() =>
        ParseResult.Failure(
            true,
            [$"TOML 文件大小不能超过 {MaxFileSizeBytes} 字节。"],
            ReadFileStage,
            "FileTooLarge");

    private static async Task<byte[]?> ReadFileBytesAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        using var content = new MemoryStream((int)stream.Length);
        var buffer = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (content.Length + read > MaxFileSizeBytes)
            {
                return null;
            }

            content.Write(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return content.ToArray();
    }

    private static bool HasUnsupportedUnicodeBom(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4
            && ((bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                || (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)))
        {
            return true;
        }

        return bytes.Length >= 2
            && ((bytes[0] == 0xFF && bytes[1] == 0xFE)
                || (bytes[0] == 0xFE && bytes[1] == 0xFF));
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        var offset = bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF
            ? 3
            : 0;
        return StrictUtf8.GetString(bytes.AsSpan(offset));
    }

    private static ParseResult CreateSyntaxFailure(
        DocumentSyntax document,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        int? firstLine = null;
        int? firstColumn = null;

        for (var index = 0; index < document.Diagnostics.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diagnostic = document.Diagnostics[index];
            if (diagnostic.Kind != DiagnosticMessageKind.Error)
            {
                continue;
            }

            var line = diagnostic.Span.Start.Line + 1;
            var column = diagnostic.Span.Start.Column + 1;
            firstLine ??= line;
            firstColumn ??= column;
            errors.Add($"第 {line} 行，第 {column} 列：{GetSafeDiagnosticMessage(diagnostic.Message)}");
        }

        if (errors.Count == 0)
        {
            errors.Add("TOML 语法无效。");
        }

        return ParseResult.Failure(
            true,
            errors,
            ParseDocumentStage,
            "TomlSyntaxError",
            firstLine,
            firstColumn);
    }

    private static string GetSafeDiagnosticMessage(string message)
    {
        if (message.Contains("string", StringComparison.OrdinalIgnoreCase))
        {
            return "TOML 字符串语法错误。";
        }

        if (message.Contains("table", StringComparison.OrdinalIgnoreCase))
        {
            return "TOML 表结构语法错误。";
        }

        if (message.Contains("key", StringComparison.OrdinalIgnoreCase))
        {
            return "TOML 键语法错误。";
        }

        return "TOML 语法错误。";
    }

    private static ConfigNode BuildTree(
        DocumentSyntax document,
        CancellationToken cancellationToken)
    {
        var root = ConfigNode.CreateObject();
        foreach (var keyValue in document.KeyValues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetValue(
                root,
                GetKeySegments(keyValue.Key!, cancellationToken),
                ConvertSyntaxValue(keyValue.Value!, cancellationToken),
                cancellationToken);
        }

        foreach (var table in document.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tablePath = GetKeySegments(table.Name!, cancellationToken);
            ConfigNode tableNode;
            if (table is TableArraySyntax)
            {
                tableNode = AddTableArrayItem(root, tablePath, cancellationToken);
            }
            else
            {
                tableNode = EnsureObjectPath(root, tablePath, cancellationToken);
            }

            foreach (var keyValue in table.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetValue(
                    tableNode,
                    GetKeySegments(keyValue.Key!, cancellationToken),
                    ConvertSyntaxValue(keyValue.Value!, cancellationToken),
                    cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return root;
    }

    private static IReadOnlyList<string> GetKeySegments(
        KeySyntax key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var segments = new List<string> { GetKeySegment(key.Key!) };
        foreach (var dottedKey in key.DotKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            segments.Add(GetKeySegment(dottedKey.Key!));
        }

        return segments;
    }

    private static string GetKeySegment(BareKeyOrStringValueSyntax key) => key switch
    {
        BareKeySyntax bareKey => bareKey.Key!.Text!.Trim(),
        StringValueSyntax stringKey => stringKey.Value ?? throw new InvalidOperationException("TOML 字符串键缺少值。"),
        _ => throw new InvalidOperationException("不支持的 TOML 键语法。"),
    };

    private static ConfigNode ConvertSyntaxValue(
        ValueSyntax value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return value switch
        {
            StringValueSyntax stringValue => ConfigNode.CreateScalar(
                TomlValue.FromObject(
                    stringValue.Value ?? throw new InvalidOperationException("TOML 字符串缺少值。"))),
            IntegerValueSyntax integerValue => ConfigNode.CreateScalar(TomlValue.FromObject(integerValue.Value)),
            FloatValueSyntax floatValue when double.IsFinite(floatValue.Value) =>
                ConfigNode.CreateScalar(TomlValue.FromObject(floatValue.Value)),
            FloatValueSyntax => ConfigNode.CreateUnsupported("非有限浮点数"),
            BooleanValueSyntax booleanValue => ConfigNode.CreateScalar(TomlValue.FromObject(booleanValue.Value)),
            ArraySyntax array => ConvertArray(array, cancellationToken),
            InlineTableSyntax inlineTable => ConvertInlineTable(inlineTable, cancellationToken),
            DateTimeValueSyntax => ConfigNode.CreateUnsupported("日期时间"),
            _ => ConfigNode.CreateUnsupported(value.Kind.ToString()),
        };
    }

    private static ConfigNode ConvertArray(
        ArraySyntax array,
        CancellationToken cancellationToken)
    {
        var node = ConfigNode.CreateArray();
        foreach (var item in array.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            node.Items.Add(ConvertSyntaxValue(item.Value!, cancellationToken));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return node;
    }

    private static ConfigNode ConvertInlineTable(
        InlineTableSyntax inlineTable,
        CancellationToken cancellationToken)
    {
        var node = ConfigNode.CreateObject();
        foreach (var item in inlineTable.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetValue(
                node,
                GetKeySegments(item.KeyValue!.Key!, cancellationToken),
                ConvertSyntaxValue(item.KeyValue!.Value!, cancellationToken),
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return node;
    }

    private static void SetValue(
        ConfigNode root,
        IReadOnlyList<string> path,
        ConfigNode value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = EnsureObjectPath(root, path.Take(path.Count - 1), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        parent.Properties[path[^1]] = value;
    }

    private static ConfigNode EnsureObjectPath(
        ConfigNode root,
        IEnumerable<string> path,
        CancellationToken cancellationToken)
    {
        var current = root;
        foreach (var segment in path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = GetCurrentObject(current);
            if (!current.Properties.TryGetValue(segment, out var child))
            {
                child = ConfigNode.CreateObject();
                current.Properties.Add(segment, child);
            }

            current = child;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return GetCurrentObject(current);
    }

    private static ConfigNode AddTableArrayItem(
        ConfigNode root,
        IReadOnlyList<string> path,
        CancellationToken cancellationToken)
    {
        var parent = EnsureObjectPath(root, path.Take(path.Count - 1), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!parent.Properties.TryGetValue(path[^1], out var array))
        {
            array = ConfigNode.CreateTableArray();
            parent.Properties.Add(path[^1], array);
        }

        if (array.Kind != ConfigNodeKind.TableArray)
        {
            throw new InvalidOperationException("TOML 数组表路径与现有值冲突。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var item = ConfigNode.CreateObject();
        array.Items.Add(item);
        return item;
    }

    private static ConfigNode GetCurrentObject(ConfigNode node)
    {
        if (node.Kind == ConfigNodeKind.Object)
        {
            return node;
        }

        if (node.Kind == ConfigNodeKind.TableArray
            && node.Items.Count > 0
            && node.Items[^1].Kind == ConfigNodeKind.Object)
        {
            return node.Items[^1];
        }

        throw new InvalidOperationException("TOML 路径穿越了非表值。");
    }

    private static bool TryFindNode(
        ConfigNode root,
        IReadOnlyList<string> path,
        CancellationToken cancellationToken,
        out ConfigNode node)
    {
        node = root;
        foreach (var segment in path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.Kind != ConfigNodeKind.Object
                || !node.Properties.TryGetValue(segment, out var child))
            {
                node = null!;
                return false;
            }

            node = child;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private static bool TryConvertValue(
        ConfigNode node,
        CancellationToken cancellationToken,
        out TomlValue? value,
        out string error)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (node.Kind)
        {
            case ConfigNodeKind.Scalar:
                value = node.ScalarValue;
                error = string.Empty;
                return true;
            case ConfigNodeKind.Object:
                {
                    var properties = new Dictionary<string, TomlValue>(StringComparer.Ordinal);
                    foreach (var property in node.Properties)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryConvertValue(
                            property.Value,
                            cancellationToken,
                            out var childValue,
                            out error))
                        {
                            value = null;
                            return false;
                        }

                        properties.Add(property.Key, childValue!);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    value = TomlValue.FromObjectProperties(properties);
                    cancellationToken.ThrowIfCancellationRequested();
                    error = string.Empty;
                    return true;
                }
            case ConfigNodeKind.Array:
            case ConfigNodeKind.TableArray:
                {
                    var items = new List<TomlValue>(node.Items.Count);
                    foreach (var item in node.Items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryConvertValue(item, cancellationToken, out var childValue, out error))
                        {
                            value = null;
                            return false;
                        }

                        items.Add(childValue!);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    value = TomlValue.FromArray(items);
                    cancellationToken.ThrowIfCancellationRequested();
                    error = string.Empty;
                    return true;
                }
            default:
                value = null;
                error = $"TOML 值类型 {node.UnsupportedType} 暂不支持。";
                return false;
        }
    }

    private static Exception CreateSafeLogException(Exception exception) => exception switch
    {
        IOException => new IOException(nameof(IOException)),
        UnauthorizedAccessException => new UnauthorizedAccessException(nameof(UnauthorizedAccessException)),
        _ => new Exception(exception.GetType().Name),
    };

    private void LogSuccess(string operation, string path, long elapsedMilliseconds)
    {
        logger.LogInformation(
            "TOML 操作完成 {Operation} {FileSummary} {ElapsedMs}ms",
            operation,
            GetFileSummary(path),
            elapsedMilliseconds);
    }

    private void LogFailure(
        string operation,
        string path,
        string stage,
        string errorType,
        Exception? exception,
        long elapsedMilliseconds)
    {
        if (exception is not null)
        {
            logger.LogError(
                CreateSafeLogException(exception),
                "TOML 操作失败 {Operation} {FileSummary} {Stage} {ErrorType} {ElapsedMs}ms",
                operation,
                GetFileSummary(path),
                stage,
                errorType,
                elapsedMilliseconds);
            return;
        }

        logger.LogWarning(
            "TOML 操作失败 {Operation} {FileSummary} {Stage} {ErrorType} {ElapsedMs}ms",
            operation,
            GetFileSummary(path),
            stage,
            errorType,
            elapsedMilliseconds);
    }

    private static string GetFileSummary(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
        {
            return "(未命名 TOML 文件)";
        }

        return fileName.Length <= 120 ? fileName : fileName[..120];
    }

    private enum ConfigNodeKind
    {
        Object,
        Array,
        TableArray,
        Scalar,
        Unsupported,
    }

    private sealed class ConfigNode
    {
        private ConfigNode(ConfigNodeKind kind)
        {
            Kind = kind;
        }

        public ConfigNodeKind Kind { get; }

        public Dictionary<string, ConfigNode> Properties { get; } = new(StringComparer.Ordinal);

        public List<ConfigNode> Items { get; } = [];

        public TomlValue? ScalarValue { get; private init; }

        public string? UnsupportedType { get; private init; }

        public static ConfigNode CreateObject() => new(ConfigNodeKind.Object);

        public static ConfigNode CreateArray() => new(ConfigNodeKind.Array);

        public static ConfigNode CreateTableArray() => new(ConfigNodeKind.TableArray);

        public static ConfigNode CreateScalar(TomlValue value) =>
            new(ConfigNodeKind.Scalar) { ScalarValue = value };

        public static ConfigNode CreateUnsupported(string typeName) =>
            new(ConfigNodeKind.Unsupported) { UnsupportedType = typeName };
    }

    private sealed record KeyValueLocation(KeyValueSyntax Node, Action Remove);

    private sealed record KeyValueOwner(
        SyntaxList<KeyValueSyntax> Items,
        IReadOnlyList<string> BasePath);

    private sealed class TomlValueComparer : IEqualityComparer<TomlValue>
    {
        public static TomlValueComparer Instance { get; } = new();

        public bool Equals(TomlValue? left, TomlValue? right) =>
            ReferenceEquals(left, right)
            || (left is not null && right is not null && TomlValuesEqual(left, right));

        public int GetHashCode(TomlValue value) => value.Kind.GetHashCode();
    }

    private sealed record PatchSourceResult(
        bool IsValid,
        string? Content,
        IReadOnlyList<string> Errors,
        string Stage,
        string ErrorType,
        Exception? Exception)
    {
        public static PatchSourceResult Success(string content) =>
            new(true, content, [], string.Empty, string.Empty, null);

        public static PatchSourceResult Failure(
            IReadOnlyList<string> errors,
            string stage,
            string errorType,
            Exception? exception = null) =>
            new(false, null, errors, stage, errorType, exception);
    }

    private sealed record PatchOperationResult(
        bool Success,
        string? Content,
        IReadOnlyList<string> Errors,
        string ErrorType)
    {
        public static PatchOperationResult CreateSuccess(string content) =>
            new(true, content, [], string.Empty);

        public static PatchOperationResult Failure(string error, string errorType) =>
            new(false, null, [error], errorType);
    }

    private sealed record WriteCandidateResult(
        bool Success,
        string? BackupPath,
        IReadOnlyList<string> Errors,
        string Stage,
        string ErrorType,
        Exception? Exception)
    {
        public static WriteCandidateResult CreateSuccess(string? backupPath) =>
            new(true, backupPath, [], string.Empty, string.Empty, null);

        public static WriteCandidateResult Failure(
            string? backupPath,
            IReadOnlyList<string> errors,
            string stage,
            string errorType,
            Exception? exception = null) =>
            new(false, backupPath, errors, stage, errorType, exception);
    }

    private sealed record ParseResult(
        bool Exists,
        bool IsValid,
        ConfigNode? Root,
        IReadOnlyList<string> Errors,
        string Stage,
        string ErrorType,
        int? Line,
        int? Column,
        Exception? Exception)
    {
        public static ParseResult Success(ConfigNode root) =>
            new(true, true, root, [], string.Empty, string.Empty, null, null, null);

        public static ParseResult Failure(
            bool exists,
            IReadOnlyList<string> errors,
            string stage,
            string errorType,
            int? line = null,
            int? column = null,
            Exception? exception = null) =>
            new(exists, false, null, errors, stage, errorType, line, column, exception);
    }
}
