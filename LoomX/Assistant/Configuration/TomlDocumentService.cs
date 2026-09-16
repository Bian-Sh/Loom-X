using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace LoomX.Assistant.Configuration;

public sealed class TomlDocumentService : ITomlDocumentService
{
    internal const long MaxFileSizeBytes = 1024 * 1024;

    private const string ConvertValueStage = "ConvertValue";
    private const string NotImplementedError = "TOML Patch 尚未实现。";
    private const string ParseDocumentStage = "ParseDocument";
    private const string ReadFileStage = "ReadFile";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ILogger<TomlDocumentService> logger;

    public TomlDocumentService(ILogger<TomlDocumentService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    public Task<TomlWriteResult> PatchAsync(
        string path,
        IReadOnlyList<TomlPatchOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogWarning(
            "TOML 操作失败 {Operation} {FileSummary} {Stage} {ErrorType} {ElapsedMs}ms",
            "Patch",
            GetFileSummary(path),
            "NotImplemented",
            "NotImplemented",
            0L);
        return Task.FromResult(new TomlWriteResult(false, false, null, false, [NotImplementedError]));
    }

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
            array = ConfigNode.CreateArray();
            parent.Properties.Add(path[^1], array);
        }

        if (array.Kind != ConfigNodeKind.Array)
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

        if (node.Kind == ConfigNodeKind.Array
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
                exception,
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

        public static ConfigNode CreateScalar(TomlValue value) =>
            new(ConfigNodeKind.Scalar) { ScalarValue = value };

        public static ConfigNode CreateUnsupported(string typeName) =>
            new(ConfigNodeKind.Unsupported) { UnsupportedType = typeName };
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
