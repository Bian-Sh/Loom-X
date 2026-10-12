using System.Text.Json;

namespace LoomX.Services;

/// <summary>模板仅依赖公共 SDK；自测试是开发工具约定，不增加 Manifest 字段。</summary>
internal static class PluginDevelopmentTemplate
{
    internal static IReadOnlyDictionary<string, string> Files(string id, string entryType, string manifest)
    {
        var last = entryType.LastIndexOf('.');
        var declaration = last < 0 ? "" : $"namespace {entryType[..last]};";
        var className = last < 0 ? entryType : entryType[(last + 1)..];
        var literal = JsonSerializer.Serialize(id);
        var entryDeclaration = className == "PluginTests" ? "public sealed partial class PluginTests" : $"public sealed class {className}";
        var testDeclaration = className == "PluginTests" ? "public sealed partial class PluginTests" : "public static class PluginTests";
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["plugin.manifest.json"] = manifest,
            ["src/Plugin.cs"] = $$"""
                using System.Collections.Generic;
                using LoomX.Plugins;
                {{declaration}}
                {{entryDeclaration}} : ILoomXPlugin
                {
                    public string Id => {{literal}};
                    public void Initialize(PluginInitializationContext context) { }
                    public IEnumerable<IPipelineExtension> CreateExtensions() => System.Array.Empty<IPipelineExtension>();
                }
                """,
            ["tests/PluginTests.cs"] = $$"""
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using LoomX.Plugins;
                {{declaration}}
                {{testDeclaration}}
                {
                    // 在临时 Bridge 上补充插件业务测试，不连接真实 Provider 或生产数据。
                    public static ValueTask RunAsync(PluginInitializationContext context, CancellationToken ct)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (context.PluginId != {{literal}}) throw new InvalidOperationException("测试身份不一致。");
                        return ValueTask.CompletedTask;
                    }
                }
                """,
        };
        foreach (var culture in new[] { "zh-CN", "zh-TW", "ja-JP", "en-US" })
            files[$"i18n/{culture}.json"] = "{}";
        return files;
    }
}
