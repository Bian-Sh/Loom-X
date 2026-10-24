using System.Text.Json.Nodes;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// OpenCode 适配器：把 LoomX 写进 ~/.config/opencode/opencode.json。
/// 移植自 ai-switch OpenCodeAdapter：@ai-sdk/openai-compatible（Chat Completions）
/// 是 LoomX 为所有上游方言统一桥接的线格式；自定义 provider 必须自带模型列表，
/// 并把根级 model 指到 loomx/&lt;第一个模型&gt;，否则 provider 只"可用"而不会被选中。
/// </summary>
public sealed class OpenCodeConfigAdapter : IClientConfigAdapter
{
    public const string ProviderId = "loomx";
    private const string NpmPackage = "@ai-sdk/openai-compatible";
    private const string SchemaUrl = "https://opencode.ai/config.json";

    public ClientTargetDescriptor Descriptor { get; } =
        new("opencode", "OpenCode", RequiresModelList: true, RestartRequired: false, Platform: "opencode");

    public FileInfo ResolvePath(string homeRoot) =>
        new(Path.Combine(homeRoot, ".config", "opencode", "opencode.json"));

    public Task<ClientAdapterWriteResult> WriteAsync(
        FileInfo path,
        string? existing,
        ClientWriteInput input,
        CancellationToken cancellationToken)
    {
        var root = ClientConfigContent.ParseExistingObject(path.FullName, existing);

        if (root["$schema"] is null)
        {
            root["$schema"] = SchemaUrl;
        }

        var providers = ClientConfigContent.GetOrCreateObject(root, "provider", path.FullName);
        var existingEntry = providers[ProviderId] as JsonObject;
        var entry = ClientConfigContent.GetOrCreateObject(providers, ProviderId, path.FullName);
        entry["npm"] = NpmPackage;
        // 用户改过的名字保留；只给无名条目补默认名。
        entry["name"] = existingEntry?["name"]?.GetValue<string>() is { Length: > 0 } name ? name : "LoomX";

        // 深拷贝再改写：options 节点仍在原条目树上，直接挂回会报“已有父节点”。
        var options = (existingEntry?["options"] as JsonObject)?.DeepClone() ?? new JsonObject();
        options["baseURL"] = ClientConfigContent.BaseUrlWithV1(input.BaseUrl);
        // 内联 apiKey：OpenCode 的凭据存放在自己的 auth.json（按 provider id 键控），
        // 那份文件不属于本适配器。
        options["apiKey"] = input.ApiKey;
        entry["options"] = options;

        var models = new JsonObject();
        foreach (var model in input.Models)
        {
            models[model.Id] = new JsonObject
            {
                ["limit"] = new JsonObject
                {
                    ["context"] = model.ContextWindow,
                    ["output"] = model.MaxOutputTokens,
                },
            };
        }

        entry["models"] = models;

        if (input.Models.Count > 0)
        {
            root["model"] = $"{ProviderId}/{input.Models[0].Id}";
        }

        return Task.FromResult(new ClientAdapterWriteResult(ClientConfigContent.ToPrettyJson(root), null, true));
    }

    public ClientConfigInspection Inspect(FileInfo path, string? existing)
    {
        if (existing is null)
        {
            return new ClientConfigInspection("missing", Managed: false);
        }

        try
        {
            var root = ClientConfigContent.ParseExistingObject(path.FullName, existing);
            var managed = root["provider"]?[ProviderId] is JsonObject;
            return new ClientConfigInspection(managed ? "managed" : "unmanaged", managed);
        }
        catch (ClientConfigException)
        {
            return new ClientConfigInspection("invalid", Managed: false);
        }
    }
}
