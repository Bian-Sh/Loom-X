using System.Text.RegularExpressions;
using Xunit;

namespace LoomX.Tests;

public sealed class LocalizationNoCjkTest
{
    private static readonly Regex CjkPattern = new("[一-龥㐀-䶿]", RegexOptions.Compiled);

    private static string SolutionRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void Phase2LocalizedViewsAndViewModelsContainNoHardcodedCjk()
    {
        var files = Directory.EnumerateFiles(Path.Combine(SolutionRoot, "LoomX", "Views"), "*.axaml")
            .Concat([
                Path.Combine(SolutionRoot, "LoomX", "ViewModels", "GatewayViewModel.cs"),
                Path.Combine(SolutionRoot, "LoomX", "ViewModels", "MainWindowViewModel.cs")])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var offenders = new List<string>();

        foreach (var file in files)
        {
            var inXmlComment = false;
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.Contains("<!--", StringComparison.Ordinal)) inXmlComment = true;
                if (!inXmlComment && CjkPattern.IsMatch(line) && !IsCommentOrLog(line))
                    offenders.Add($"{Path.GetRelativePath(SolutionRoot, file)}:{index + 1}:{line.Trim()}");
                if (line.Contains("-->", StringComparison.Ordinal)) inXmlComment = false;
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsCommentOrLog(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || line.Contains("logger.", StringComparison.Ordinal)
            || Regex.IsMatch(line, @"\bLog(?:Trace|Debug|Information|Warning|Error|Critical)\b");
    }
}
