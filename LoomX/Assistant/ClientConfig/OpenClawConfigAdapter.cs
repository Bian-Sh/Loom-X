using System.Text.Json.Nodes;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// OpenClaw 适配器：把 LoomX 写进 ~/.openclaw/openclaw.json。
/// 移植自 ai-switch OpenClawAdapter：provider 挂在 models.providers.&lt;id&gt;，
/// 通过 agents.defaults.model.primary = "loomx/&lt;第一个模型&gt;" 选中；
/// models.mode 只在缺省时补 "merge"（显式的 "replace" 是用户的主动选择，不回改）。
/// 文档声明 JSON5；只按严格 JSON 解析，无法解析时拒绝覆写而不是丢掉用户注释。
/// </summary>
public sealed class OpenClawConfigAdapter : IClientConfigAdapter
{
    public const string ProviderId = "loomx";
    private const string ApiProtocol = "openai-completions";

    public ClientTargetDescriptor Descriptor { get; } =
        new("openclaw", "OpenClaw", RequiresModelList: true, RestartRequired: false, Platform: "openclaw");

    public FileInfo ResolvePath(string homeRoot) =>
        new(Path.Combine(homeRoot, ".openclaw", "openclaw.json"));

    public Task<ClientAdapterWriteResult> WriteAsync(
        FileInfo path,
        string? existing,
        ClientWriteInput input,
        CancellationToken cancellationToken)
    {
        var root = ClientConfigContent.ParseExistingObject(path.FullName, existing);

        var models = ClientConfigContent.GetOrCreateObject(root, "models", path.FullName);
        if (models["mode"] is null)
        {
            models["mode"] = "merge";
        }

        var providers = ClientConfigContent.GetOrCreateObject(models, "providers", path.FullName);
        var entry = ClientConfigContent.GetOrCreateObject(providers, ProviderId, path.FullName);
        entry["baseUrl"] = ClientConfigContent.BaseUrlWithV1(input.BaseUrl);
        entry["apiKey"] = input.ApiKey;
        entry["api"] = ApiProtocol;
        entry["models"] = new JsonArray(input.Models.Select(model => (JsonNode?)new JsonObject
        {
            ["id"] = model.Id,
            // contextWindow 是原生窗口预算，maxTokens 是输出上限；省略窗口会退回平铺 200K。
            ["contextWindow"] = model.ContextWindow,
            ["maxTokens"] = model.MaxOutputTokens,
        }).ToArray());

        if (input.Models.Count > 0)
        {
            var agents = ClientConfigContent.GetOrCreateObject(root, "agents", path.FullName);
            var defaults = ClientConfigContent.GetOrCreateObject(agents, "defaults", path.FullName);
            var modelSection = ClientConfigContent.GetOrCreateObject(defaults, "model", path.FullName);
            modelSection["primary"] = $"{ProviderId}/{input.Models[0].Id}";
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
            var managed = root["models"]?["providers"]?[ProviderId] is JsonObject;
            return new ClientConfigInspection(managed ? "managed" : "unmanaged", managed);
        }
        catch (ClientConfigException)
        {
            return new ClientConfigInspection("invalid", Managed: false);
        }
    }
}
