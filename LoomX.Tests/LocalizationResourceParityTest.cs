using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LoomX.Tests;

public sealed class LocalizationResourceParityTest
{
    private static string ResourceDirectory => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
        "LoomX",
        "Resources");

    [Fact]
    public void EnUsContainsExactlyTheZhCnKeys()
    {
        var zhCn = Load("Strings.resx");
        var enUs = Load("Strings.en-US.resx");
        Assert.Empty(zhCn.Keys.Except(enUs.Keys));
        Assert.Empty(enUs.Keys.Except(zhCn.Keys));
    }

    [Fact]
    public void EnUsValuesAreNotEmpty()
    {
        var empty = Load("Strings.en-US.resx")
            .Where(item => string.IsNullOrWhiteSpace(item.Value))
            .Select(item => item.Key)
            .ToArray();
        Assert.Empty(empty);
    }

    [Fact]
    public void EnUsFormatPlaceholdersMatchZhCn()
    {
        var zhCn = Load("Strings.resx");
        var enUs = Load("Strings.en-US.resx");

        foreach (var key in zhCn.Keys)
        {
            var zhPlaceholders = Placeholders(zhCn[key]);
            var enPlaceholders = Placeholders(enUs[key]);
            Assert.Equal(zhPlaceholders, enPlaceholders);
        }
    }

    private static string[] Placeholders(string value) =>
        Regex.Matches(value, @"\{\d+(?:[^}]*)\}")
            .Select(match => match.Value)
            .ToArray();

    private static Dictionary<string, string> Load(string fileName)
    {
        var path = Path.Combine(ResourceDirectory, fileName);
        Assert.True(File.Exists(path), $"Missing resource file: {path}");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? string.Empty,
                StringComparer.Ordinal);
    }
}
