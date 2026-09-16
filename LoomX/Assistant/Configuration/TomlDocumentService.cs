using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace LoomX.Assistant.Configuration;

public sealed class TomlDocumentService : ITomlDocumentService
{
    internal const long MaxFileSizeBytes = 1024 * 1024;

    private const string NotImplementedError = "TOML Patch 尚未实现。";
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
        if (!parsed.IsValid)
        {
            LogFailure("Read", path, parsed, stopwatch.ElapsedMilliseconds);
            return new TomlReadResult(parsed.Exists, false, [], parsed.Errors);
        }

        LogSuccess("Read", path, stopwatch.ElapsedMilliseconds);
        return new TomlReadResult(true, true, parsed.Root!.Properties.Keys.ToArray(), []);
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
        if (!parsed.IsValid)
        {
            LogFailure("Get", path, parsed, stopwatch.ElapsedMilliseconds);
            return new TomlValueResult(false, null, null, parsed.Errors);
        }

        if (!TryFindNode(parsed.Root!, keyPath.Segments, out var node))
        {
            LogSuccess("Get", path, stopwatch.ElapsedMilliseconds);
            return new TomlValueResult(false, null, null, []);
        }

        if (!TryConvertValue(node, out var value, out var conversionError))
        {
            var errors = new[] { conversionError };
            LogFailure(
                "Get",
                path,
                ParseResult.Failure(true, errors, "UnsupportedTomlType"),
                stopwatch.ElapsedMilliseconds);
            return new TomlValueResult(true, null, null, errors);
        }

        var redacted = SensitiveKeyPolicy.Redact(value!, keyPath.Segments);
        LogSuccess("Get", path, stopwatch.ElapsedMilliseconds);
        return new TomlValueResult(true, redacted.Kind, redacted, []);
    }

    public async Task<TomlValidationResult> ValidateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var parsed = await ParseDocumentAsync(path, cancellationToken);
        if (!parsed.IsValid)
        {
            LogFailure("Validate", path, parsed, stopwatch.ElapsedMilliseconds);
            return new TomlValidationResult(false, parsed.Line, parsed.Column, parsed.Errors);
        }

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
                return ParseResult.Failure(
                    true,
                    [$"TOML 文件大小不能超过 {MaxFileSizeBytes} 字节。"],
                    "FileTooLarge");
            }

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            if (stream.Length > MaxFileSizeBytes)
            {
                return ParseResult.Failure(
                    true,
                    [$"TOML 文件大小不能超过 {MaxFileSizeBytes} 字节。"],
                    "FileTooLarge");
            }

            var document = SyntaxParser.Parse(content, GetFileSummary(path), validate: true);
            if (document.HasErrors)
            {
                return CreateSyntaxFailure(document);
            }

            return ParseResult.Success(BuildTree(document));
        }
        catch (FileNotFoundException)
        {
            return ParseResult.Failure(false, ["TOML 文件不存在。"], "FileNotFound");
        }
        catch (DirectoryNotFoundException)
        {
            return ParseResult.Failure(false, ["TOML 文件不存在。"], "FileNotFound");
        }
        catch (DecoderFallbackException exception)
        {
            return ParseResult.Failure(
                true,
                ["TOML 文件不是有效的 UTF-8 文本。"],
                "InvalidUtf8",
                exception: exception);
        }
        catch (IOException)
        {
            return ParseResult.Failure(
                File.Exists(path),
                ["读取 TOML 文件失败。"],
                "IOException",
                exception: new IOException("读取 TOML 文件失败。"));
        }
        catch (UnauthorizedAccessException)
        {
            return ParseResult.Failure(
                File.Exists(path),
                ["没有权限读取 TOML 文件。"],
                "UnauthorizedAccess",
                exception: new UnauthorizedAccessException("没有权限读取 TOML 文件。"));
        }
    }

    private static ParseResult CreateSyntaxFailure(DocumentSyntax document)
    {
        var errors = new List<string>();
        int? firstLine = null;
        int? firstColumn = null;

        for (var index = 0; index < document.Diagnostics.Count; index++)
        {
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

    private static ConfigNode BuildTree(DocumentSyntax document)
    {
        var root = ConfigNode.CreateObject();
        foreach (var keyValue in document.KeyValues)
        {
            SetValue(root, GetKeySegments(keyValue.Key!), ConvertSyntaxValue(keyValue.Value!));
        }

        foreach (var table in document.Tables)
        {
            var tablePath = GetKeySegments(table.Name!);
            ConfigNode tableNode;
            if (table is TableArraySyntax)
            {
                tableNode = AddTableArrayItem(root, tablePath);
            }
            else
            {
                tableNode = EnsureObjectPath(root, tablePath);
            }

            foreach (var keyValue in table.Items)
            {
                SetValue(tableNode, GetKeySegments(keyValue.Key!), ConvertSyntaxValue(keyValue.Value!));
            }
        }

        return root;
    }

    private static IReadOnlyList<string> GetKeySegments(KeySyntax key)
    {
        var segments = new List<string> { GetKeySegment(key.Key!) };
        foreach (var dottedKey in key.DotKeys)
        {
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

    private static ConfigNode ConvertSyntaxValue(ValueSyntax value) => value switch
    {
        StringValueSyntax stringValue => ConfigNode.CreateScalar(TomlValue.FromObject(stringValue.Value ?? throw new InvalidOperationException("TOML 字符串缺少值。"))),
        IntegerValueSyntax integerValue => ConfigNode.CreateScalar(TomlValue.FromObject(integerValue.Value)),
        FloatValueSyntax floatValue when double.IsFinite(floatValue.Value) =>
            ConfigNode.CreateScalar(TomlValue.FromObject(floatValue.Value)),
        FloatValueSyntax => ConfigNode.CreateUnsupported("非有限浮点数"),
        BooleanValueSyntax booleanValue => ConfigNode.CreateScalar(TomlValue.FromObject(booleanValue.Value)),
        ArraySyntax array => ConfigNode.CreateArray(array.Items.Select(item => ConvertSyntaxValue(item.Value!))),
        InlineTableSyntax inlineTable => ConvertInlineTable(inlineTable),
        DateTimeValueSyntax => ConfigNode.CreateUnsupported("日期时间"),
        _ => ConfigNode.CreateUnsupported(value.Kind.ToString()),
    };

    private static ConfigNode ConvertInlineTable(InlineTableSyntax inlineTable)
    {
        var node = ConfigNode.CreateObject();
        foreach (var item in inlineTable.Items)
        {
            SetValue(
                node,
                GetKeySegments(item.KeyValue!.Key!),
                ConvertSyntaxValue(item.KeyValue!.Value!));
        }

        return node;
    }

    private static void SetValue(ConfigNode root, IReadOnlyList<string> path, ConfigNode value)
    {
        var parent = EnsureObjectPath(root, path.Take(path.Count - 1));
        parent.Properties[path[^1]] = value;
    }

    private static ConfigNode EnsureObjectPath(ConfigNode root, IEnumerable<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            current = GetCurrentObject(current);
            if (!current.Properties.TryGetValue(segment, out var child))
            {
                child = ConfigNode.CreateObject();
                current.Properties.Add(segment, child);
            }

            current = child;
        }

        return GetCurrentObject(current);
    }

    private static ConfigNode AddTableArrayItem(ConfigNode root, IReadOnlyList<string> path)
    {
        var parent = EnsureObjectPath(root, path.Take(path.Count - 1));
        if (!parent.Properties.TryGetValue(path[^1], out var array))
        {
            array = ConfigNode.CreateArray([]);
            parent.Properties.Add(path[^1], array);
        }

        if (array.Kind != ConfigNodeKind.Array)
        {
            throw new InvalidOperationException("TOML 数组表路径与现有值冲突。");
        }

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
        out ConfigNode node)
    {
        node = root;
        foreach (var segment in path)
        {
            if (node.Kind != ConfigNodeKind.Object
                || !node.Properties.TryGetValue(segment, out var child))
            {
                node = null!;
                return false;
            }

            node = child;
        }

        return true;
    }

    private static bool TryConvertValue(
        ConfigNode node,
        out TomlValue? value,
        out string error)
    {
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
                        if (!TryConvertValue(property.Value, out var childValue, out error))
                        {
                            value = null;
                            return false;
                        }

                        properties.Add(property.Key, childValue!);
                    }

                    value = TomlValue.FromObjectProperties(properties);
                    error = string.Empty;
                    return true;
                }
            case ConfigNodeKind.Array:
                {
                    var items = new List<TomlValue>(node.Items.Count);
                    foreach (var item in node.Items)
                    {
                        if (!TryConvertValue(item, out var childValue, out error))
                        {
                            value = null;
                            return false;
                        }

                        items.Add(childValue!);
                    }

                    value = TomlValue.FromArray(items);
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
        ParseResult parsed,
        long elapsedMilliseconds)
    {
        if (parsed.Exception is not null)
        {
            logger.LogError(
                parsed.Exception,
                "TOML 操作失败 {Operation} {FileSummary} {Stage} {ErrorType} {ElapsedMs}ms",
                operation,
                GetFileSummary(path),
                "ParseDocument",
                parsed.ErrorType,
                elapsedMilliseconds);
            return;
        }

        logger.LogWarning(
            "TOML 操作失败 {Operation} {FileSummary} {Stage} {ErrorType} {ElapsedMs}ms",
            operation,
            GetFileSummary(path),
            "ParseDocument",
            parsed.ErrorType,
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

        public static ConfigNode CreateArray(IEnumerable<ConfigNode> items)
        {
            var node = new ConfigNode(ConfigNodeKind.Array);
            node.Items.AddRange(items);
            return node;
        }

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
        string ErrorType,
        int? Line,
        int? Column,
        Exception? Exception)
    {
        public static ParseResult Success(ConfigNode root) =>
            new(true, true, root, [], string.Empty, null, null, null);

        public static ParseResult Failure(
            bool exists,
            IReadOnlyList<string> errors,
            string errorType,
            int? line = null,
            int? column = null,
            Exception? exception = null) =>
            new(exists, false, null, errors, errorType, line, column, exception);
    }
}
