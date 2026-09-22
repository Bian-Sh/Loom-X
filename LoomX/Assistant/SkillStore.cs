using System.Text.Json;

namespace LoomX.Assistant;

public sealed record SkillManifest(string Category, string Name, string Description, string? WhenToUse);

public sealed record SkillDocument(SkillManifest Manifest, string Content, string FilePath);

/// <summary>
/// Skill 仓库：Skill = 知识 + 调用规则，不是插件。
/// 目录结构：&lt;root&gt;\&lt;category&gt;\&lt;name&gt;\manifest.json + SKILL.md
/// </summary>
public sealed class SkillStore
{
    private readonly string rootDirectory;

    public SkillStore(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
    }

    /// <summary>
    /// 默认安装目录下的 Skills 目录。
    /// </summary>
    public static SkillStore ForInstallDirectory() => new(Path.Combine(AppContext.BaseDirectory, "Skills"));

    /// <summary>
    /// 空 Skill 仓库（诊断等不需要 Skill 的场景）。
    /// </summary>
    public static SkillStore Empty() => new(Path.Combine(Path.GetTempPath(), "loomx-skills-empty"));

    public IReadOnlyList<SkillManifest> List()
    {
        var manifests = new List<SkillManifest>();
        if (!Directory.Exists(rootDirectory)) return manifests;

        foreach (var categoryDirectory in Directory.EnumerateDirectories(rootDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var category = Path.GetFileName(categoryDirectory);
            foreach (var skillDirectory in Directory.EnumerateDirectories(categoryDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var manifest = ReadManifest(category, Path.Combine(skillDirectory, "manifest.json"));
                if (manifest is not null) manifests.Add(manifest);
            }
        }
        return manifests;
    }

    public SkillDocument? Load(string category, string name)
    {
        var skillDirectory = Path.Combine(rootDirectory, category, name);
        var manifestPath = Path.Combine(skillDirectory, "manifest.json");
        var contentPath = Path.Combine(skillDirectory, "SKILL.md");
        var manifest = ReadManifest(category, manifestPath);
        if (manifest is null || !File.Exists(contentPath)) return null;
        return new SkillDocument(manifest, File.ReadAllText(contentPath), contentPath);
    }

    private static SkillManifest? ReadManifest(string category, string manifestPath)
    {
        if (!File.Exists(manifestPath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var description = root.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description)) return null;
            var whenToUse = root.TryGetProperty("when_to_use", out var whenElement) ? whenElement.GetString() : null;
            return new SkillManifest(category, name, description, whenToUse);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
