using System.Text.Json.Nodes;

namespace LoomX.Assistant.ClientConfig;

/// <summary>
/// settings.json 型客户端适配器（Claude Code / Gemini CLI / Grok）。
/// 移植自 ai-switch JsonAgentAdapter：改写 env 里的平台 base-url / 鉴权键，
/// 并在 `loomx.gateway` 下留托管标记，便于识别与原地更新。
/// 鉴权键只写经过验证的键（目前只有 Claude 的 ANTHROPIC_AUTH_TOKEN），
/// 不为未验证的 CLI 猜测环境变量名。
/// </summary>
public sealed class JsonAgentConfigAdapter(
    string targetKey,
    string displayName,
    string platform,
    string configDir,
    IReadOnlyList<string> baseUrlKeys,
    IReadOnlyList<string> authTokenKeys) : IClientConfigAdapter
{
    public static JsonAgentConfigAdapter ClaudeCode() => new(
        "claude-code", "Claude Code", "claude", ".claude",
        ["ANTHROPIC_BASE_URL"], ["ANTHROPIC_AUTH_TOKEN"]);

    public static JsonAgentConfigAdapter GeminiCli() => new(
        "gemini-cli", "Gemini CLI", "gemini", ".gemini",
        ["GEMINI_API_BASE_URL", "GOOGLE_GEMINI_BASE_URL"], []);

    public static JsonAgentConfigAdapter Grok() => new(
        "grok", "Grok", "grok", ".grok",
        ["XAI_API_BASE_URL", "GROK_API_BASE_URL"], []);

    public ClientTargetDescriptor Descriptor { get; } =
        new(targetKey, displayName, RequiresModelList: false, RestartRequired: false, platform);

    public FileInfo ResolvePath(string homeRoot) => new(Path.Combine(homeRoot, configDir, "settings.json"));

    public Task<ClientAdapterWriteResult> WriteAsync(
        FileInfo path,
        string? existing,
        ClientWriteInput input,
        CancellationToken cancellationToken)
    {
        var root = ClientConfigContent.ParseExistingObject(path.FullName, existing);

        var loomx = ClientConfigContent.GetOrCreateObject(root, "loomx", path.FullName);
        var gateway = ClientConfigContent.GetOrCreateObject(loomx, "gateway", path.FullName);
        gateway["enabled"] = true;
        gateway["baseUrl"] = ClientConfigContent.BaseUrlRoot(input.BaseUrl);
        gateway["platform"] = Descriptor.Platform;
        gateway["apiKey"] = input.ApiKey;

        var env = ClientConfigContent.GetOrCreateObject(root, "env", path.FullName);
        var baseUrl = ClientConfigContent.BaseUrlRoot(input.BaseUrl);
        foreach (var key in baseUrlKeys)
        {
            env[key] = baseUrl;
        }

        foreach (var key in authTokenKeys)
        {
            env[key] = input.ApiKey;
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
            var managed = root["loomx"] is JsonObject loomx
                && loomx["gateway"] is JsonObject gateway
                && gateway["enabled"]?.GetValue<bool>() == true;
            return new ClientConfigInspection(managed ? "managed" : "unmanaged", managed);
        }
        catch (ClientConfigException)
        {
            return new ClientConfigInspection("invalid", Managed: false);
        }
    }
}
