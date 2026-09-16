using System.Text;
using LoomX.Assistant.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class TomlDocumentServiceTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "LoomX TOML 测试",
        Guid.NewGuid().ToString("N"));

    public TomlDocumentServiceTests()
    {
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void TomlReadResult_复制集合以保持不可变()
    {
        var keys = new List<string> { "model" };
        var errors = new List<string> { "读取失败" };
        var result = new TomlReadResult(true, false, keys, errors);

        keys.Add("provider");
        errors.Clear();

        Assert.Equal(["model"], result.TopLevelKeys);
        Assert.Equal(["读取失败"], result.Errors);
    }

    [Fact]
    public async Task ReadAsync_返回顶层键并支持嵌套表与中文空格路径()
    {
        var file = WriteToml(
            "配置 文件.toml",
            """
            title = "demo"
            database.server = "localhost"
            [model_providers.loomx]
            model = "loomx-chat"
            [[servers]]
            name = "one"
            """);
        var service = CreateService();

        var result = await service.ReadAsync(file);

        Assert.True(result.Exists);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(["title", "database", "model_providers", "servers"], result.TopLevelKeys);
    }

    [Fact]
    public async Task ReadAsync_空文件有效且没有顶层键()
    {
        var file = WriteToml("empty.toml", string.Empty);
        var service = CreateService();

        var result = await service.ReadAsync(file);

        Assert.True(result.Exists);
        Assert.True(result.IsValid);
        Assert.Empty(result.TopLevelKeys);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReadAsync_缺失文件返回不存在而不创建文件()
    {
        var file = Path.Combine(tempDirectory, "missing.toml");
        var service = CreateService();

        var result = await service.ReadAsync(file);

        Assert.False(result.Exists);
        Assert.False(result.IsValid);
        Assert.Empty(result.TopLevelKeys);
        Assert.NotEmpty(result.Errors);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task ReadAsync_拒绝超过大小上限的文件()
    {
        var file = Path.Combine(tempDirectory, "large.toml");
        await File.WriteAllBytesAsync(
            file,
            Enumerable.Repeat((byte)'a', checked((int)TomlDocumentService.MaxFileSizeBytes + 1)).ToArray());
        var service = CreateService();

        var result = await service.ReadAsync(file);

        Assert.True(result.Exists);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("大小", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_区分包含点号的QuotedKey与嵌套路径()
    {
        var file = WriteToml(
            "quoted.toml",
            """
            [root]
            "a.b" = "quoted"
            [root.a]
            b = "nested"
            """);
        var service = CreateService();

        var quoted = await service.GetAsync(file, new TomlPath(["root", "a.b"]));
        var nested = await service.GetAsync(file, new TomlPath(["root", "a", "b"]));

        Assert.True(quoted.Found);
        Assert.Equal(TomlValueKind.String, quoted.ValueType);
        Assert.Equal("quoted", quoted.Value?.Value);
        Assert.True(nested.Found);
        Assert.Equal("nested", nested.Value?.Value);
    }

    [Fact]
    public async Task GetAsync_按真实Segment查询DottedKey()
    {
        var file = WriteToml("dotted.toml", """
            model.providers.loomx = "ready"
            """);
        var service = CreateService();

        var result = await service.GetAsync(file, new TomlPath(["model", "providers", "loomx"]));

        Assert.True(result.Found);
        Assert.Equal(TomlValueKind.String, result.ValueType);
        Assert.Equal("ready", result.Value?.Value);
    }

    [Fact]
    public async Task GetAsync_读取数组与内联表()
    {
        var file = WriteToml(
            "values.toml",
            """
            ports = [8000, 8001]
            owner = { name = "张三", active = true }
            """);
        var service = CreateService();

        var portsResult = await service.GetAsync(file, new TomlPath(["ports"]));
        var ownerResult = await service.GetAsync(file, new TomlPath(["owner"]));

        var ports = Assert.IsAssignableFrom<IReadOnlyList<TomlValue>>(portsResult.Value?.Value);
        Assert.Equal([8000L, 8001L], ports.Select(item => (long)item.Value));
        var owner = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(ownerResult.Value?.Value);
        Assert.Equal("张三", owner["name"].Value);
        Assert.Equal(true, owner["active"].Value);
    }

    [Fact]
    public async Task GetAsync_读取数组表为对象数组()
    {
        var file = WriteToml(
            "servers.toml",
            """
            [[servers]]
            name = "one"
            [[servers]]
            name = "two"
            """);
        var service = CreateService();

        var result = await service.GetAsync(file, new TomlPath(["servers"]));

        Assert.True(result.Found);
        Assert.Equal(TomlValueKind.Array, result.ValueType);
        var servers = Assert.IsAssignableFrom<IReadOnlyList<TomlValue>>(result.Value?.Value);
        Assert.Equal(2, servers.Count);
        var first = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(servers[0].Value);
        var second = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(servers[1].Value);
        Assert.Equal("one", first["name"].Value);
        Assert.Equal("two", second["name"].Value);
    }

    [Fact]
    public async Task GetAsync_敏感路径返回固定脱敏值()
    {
        var file = WriteToml(
            "secret.toml",
            """
            [provider]
            api_key = "secret-value-123"
            """);
        var service = CreateService();

        var result = await service.GetAsync(file, new TomlPath(["provider", "api_key"]));

        Assert.True(result.Found);
        Assert.Equal(TomlValueKind.String, result.ValueType);
        Assert.Equal("***", result.Value?.Value);
        Assert.DoesNotContain("secret-value-123", result.Errors);
    }

    [Fact]
    public async Task GetAsync_不存在路径与空字符串值可区分()
    {
        var file = WriteToml("empty-value.toml", """
            name = ""
            """);
        var service = CreateService();

        var existing = await service.GetAsync(file, new TomlPath(["name"]));
        var missing = await service.GetAsync(file, new TomlPath(["missing"]));

        Assert.True(existing.Found);
        Assert.Equal(string.Empty, existing.Value?.Value);
        Assert.False(missing.Found);
        Assert.Null(missing.ValueType);
        Assert.Null(missing.Value);
    }

    [Fact]
    public async Task ValidateAsync_非法字符串返回一基行列安全摘要且不回显原文()
    {
        const string secret = "secret-value-123";
        var invalidContent = $"api_key = \"{secret}\n";
        var file = WriteToml("invalid.toml", invalidContent);
        var service = CreateService();

        var result = await service.ValidateAsync(file);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Line);
        Assert.NotNull(result.Column);
        Assert.True(result.Line >= 1);
        Assert.True(result.Column >= 1);
        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, error => Assert.DoesNotContain(secret, error, StringComparison.Ordinal));
        Assert.Equal(invalidContent, await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ReadGetValidate_对同一非法文档返回一致失败()
    {
        var file = WriteToml("same-parser.toml", """
            value = "unterminated
            """);
        var service = CreateService();

        var read = await service.ReadAsync(file);
        var get = await service.GetAsync(file, new TomlPath(["value"]));
        var validate = await service.ValidateAsync(file);

        Assert.False(read.IsValid);
        Assert.False(get.Found);
        Assert.False(validate.IsValid);
        Assert.Equal(read.Errors, get.Errors);
        Assert.Equal(read.Errors, validate.Errors);
    }

    [Fact]
    public async Task ReadGetValidate_接受严格Utf8无Bom与Utf8Bom()
    {
        var files = new[]
        {
            WriteBytes("utf8-no-bom.toml", new UTF8Encoding(false, true).GetBytes("name = \"loomx\"\n")),
            WriteBytes(
                "utf8-bom.toml",
                new UTF8Encoding(true, true).GetPreamble()
                    .Concat(new UTF8Encoding(false, true).GetBytes("name = \"loomx\"\n"))
                    .ToArray()),
        };
        var service = CreateService();

        foreach (var file in files)
        {
            var read = await service.ReadAsync(file);
            var get = await service.GetAsync(file, new TomlPath(["name"]));
            var validate = await service.ValidateAsync(file);

            Assert.True(read.IsValid);
            Assert.Equal("loomx", get.Value?.Value);
            Assert.True(validate.IsValid);
        }
    }

    [Fact]
    public async Task ReadGetValidate_一致拒绝Utf16Utf32与非法Utf8()
    {
        const string content = "name = \"loomx\"\n";
        var encodings = new (string FileName, byte[] Bytes)[]
        {
            ("utf16-le.toml", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(content)).ToArray()),
            ("utf16-be.toml", Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(content)).ToArray()),
            (
                "utf32-le.toml",
                new UTF32Encoding(false, true, true).GetPreamble()
                    .Concat(new UTF32Encoding(false, false, true).GetBytes(content))
                    .ToArray()),
            (
                "utf32-be.toml",
                new UTF32Encoding(true, true, true).GetPreamble()
                    .Concat(new UTF32Encoding(true, false, true).GetBytes(content))
                    .ToArray()),
            ("invalid-utf8.toml", [.. Encoding.ASCII.GetBytes("name = \""), 0xC3, 0x28, (byte)'\"', (byte)'\n']),
        };
        var service = CreateService();

        foreach (var encoding in encodings)
        {
            var file = WriteBytes(encoding.FileName, encoding.Bytes);

            var read = await service.ReadAsync(file);
            var get = await service.GetAsync(file, new TomlPath(["name"]));
            var validate = await service.ValidateAsync(file);

            Assert.False(read.IsValid);
            Assert.False(get.Found);
            Assert.False(validate.IsValid);
            Assert.Equal(read.Errors, get.Errors);
            Assert.Equal(read.Errors, validate.Errors);
            Assert.Contains(read.Errors, error => error.Contains("UTF-8", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task GetAsync_读取有限浮点数()
    {
        var file = WriteToml("float.toml", "temperature = 0.75\n");
        var service = CreateService();

        var result = await service.GetAsync(file, new TomlPath(["temperature"]));

        Assert.True(result.Found);
        Assert.Equal(TomlValueKind.Float, result.ValueType);
        Assert.Equal(0.75d, result.Value?.Value);
    }

    [Fact]
    public async Task GetAsync_查询父表时递归脱敏嵌套对象与数组表()
    {
        var file = WriteToml(
            "parent-redaction.toml",
            """
            [provider]
            name = "loomx"
            credentials = { api_key = "inline-secret", enabled = true }
            [[provider.accounts]]
            name = "first"
            token = "array-secret"
            [[provider.accounts]]
            name = "second"
            password = "password-secret"
            """);
        var service = CreateService();

        var result = await service.GetAsync(file, new TomlPath(["provider"]));

        var provider = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(result.Value?.Value);
        var credentials = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(provider["credentials"].Value);
        Assert.Equal("***", credentials["api_key"].Value);
        Assert.Equal(true, credentials["enabled"].Value);
        var accounts = Assert.IsAssignableFrom<IReadOnlyList<TomlValue>>(provider["accounts"].Value);
        var first = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(accounts[0].Value);
        var second = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(accounts[1].Value);
        Assert.Equal("***", first["token"].Value);
        Assert.Equal("***", second["password"].Value);
    }

    [Fact]
    public async Task 所有操作_预取消时抛出取消异常()
    {
        var file = WriteToml("pre-cancel.toml", "name = \"loomx\"\n");
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(file, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetAsync(file, new TomlPath(["name"]), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ValidateAsync(file, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PatchAsync(file, [], cancellation.Token));
    }

    [Fact]
    public async Task GetAsync_大数组处理期间响应取消()
    {
        var file = WriteToml(
            "large-array-cancel.toml",
            $"values = [{string.Join(',', Enumerable.Repeat("0", 180_000))}]\n");
        var service = CreateService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetAsync(file, new TomlPath(["values"]), cancellation.Token));
    }

    [Fact]
    public async Task ReadAsync_大量节点处理期间响应取消()
    {
        var content = new StringBuilder();
        for (var index = 0; index < 60_000; index++)
        {
            content.Append("key").Append(index.ToString("D5")).Append(" = 0\n");
        }

        var file = WriteToml("many-nodes-cancel.toml", content.ToString());
        var service = CreateService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(file, cancellation.Token));
    }

    [Fact]
    public async Task 成功日志包含结构化操作文件摘要与耗时且不含完整路径()
    {
        var file = WriteToml("logging.toml", """
            name = "loomx"
            """);
        var logger = new RecordingLogger<TomlDocumentService>();
        var service = CreateService(logger);

        await service.ReadAsync(file);

        var entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Information);
        Assert.Equal("Read", entry.Properties["Operation"]);
        Assert.Equal(Path.GetFileName(file), entry.Properties["FileSummary"]);
        Assert.IsType<long>(entry.Properties["ElapsedMs"]);
        Assert.DoesNotContain(Path.GetDirectoryName(file)!, entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 解析失败日志包含错误类型且不含原文敏感值与完整路径()
    {
        const string secret = "secret-value-123";
        var file = WriteToml("logging-invalid.toml", $"""
            api_key = "{secret}
            """);
        var logger = new RecordingLogger<TomlDocumentService>();
        var service = CreateService(logger);

        await service.ValidateAsync(file);

        var entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Warning);
        Assert.Equal("Validate", entry.Properties["Operation"]);
        Assert.Equal("TomlSyntaxError", entry.Properties["ErrorType"]);
        Assert.Equal("ParseDocument", entry.Properties["Stage"]);
        Assert.Equal(Path.GetFileName(file), entry.Properties["FileSummary"]);
        Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetDirectoryName(file)!, entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 文件读取失败日志阶段为ReadFile()
    {
        var file = Path.Combine(tempDirectory, "logging-missing.toml");
        var logger = new RecordingLogger<TomlDocumentService>();
        var service = CreateService(logger);

        await service.ReadAsync(file);

        var entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Warning);
        Assert.Equal("ReadFile", entry.Properties["Stage"]);
        Assert.Equal("FileNotFound", entry.Properties["ErrorType"]);
    }

    [Fact]
    public async Task 不支持值类型日志阶段为ConvertValue且不泄漏原值()
    {
        const string originalValue = "1979-05-27T07:32:00Z";
        var file = WriteToml("unsupported-value.toml", $"released_at = {originalValue}\n");
        var logger = new RecordingLogger<TomlDocumentService>();
        var service = CreateService(logger);

        var result = await service.GetAsync(file, new TomlPath(["released_at"]));

        Assert.True(result.Found);
        Assert.Null(result.Value);
        var entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Warning);
        Assert.Equal("ConvertValue", entry.Properties["Stage"]);
        Assert.Equal("UnsupportedTomlType", entry.Properties["ErrorType"]);
        Assert.DoesNotContain(originalValue, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            logger.Entries.SelectMany(item => item.Properties.Values).OfType<string>(),
            value => value.Contains(originalValue, StringComparison.Ordinal));
        Assert.All(result.Errors, error => Assert.DoesNotContain(originalValue, error, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PatchAsync_不存在文件时不创建目标备份或临时文件()
    {
        var file = Path.Combine(tempDirectory, "missing-patch.toml");
        var service = CreateService();
        var filesBefore = Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories);

        var result = await service.PatchAsync(file, []);

        Assert.False(result.Success);
        Assert.False(File.Exists(file));
        Assert.Null(result.BackupPath);
        Assert.Equal(filesBefore, Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PatchAsync_当前返回稳定未实现失败且不修改文件()
    {
        const string original = """
            model = "old"
            """;
        var file = WriteToml("patch.toml", original);
        var service = CreateService();
        var operations = new[]
        {
            new TomlPatchOperation(
                TomlPatchKind.Set,
                new TomlPath(["model"]),
                TomlValue.FromObject("new")),
        };

        var result = await service.PatchAsync(file, operations);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.False(result.FormattingChanged);
        Assert.Single(result.Errors);
        Assert.Contains("未实现", result.Errors[0], StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private TomlDocumentService CreateService(ILogger<TomlDocumentService>? logger = null) =>
        new(logger ?? new RecordingLogger<TomlDocumentService>());

    private string WriteToml(string fileName, string content)
    {
        var path = Path.Combine(tempDirectory, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private string WriteBytes(string fileName, byte[] content)
    {
        var path = Path.Combine(tempDirectory, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values
                    .Where(item => item.Key != "{OriginalFormat}")
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
