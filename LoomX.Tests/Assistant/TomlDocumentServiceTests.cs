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
    public async Task PatchAsync_新文件在中文空格目录写入且不创建备份或残留临时文件()
    {
        var directory = Path.Combine(tempDirectory, "新 配置");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "新 文件.toml");
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [Set(["provider", "name"], "新模型")]);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.True(File.Exists(file));
        var value = await service.GetAsync(file, new TomlPath(["provider", "name"]));
        Assert.Equal("新模型", value.Value?.Value);
        Assert.Empty(Directory.GetFiles(directory, "*.bak"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task PatchAsync_Set更新目标并保留注释未知节与无关字段()
    {
        const string original = """
            # keep comment
            [provider]
            name  = 'demo' # keep tail
            enabled = true

            [unknown]
            untouched = "保留"
            """;
        var file = WriteToml("patch-set.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [Set(["provider", "name"], "changed")]);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(Path.GetDirectoryName(file), Path.GetDirectoryName(result.BackupPath));
        Assert.Equal(original, await File.ReadAllTextAsync(result.BackupPath));
        var updated = await File.ReadAllTextAsync(file);
        Assert.Contains("# keep comment", updated, StringComparison.Ordinal);
        Assert.Contains("# keep tail", updated, StringComparison.Ordinal);
        Assert.Contains("enabled = true", updated, StringComparison.Ordinal);
        Assert.Contains("[unknown]", updated, StringComparison.Ordinal);
        Assert.Contains("untouched = \"保留\"", updated, StringComparison.Ordinal);
        var value = await service.GetAsync(file, new TomlPath(["provider", "name"]));
        Assert.Equal("changed", value.Value?.Value);
    }

    [Fact]
    public async Task PatchAsync_Delete只删除目标并保留空父表()
    {
        const string original = """
            [provider]
            name = "demo"

            [provider.empty]
            remove_me = true

            [other]
            value = 1
            """;
        var file = WriteToml("patch-delete.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [Delete(["provider", "empty", "remove_me"])]);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        var updated = await File.ReadAllTextAsync(file);
        Assert.Contains("[provider.empty]", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("remove_me", updated, StringComparison.Ordinal);
        Assert.Contains("[other]", updated, StringComparison.Ordinal);
        Assert.Contains("value = 1", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatchAsync_Set为缺失路径创建普通父表()
    {
        var file = WriteToml("patch-create-parent.toml", "title = \"demo\"\n");
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [Set(["provider", "options", "timeout"], 30L)]);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        var updated = await File.ReadAllTextAsync(file);
        Assert.Contains(
            updated.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith('[')
                && line.Contains("provider", StringComparison.Ordinal)
                && line.Contains("options", StringComparison.Ordinal));
        var value = await service.GetAsync(file, new TomlPath(["provider", "options", "timeout"]));
        Assert.Equal(30L, value.Value?.Value);
    }

    [Fact]
    public async Task PatchAsync_Set与Delete可穿越内联表并保留同级字段()
    {
        const string original = "owner = { name = \"old\", enabled = true } # keep\n";
        var file = WriteToml("patch-inline-table.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [
                Set(["owner", "name"], "new"),
                Set(["owner", "region"], "cn"),
                Delete(["owner", "enabled"]),
            ]);

        Assert.True(result.Success);
        var ownerResult = await service.GetAsync(file, new TomlPath(["owner"]));
        var owner = Assert.IsAssignableFrom<IReadOnlyDictionary<string, TomlValue>>(ownerResult.Value?.Value);
        Assert.Equal("new", owner["name"].Value);
        Assert.Equal("cn", owner["region"].Value);
        Assert.DoesNotContain("enabled", owner.Keys);
        Assert.Contains("# keep", await File.ReadAllTextAsync(file), StringComparison.Ordinal);
    }
    [Fact]
    public async Task PatchAsync_Set拒绝穿越标量且不落盘()
    {
        const string original = "provider = \"scalar\"\n";
        var file = WriteToml("patch-scalar.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [Set(["provider", "name"], "invalid")]);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains(result.Errors, error => error.Contains("标量", StringComparison.Ordinal));
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.bak"));
    }

    [Fact]
    public async Task PatchAsync_Set拒绝穿越数组表且不落盘()
    {
        const string original = """
            [[servers]]
            name = "one"
            """;
        var file = WriteToml("patch-array-table.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [Set(["servers", "name"], "invalid")]);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains(result.Errors, error => error.Contains("数组表", StringComparison.Ordinal));
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.bak"));
    }

    [Fact]
    public async Task PatchAsync_批量第二项非法时整批不落盘()
    {
        const string original = """
            # keep
            [provider]
            name = "demo"
            """;
        var file = WriteToml("patch-batch.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(
            file,
            [
                Set(["provider", "name"], "changed"),
                Set(["provider", "name", "child"], "invalid"),
            ]);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Contains(result.Errors, error => error.Contains("第 2 项", StringComparison.Ordinal));
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.bak"));
    }

    [Fact]
    public async Task PatchAsync_Set同值时NoOp且不创建备份()
    {
        const string original = "name  = 'demo' # keep formatting\n";
        var file = WriteToml("patch-no-op.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(file, [Set(["name"], "demo")]);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.False(result.FormattingChanged);
        Assert.Empty(result.Errors);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.bak"));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp"));
    }

    [Fact]
    public async Task PatchAsync_Delete缺失目标时NoOp且不创建备份()
    {
        const string original = "[provider]\nname = \"demo\"\n";
        var file = WriteToml("patch-delete-missing.toml", original);
        var service = CreateService();

        var result = await service.PatchAsync(file, [Delete(["provider", "missing"])]);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.bak"));
    }

    [Fact]
    public async Task PatchAsync_NoOp不调用任何文件事务操作()
    {
        const string original = "name = \"demo\"\n";
        var file = WriteToml("patch-no-op-operations.toml", original);
        var fileOperations = new FakeTomlFileOperations();
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "demo")]);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Equal(0, fileOperations.TotalCalls);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task PatchAsync_临时写入失败时保留原文件与备份并清理临时文件()
    {
        const string original = "name = \"old\"\n";
        var file = WriteToml("patch-temp-write-failure.toml", original);
        var fileOperations = new FakeTomlFileOperations
        {
            WriteAction = (_, _, _, _) => throw new IOException("临时写入失败"),
        };
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "new")]);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Equal(original, await File.ReadAllTextAsync(result.BackupPath));
        Assert.Equal(1, fileOperations.WriteCalls);
        Assert.Equal(0, fileOperations.ReplaceCalls);
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PatchAsync_临时验证失败时不替换目标并清理临时文件()
    {
        const string original = "name = \"old\"\n";
        var file = WriteToml("patch-temp-validation-failure.toml", original);
        var fileOperations = new FakeTomlFileOperations
        {
            WriteAction = (path, _, encoding, cancellationToken) =>
                File.WriteAllTextAsync(path, "broken = \"", encoding, cancellationToken),
        };
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "new")]);

        Assert.False(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Equal(0, fileOperations.ReplaceCalls);
        Assert.Contains(result.Errors, error => error.Contains("临时", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PatchAsync_Replace两次IOException后重试成功()
    {
        var file = WriteToml("patch-replace-retry.toml", "name = \"old\"\n");
        var fileOperations = new FakeTomlFileOperations
        {
            ReplaceAction = (source, destination) =>
            {
                if (fileOperationsPlaceholder.ReplaceCalls < 3)
                {
                    throw new IOException("文件暂时被占用");
                }

                File.Replace(source, destination, destinationBackupFileName: null);
            },
        };
        fileOperationsPlaceholder = fileOperations;
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "new")]);

        Assert.True(result.Success);
        Assert.Equal(3, fileOperations.ReplaceCalls);
        Assert.Equal(2, fileOperations.DelayCalls);
        Assert.Equal("new", (await service.GetAsync(file, new TomlPath(["name"]))).Value?.Value);
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PatchAsync_Move一次UnauthorizedAccess后重试成功()
    {
        var file = Path.Combine(tempDirectory, "patch-move-retry.toml");
        var fileOperations = new FakeTomlFileOperations
        {
            MoveAction = (source, destination) =>
            {
                if (fileOperationsPlaceholder.MoveCalls < 2)
                {
                    throw new UnauthorizedAccessException("文件暂时不可移动");
                }

                File.Move(source, destination);
            },
        };
        fileOperationsPlaceholder = fileOperations;
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "new")]);

        Assert.True(result.Success);
        Assert.Null(result.BackupPath);
        Assert.Equal(2, fileOperations.MoveCalls);
        Assert.Equal(1, fileOperations.DelayCalls);
        Assert.True(File.Exists(file));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PatchAsync_Replace最终失败时保留原文件与可恢复备份()
    {
        const string original = "name = \"old\"\n";
        var file = WriteToml("patch-replace-final-failure.toml", original);
        var fileOperations = new FakeTomlFileOperations
        {
            ReplaceAction = (_, _) => throw new IOException("持续占用"),
        };
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "new")]);

        Assert.False(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(3, fileOperations.ReplaceCalls);
        Assert.Equal(2, fileOperations.DelayCalls);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Equal(original, await File.ReadAllTextAsync(result.BackupPath));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PatchAsync_写后目标验证失败时从备份恢复原文件()
    {
        const string original = "name = \"old\"\n";
        var file = WriteToml("patch-target-validation-restore.toml", original);
        var fileOperations = new FakeTomlFileOperations
        {
            ReplaceAction = (source, destination) =>
            {
                File.Replace(source, destination, destinationBackupFileName: null);
                File.WriteAllText(destination, "broken = \"", new UTF8Encoding(false));
            },
        };
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "new")]);

        Assert.False(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(2, fileOperations.CopyCalls);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Equal(original, await File.ReadAllTextAsync(result.BackupPath));
        Assert.Contains(result.Errors, error => error.Contains("恢复", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PatchAsync_写后验证失败且恢复失败时保留备份并记录安全错误()
    {
        const string secret = "secret-patch-value-123";
        const string original = "api_key = \"old\"\n";
        var file = WriteToml("patch-restore-failure.toml", original);
        var logger = new RecordingLogger<TomlDocumentService>();
        var fileOperations = new FakeTomlFileOperations
        {
            CopyAction = (source, destination, overwrite) =>
            {
                if (fileOperationsPlaceholder.CopyCalls > 1)
                {
                    throw new UnauthorizedAccessException("恢复失败");
                }

                File.Copy(source, destination, overwrite);
            },
            ReplaceAction = (source, destination) =>
            {
                File.Replace(source, destination, destinationBackupFileName: null);
                File.WriteAllText(destination, "broken = \"", new UTF8Encoding(false));
            },
        };
        fileOperationsPlaceholder = fileOperations;
        var service = CreateService(logger, fileOperations);

        var result = await service.PatchAsync(file, [Set(["api_key"], secret)]);

        Assert.False(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.NotEqual(original, await File.ReadAllTextAsync(file));
        Assert.Contains(result.Errors, error => error.Contains("恢复失败", StringComparison.Ordinal));
        var entry = Assert.Single(logger.Entries, item => item.Properties.GetValueOrDefault("Stage") as string == "Restore");
        Assert.IsType<UnauthorizedAccessException>(entry.Exception);
        Assert.DoesNotContain(secret, string.Join('|', result.Errors), StringComparison.Ordinal);
        Assert.All(
            logger.Entries,
            logEntry =>
            {
                Assert.DoesNotContain(secret, logEntry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(original, logEntry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    Path.GetDirectoryName(file)!,
                    logEntry.Message,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    logEntry.Properties.Values.OfType<string>(),
                    value => value.Contains(secret, StringComparison.Ordinal)
                        || value.Contains(original, StringComparison.Ordinal)
                        || value.Contains(Path.GetDirectoryName(file)!, StringComparison.OrdinalIgnoreCase));
            });
    }

    [Fact]
    public async Task PatchAsync_重试延迟期间取消时传播取消异常()
    {
        const string original = "name = \"old\"\n";
        var file = WriteToml("patch-retry-cancel.toml", original);
        using var cancellation = new CancellationTokenSource();
        var fileOperations = new FakeTomlFileOperations
        {
            ReplaceAction = (_, _) => throw new IOException("暂时占用"),
            DelayAction = (_, cancellationToken) =>
            {
                cancellation.Cancel();
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
        };
        var service = CreateService(fileOperations: fileOperations);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.PatchAsync(file, [Set(["name"], "new")], cancellation.Token));

        Assert.Equal(1, fileOperations.ReplaceCalls);
        Assert.Equal(1, fileOperations.DelayCalls);
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PatchAsync_备份和临时文件始终位于目标同目录且成功后仅保留备份()
    {
        var directory = Path.Combine(tempDirectory, "事务 路径");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "配置.toml");
        File.WriteAllText(file, "name = \"old\"\n", new UTF8Encoding(false));
        var fileOperations = new FakeTomlFileOperations();
        var service = CreateService(fileOperations: fileOperations);

        var result = await service.PatchAsync(file, [Set(["name"], "new")]);

        Assert.True(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(directory, Path.GetDirectoryName(result.BackupPath));
        Assert.All(fileOperations.WrittenPaths, path => Assert.Equal(directory, Path.GetDirectoryName(path)));
        Assert.True(File.Exists(result.BackupPath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static TomlPatchOperation Set(IReadOnlyList<string> path, object value) =>
        new(TomlPatchKind.Set, new TomlPath(path), TomlValue.FromObject(value));

    private static TomlPatchOperation Delete(IReadOnlyList<string> path) =>
        new(TomlPatchKind.Delete, new TomlPath(path));

    private TomlDocumentService CreateService(
        ILogger<TomlDocumentService>? logger = null,
        ITomlFileOperations? fileOperations = null) =>
        fileOperations is null
            ? new TomlDocumentService(logger ?? new RecordingLogger<TomlDocumentService>())
            : new TomlDocumentService(logger ?? new RecordingLogger<TomlDocumentService>(), fileOperations);

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

    private FakeTomlFileOperations fileOperationsPlaceholder = null!;

    private sealed class FakeTomlFileOperations : ITomlFileOperations
    {
        public Action<string, string, bool>? CopyAction { get; init; }

        public Func<string, string, Encoding, CancellationToken, Task>? WriteAction { get; init; }

        public Action<string, string>? ReplaceAction { get; init; }

        public Action<string, string>? MoveAction { get; init; }

        public Action<string>? DeleteAction { get; init; }

        public Func<TimeSpan, CancellationToken, Task>? DelayAction { get; init; }

        public int CopyCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public int ReplaceCalls { get; private set; }

        public int MoveCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public int DelayCalls { get; private set; }

        public int TotalCalls => CopyCalls + WriteCalls + ReplaceCalls + MoveCalls + DeleteCalls + DelayCalls;

        public List<string> WrittenPaths { get; } = [];

        public void Copy(string sourcePath, string destinationPath, bool overwrite)
        {
            CopyCalls++;
            if (CopyAction is not null)
            {
                CopyAction(sourcePath, destinationPath, overwrite);
                return;
            }

            File.Copy(sourcePath, destinationPath, overwrite);
        }

        public Task WriteAllTextAsync(
            string path,
            string content,
            Encoding encoding,
            CancellationToken cancellationToken)
        {
            WriteCalls++;
            WrittenPaths.Add(path);
            return WriteAction is null
                ? File.WriteAllTextAsync(path, content, encoding, cancellationToken)
                : WriteAction(path, content, encoding, cancellationToken);
        }

        public void Replace(string sourcePath, string destinationPath)
        {
            ReplaceCalls++;
            if (ReplaceAction is not null)
            {
                ReplaceAction(sourcePath, destinationPath);
                return;
            }

            File.Replace(sourcePath, destinationPath, destinationBackupFileName: null);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            MoveCalls++;
            if (MoveAction is not null)
            {
                MoveAction(sourcePath, destinationPath);
                return;
            }

            File.Move(sourcePath, destinationPath);
        }

        public void Delete(string path)
        {
            DeleteCalls++;
            if (DeleteAction is not null)
            {
                DeleteAction(path);
                return;
            }

            File.Delete(path);
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCalls++;
            return DelayAction is null
                ? Task.CompletedTask
                : DelayAction(delay, cancellationToken);
        }
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
