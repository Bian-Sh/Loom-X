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

    private static readonly string[] FeatureSatelliteCultures = ["en-US", "zh-TW"];
    private static readonly string[] ExistingSatelliteCultures = ["en-US", "zh-TW", "ja-JP"];
    private static readonly string[] UpdateExperienceKeys =
    [
        "update.entry.downloading", "update.entry.verifying", "update.entry.ready", "update.entry.error",
        "update.dialog.close", "update.dialog.later", "update.dialog.install", "update.dialog.retry",
        "update.progress.unknown", "update.progress.second", "update.progress.calculating",
        "update.status.idle", "update.status.checking", "update.status.downloading", "update.status.verifying",
        "update.status.ready", "update.status.installing", "update.status.latest", "update.status.error",
        "update.error.check", "update.error.prepare", "update.error.install", "release.notes.empty",
        "settings.update.history.title", "settings.update.history.refresh", "settings.update.history.loading",
        "settings.update.history.empty", "settings.update.history.error.load", "settings.update.history.retry",
        "settings.update.history.load.more", "settings.update.history.loading.more",
        "settings.update.history.badge.latest", "settings.update.history.badge.current"
    ];

    [Fact]
    public void UpdateExperienceResourcesExistInEveryCulture()
    {
        foreach (var fileName in new[] { "Strings.resx", "Strings.en-US.resx", "Strings.zh-TW.resx", "Strings.ja-JP.resx" })
        {
            var resources = Load(fileName);
            var missing = UpdateExperienceKeys.Where(key => !resources.ContainsKey(key)).ToArray();
            Assert.True(missing.Length == 0, $"Missing update resources in '{fileName}': {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void FeatureSatellitesContainExactlyTheZhCnKeys()
    {
        var zhCn = Load("Strings.resx");
        foreach (var culture in FeatureSatelliteCultures)
        {
            var satellite = Load($"Strings.{culture}.resx");
            Assert.Empty(zhCn.Keys.Except(satellite.Keys));
            Assert.Empty(satellite.Keys.Except(zhCn.Keys));
        }
    }

    [Fact]
    public void AllSatelliteValuesAreNotEmpty()
    {
        foreach (var culture in ExistingSatelliteCultures)
        {
            var empty = Load($"Strings.{culture}.resx")
                .Where(item => string.IsNullOrWhiteSpace(item.Value))
                .Select(item => item.Key)
                .ToArray();
            Assert.Empty(empty);
        }
    }

    [Fact]
    public void FeatureSatelliteFormatPlaceholdersMatchZhCn()
    {
        var zhCn = Load("Strings.resx");
        foreach (var culture in FeatureSatelliteCultures)
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

    [Theory]
    [InlineData("Strings.resx", "未启用任何模型")]
    [InlineData("Strings.zh-TW.resx", "尚未啟用任何模型")]
    [InlineData("Strings.en-US.resx", "No models are enabled")]
    public void Provider测试无启用模型提示已本地化(string fileName, string expected)
    {
        var resources = Load(fileName);

        Assert.Contains(expected, resources["providers.test.model.empty"], StringComparison.Ordinal);
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
