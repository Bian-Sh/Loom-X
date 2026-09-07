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

    private static readonly string[] SatelliteCultures = ["en-US", "zh-TW", "ja-JP"];

    [Fact]
    public void AllSatellitesContainExactlyTheZhCnKeys()
    {
        var zhCn = Load("Strings.resx");
        foreach (var culture in SatelliteCultures)
        {
            var satellite = Load($"Strings.{culture}.resx");
            Assert.Empty(zhCn.Keys.Except(satellite.Keys));
            Assert.Empty(satellite.Keys.Except(zhCn.Keys));
        }
    }

    [Fact]
    public void AllSatelliteValuesAreNotEmpty()
    {
        foreach (var culture in SatelliteCultures)
        {
            var empty = Load($"Strings.{culture}.resx")
                .Where(item => string.IsNullOrWhiteSpace(item.Value))
                .Select(item => item.Key)
                .ToArray();
            Assert.Empty(empty);
        }
    }

    [Fact]
    public void AllSatelliteFormatPlaceholdersMatchZhCn()
    {
        var zhCn = Load("Strings.resx");
        foreach (var culture in SatelliteCultures)
        {
            var satellite = Load($"Strings.{culture}.resx");
            foreach (var key in zhCn.Keys)
            {
                var zhPlaceholders = Placeholders(zhCn[key]);
                var satPlaceholders = Placeholders(satellite[key]);
                Assert.True(
                    zhPlaceholders.SequenceEqual(satPlaceholders),
                    $"Placeholder mismatch for '{key}' in '{culture}': " +
                    $"[zh-CN] [{string.Join(", ", zhPlaceholders)}] " +
                    $"[{culture}] [{string.Join(", ", satPlaceholders)}]");
            }
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
