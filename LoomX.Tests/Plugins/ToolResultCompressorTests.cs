using System.Text.Json.Nodes;
using LoomX.ToolResultCompression;
using Xunit;

namespace LoomX.Tests.Plugins;

public sealed class ToolResultCompressorTests
{
    private static readonly ToolResultCompressionOptions Options = ToolResultCompressionOptions.Default;

    [Fact]
    public void Compress_LeavesShortResultUntouched()
    {
        var text = string.Join('\n', Enumerable.Range(0, 5).Select(index => $"line {index}"));

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.False(result.Changed);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Compress_LeavesResultUntouchedWhenPluginDisabled()
    {
        var text = string.Join('\n', Enumerable.Repeat("INFO heartbeat", 200));

        var result = ToolResultCompressor.Compress(text, Options with { Enabled = false });

        Assert.False(result.Changed);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Compress_CollapsesRepeatedLinesIntoCount()
    {
        var text = string.Join('\n', Enumerable.Repeat("INFO  heartbeat ok", 200));

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.True(result.Changed);
        Assert.Contains("×200", result.Text, StringComparison.Ordinal);
        Assert.Contains(ToolResultCompressor.StrategyDedupe, result.Report.Strategies);
        Assert.True(result.Report.SavingsRatio > 0.9);
    }

    [Fact]
    public void Compress_CollapsesNonAdjacentRepeatedLines()
    {
        var noisy = new List<string>();
        for (var index = 0; index < 120; index++)
        {
            noisy.Add($"step-{index} processed");
            noisy.Add("WARN deprecated configuration detected");
        }

        var result = ToolResultCompressor.Compress(string.Join('\n', noisy), Options);

        Assert.True(result.Changed);
        Assert.Contains("共 120 次", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_TruncatesLongLinesWithOmittedCount()
    {
        var text = new string('a', 2000);

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.True(result.Changed);
        Assert.Contains("(+1600 字符)", result.Text, StringComparison.Ordinal);
        Assert.Contains(ToolResultCompressor.StrategyTruncateLongLines, result.Report.Strategies);
    }

    [Fact]
    public void Compress_KeepsHeadAndTailWhenTooManyLines()
    {
        var text = string.Join('\n', Enumerable.Range(0, 300).Select(index => $"entry {index} value"));

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.True(result.Changed);
        Assert.Contains("已省略 180 行", result.Text, StringComparison.Ordinal);
        Assert.Contains("entry 0 value", result.Text, StringComparison.Ordinal);
        Assert.Contains("entry 299 value", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_FoldsLargeJsonArrayAndKeepsValidJson()
    {
        var items = Enumerable.Range(0, 200)
            .Select(index => $"{{\"id\":{index},\"name\":\"item-{index}\",\"note\":\"{new string('x', 40)}\"}}");
        var text = $"[{string.Join(',', items)}]";

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.True(result.Changed);
        Assert.Contains(ToolResultCompressor.StrategyJsonFold, result.Report.Strategies);
        Assert.Contains("项已省略", result.Text, StringComparison.Ordinal);
        Assert.NotNull(JsonNode.Parse(result.Text));
    }

    [Fact]
    public void Compress_GroupsGitStatusByState()
    {
        var lines = new List<string> { "On branch main" };
        lines.AddRange(Enumerable.Range(0, 60).Select(index => $" M src/module-{index}/Service.cs"));
        lines.Add("?? docs/notes.md");
        lines.Add("A  src/Added.cs");
        lines.Add(" D src/Removed.cs");

        var result = ToolResultCompressor.Compress(string.Join('\n', lines), Options);

        Assert.True(result.Changed);
        Assert.Contains(ToolResultCompressor.StrategyGitStatus, result.Report.Strategies);
        Assert.Contains("[modified]", result.Text, StringComparison.Ordinal);
        Assert.Contains("[untracked]", result.Text, StringComparison.Ordinal);
        Assert.Contains("[deleted]", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_ReducesGitLogToHashAuthorAndTitle()
    {
        var lines = new List<string>();
        for (var index = 0; index < 12; index++)
        {
            lines.Add($"commit {index.ToString("x40", System.Globalization.CultureInfo.InvariantCulture)}");
            lines.Add("Author: Alice <alice@example.com>");
            lines.Add("Date:   Mon Jan 1 10:00:00 2026 +0800");
            lines.Add(string.Empty);
            lines.Add($"    fix login redirect #{index}");
            lines.Add(string.Empty);
        }

        var result = ToolResultCompressor.Compress(string.Join('\n', lines), Options);

        Assert.True(result.Changed);
        Assert.Contains(ToolResultCompressor.StrategyGitLog, result.Report.Strategies);
        Assert.Contains("fix login redirect #0", result.Text, StringComparison.Ordinal);
        Assert.Contains("Alice", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Date:", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_FoldsGitDiffContextLines()
    {
        var lines = new List<string>
        {
            "diff --git a/src/App.cs b/src/App.cs",
            "index 1234567..89abcde 100644",
            "--- a/src/App.cs",
            "+++ b/src/App.cs",
            "@@ -10,20 +10,21 @@",
        };
        lines.AddRange(Enumerable.Range(0, 45).Select(index =>
            $"     var configurationValue{index} = ComputeDefaultConfiguration({index});"));
        lines.Add("-    var removed = true;");
        lines.Add("+    var added = true;");

        var result = ToolResultCompressor.Compress(string.Join('\n', lines), Options);

        Assert.True(result.Changed);
        Assert.Contains(ToolResultCompressor.StrategyGitDiff, result.Report.Strategies);
        Assert.Contains("行上下文已省略", result.Text, StringComparison.Ordinal);
        Assert.Contains("+    var added = true;", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("index 1234567", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_KeepsOnlyFailuresForTestOutput()
    {
        var lines = new List<string>();
        for (var index = 0; index < 30; index++)
            lines.Add($"tests/unit/test_login_flow.py::test_case_{index} PASSED");
        lines.Add("tests/unit/test_login_flow.py::test_payment FAILED");
        lines.Add("E   AssertionError: expected 1 got 2");

        var result = ToolResultCompressor.Compress(string.Join('\n', lines), Options);

        Assert.True(result.Changed);
        Assert.Contains(ToolResultCompressor.StrategyTestOutput, result.Report.Strategies);
        Assert.Contains("通过 30 项", result.Text, StringComparison.Ordinal);
        Assert.Contains("AssertionError", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("test_case_17 PASSED", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_CompactsLongDirectoryListing()
    {
        var lines = new List<string> { "total 128" };
        lines.AddRange(Enumerable.Range(0, 12).Select(index =>
            $"drwxr-xr-x  4 user  staff    128 Jan  1 10:00 directory-{index}"));
        lines.AddRange(Enumerable.Range(0, 12).Select(index =>
            $"-rw-r--r--  1 user  staff   4096 Jan  1 10:00 file-{index}.txt"));

        var result = ToolResultCompressor.Compress(string.Join('\n', lines), Options);

        Assert.True(result.Changed);
        Assert.Contains(ToolResultCompressor.StrategyDirectoryListing, result.Report.Strategies);
        Assert.Contains("directory-0/", result.Text, StringComparison.Ordinal);
        Assert.Contains("file-0.txt", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("drwxr-xr-x", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_NeverLeavesBrokenCredentialPlaceholder()
    {
        // 占位符跨越长行截断边界：必须整体回退，不能留下半截 token。
        var text = new string('x', 390)
                   + "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}"
                   + new string('y', 2000);

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.True(result.Changed);
        Assert.DoesNotContain("{{LOOMX_CREDENTIAL", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_KeepsIntactCredentialPlaceholder()
    {
        var placeholder = "{{LOOMX_CREDENTIAL_ABCDEFGHIJKLMNOPQRST}}";
        var text = new string('x', 300) + placeholder + new string('y', 2000);

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.True(result.Changed);
        Assert.Contains(placeholder, result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_KeepsOriginalWhenSavingsAreTooSmall()
    {
        var text = string.Join('\n', Enumerable.Range(0, 60)
            .Select(index => $"unique diagnostic line {index} without repetition"));

        var result = ToolResultCompressor.Compress(text, Options);

        Assert.False(result.Changed);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void Compress_RemovesAnsiNoise()
    {
        var body = string.Join('\n', Enumerable.Range(0, 60)
            .Select(index => $"\u001B[32mINFO\u001B[0m  worker-{index} started and reported ready state"));

        var result = ToolResultCompressor.Compress(body, Options);

        Assert.True(result.Changed);
        Assert.DoesNotContain("\u001B[", result.Text, StringComparison.Ordinal);
    }
}
