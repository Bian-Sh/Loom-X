using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OllamaHub.Configuration;

public sealed record ProviderInput(string BusinessId, string DisplayName, string BaseUrl, string ApiMode, bool Enabled, string? ApiKey, bool ClearApiKey, Dictionary<string, string>? Headers, bool UseProxy = false, string? ModelListUrl = null);
public sealed record AppSettingsInput(string Language, string Theme, bool OpenControlCenterOnStartup, string ProxyMode, string ProxyHost, int ProxyPort, string? ProxyUsername, string? ProxyPassword, bool ClearProxyPassword, bool AutoCheckUpdates, string UpdateChannel, bool DiagnosticsEnabled, int LogRetentionDays);
public sealed record AppSettingsResponse(int Id, string Language, string Theme, bool OpenControlCenterOnStartup, string ProxyMode, string ProxyHost, int ProxyPort, string? ProxyUsername, bool HasProxyPassword, bool AutoCheckUpdates, string UpdateChannel, bool DiagnosticsEnabled, int LogRetentionDays);
public sealed record ModelInput(string ModelId, string DisplayName, string? ConfigId, string Family, string? BaseUrl, string? ApiMode, int ContextLength, int MaxTokens, bool Vision, double? Temperature, double? TopP, bool Enabled, string? ApiKey, bool ClearApiKey, Dictionary<string, string>? Headers, Dictionary<string, JsonElement>? Extra);
public sealed record ProviderResponse(Guid Id, string BusinessId, string DisplayName, string BaseUrl, string ApiMode, bool Enabled, bool UseProxy, bool HasApiKey, int ModelCount, string HeadersJson, IReadOnlyList<ModelResponse> Models, string? ModelListUrl = null);
public sealed record ModelResponse(Guid Id, string ProviderId, string ModelId, string DisplayName, string? ConfigId, string Family, string? BaseUrl, string? ApiMode, int ContextLength, int MaxTokens, bool Vision, double? Temperature, double? TopP, bool Enabled, bool HasApiKey, string HeadersJson, string ExtraJson);

public sealed class ConfigurationManagementService(IDbContextFactory<ConfigurationDbContext> dbContextFactory, IDatabaseConfigurationProvider configurationProvider)
{
    public async Task<IReadOnlyList<ProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var providers = await db.Providers.AsNoTracking().Include(provider => provider.Models).OrderBy(provider => provider.SortOrder).ToListAsync(cancellationToken);
        return providers.Select(ToResponse).ToArray();
    }

    public async Task<AppSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.AppSettings.AsNoTracking().SingleAsync(cancellationToken);
        return ToResponse(settings);
    }

    public async Task<AppSettingsResponse> UpdateSettingsAsync(AppSettingsInput input, CancellationToken cancellationToken = default)
    {
        ValidateSettings(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.AppSettings.SingleAsync(cancellationToken);
        settings.Language = input.Language.Trim();
        settings.Theme = NormalizeTheme(input.Theme);
        settings.OpenControlCenterOnStartup = input.OpenControlCenterOnStartup;
        settings.ProxyMode = NormalizeProxyMode(input.ProxyMode);
        settings.ProxyHost = input.ProxyHost.Trim();
        settings.ProxyPort = input.ProxyPort;
        settings.ProxyUsername = string.IsNullOrWhiteSpace(input.ProxyUsername) ? null : input.ProxyUsername.Trim();
        if (input.ClearProxyPassword) settings.ProtectedProxyPassword = null;
        else if (!string.IsNullOrWhiteSpace(input.ProxyPassword)) settings.ProtectedProxyPassword = ProtectedApiKeyStore.Protect(input.ProxyPassword.Trim());
        settings.AutoCheckUpdates = input.AutoCheckUpdates;
        settings.UpdateChannel = NormalizeUpdateChannel(input.UpdateChannel);
        settings.DiagnosticsEnabled = input.DiagnosticsEnabled;
        settings.LogRetentionDays = input.LogRetentionDays;
        await db.SaveChangesAsync(cancellationToken);
        await configurationProvider.ReloadAsync(cancellationToken);
        return ToResponse(settings);
    }

    public async Task<ProviderResponse> CreateProviderAsync(ProviderInput input, CancellationToken cancellationToken = default)
    {
        ValidateProvider(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Providers.AnyAsync(provider => provider.BusinessId == input.BusinessId.Trim(), cancellationToken)) throw new InvalidOperationException("Provider ID 已存在。");
        var provider = new ProviderEntity { BusinessId = input.BusinessId.Trim(), DisplayName = input.DisplayName.Trim(), BaseUrl = NormalizeUrl(input.BaseUrl), ModelListUrl = NormalizeOptionalUrl(input.ModelListUrl), ApiMode = NormalizeProviderMode(input.ApiMode), Enabled = input.Enabled, UseProxy = input.UseProxy, HeadersJson = Serialize(input.Headers) };
        provider.ProtectedApiKey = ProtectApiKey(input.ApiKey);
        db.Providers.Add(provider);
        await db.SaveChangesAsync(cancellationToken);
        await configurationProvider.ReloadAsync(cancellationToken);
        return ToResponse(provider);
    }

    public async Task<ProviderResponse> UpdateProviderAsync(Guid id, ProviderInput input, CancellationToken cancellationToken = default)
    {
        ValidateProvider(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var provider = await db.Providers.Include(item => item.Models).SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Provider 不存在。");
        if (await db.Providers.AnyAsync(item => item.Id != id && item.BusinessId == input.BusinessId.Trim(), cancellationToken)) throw new InvalidOperationException("Provider ID 已存在。");
        provider.BusinessId = input.BusinessId.Trim(); provider.DisplayName = input.DisplayName.Trim(); provider.BaseUrl = NormalizeUrl(input.BaseUrl); provider.ModelListUrl = NormalizeOptionalUrl(input.ModelListUrl); provider.ApiMode = NormalizeProviderMode(input.ApiMode); provider.Enabled = input.Enabled; provider.UseProxy = input.UseProxy; provider.HeadersJson = Serialize(input.Headers);
        ApplyApiKey(provider, input.ApiKey, input.ClearApiKey);
        await db.SaveChangesAsync(cancellationToken);
        await configurationProvider.ReloadAsync(cancellationToken);
        return ToResponse(provider);
    }

    public async Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var provider = await db.Providers.Include(item => item.Models).SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Provider 不存在。");
        if (provider.Models.Count > 0) throw new InvalidOperationException("Provider 仍被模型引用，请先删除模型。");
        db.Providers.Remove(provider); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken);
    }

    public async Task<ModelResponse> CreateModelAsync(Guid providerId, ModelInput input, CancellationToken cancellationToken = default)
    {
        ValidateModel(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var provider = await db.Providers.SingleOrDefaultAsync(item => item.Id == providerId, cancellationToken) ?? throw new KeyNotFoundException("Provider 不存在。");
        if (await db.Models.AnyAsync(model => model.ProviderId == providerId && model.ModelId == input.ModelId.Trim(), cancellationToken)) throw new InvalidOperationException("Model ID 已存在。");
        var model = new ModelEntity { ProviderId = providerId, ModelId = input.ModelId.Trim(), DisplayName = input.DisplayName.Trim(), ConfigId = input.ConfigId?.Trim(), Family = input.Family.Trim(), BaseUrl = NormalizeOptionalUrl(input.BaseUrl), ApiMode = NormalizeOptionalModes(input.ApiMode), ContextLength = input.ContextLength, MaxTokens = input.MaxTokens, Vision = input.Vision, Temperature = input.Temperature, TopP = input.TopP, Enabled = input.Enabled, HeadersJson = Serialize(input.Headers), ExtraJson = Serialize(input.Extra) };
        model.ProtectedApiKey = ProtectApiKey(input.ApiKey); db.Models.Add(model); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken); return ToResponse(provider, model);
    }

    public async Task<ModelResponse> UpdateModelAsync(Guid id, ModelInput input, CancellationToken cancellationToken = default)
    {
        ValidateModel(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.Models.Include(item => item.Provider).SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Model 不存在。");
        if (await db.Models.AnyAsync(item => item.Id != id && item.ProviderId == model.ProviderId && item.ModelId == input.ModelId.Trim(), cancellationToken)) throw new InvalidOperationException("Model ID 已存在。");
        model.ModelId = input.ModelId.Trim(); model.DisplayName = input.DisplayName.Trim(); model.ConfigId = input.ConfigId?.Trim(); model.Family = input.Family.Trim(); model.BaseUrl = NormalizeOptionalUrl(input.BaseUrl); model.ApiMode = NormalizeOptionalModes(input.ApiMode); model.ContextLength = input.ContextLength; model.MaxTokens = input.MaxTokens; model.Vision = input.Vision; model.Temperature = input.Temperature; model.TopP = input.TopP; model.Enabled = input.Enabled; model.HeadersJson = Serialize(input.Headers); model.ExtraJson = Serialize(input.Extra); ApplyApiKey(model, input.ApiKey, input.ClearApiKey); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken); return ToResponse(model.Provider, model);
    }

    public async Task DeleteModelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var model = await db.Models.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Model 不存在。"); db.Models.Remove(model); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken);
    }

    private static void ValidateProvider(ProviderInput input)
    {
        if (string.IsNullOrWhiteSpace(input.BusinessId) || string.IsNullOrWhiteSpace(input.DisplayName)) throw new ArgumentException("Provider ID 和名称不能为空。");
        _ = NormalizeUrl(input.BaseUrl); _ = NormalizeProviderMode(input.ApiMode);
        if (input.ModelListUrl is not null) _ = NormalizeOptionalUrl(input.ModelListUrl);
    }

    private static void ValidateModel(ModelInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ModelId) || string.IsNullOrWhiteSpace(input.DisplayName) || string.IsNullOrWhiteSpace(input.Family)) throw new ArgumentException("Model ID、名称和 Family 不能为空。");
        if (input.ContextLength <= 0 || input.MaxTokens <= 0) throw new ArgumentException("上下文长度和最大输出必须为正数。");
        if (input.Temperature is double temperature && (!double.IsFinite(temperature) || temperature < 0)) throw new ArgumentException("Temperature 无效。");
        if (input.TopP is double topP && (!double.IsFinite(topP) || topP <= 0 || topP > 1)) throw new ArgumentException("Top P 无效。");
        if (input.BaseUrl is not null) _ = NormalizeOptionalUrl(input.BaseUrl); if (input.ApiMode is not null) _ = NormalizeOptionalModes(input.ApiMode);
    }

    private static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Base URL 必须是 HTTP 或 HTTPS 绝对地址。");
        return uri.ToString().TrimEnd('/');
    }

    private static string? NormalizeOptionalUrl(string? value) => string.IsNullOrWhiteSpace(value) ? null : NormalizeUrl(value);
    private static string NormalizeModes(string value) => string.Join(';', SplitModes(value));
    private static string NormalizeProviderMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "openai" or "anthropic" or "ollama" ? normalized : throw new ArgumentException("Provider 类型仅支持 openai、anthropic 或 ollama。");
    }

    private static void ValidateSettings(AppSettingsInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Language)) throw new ArgumentException("界面语言不能为空。");
        _ = NormalizeTheme(input.Theme); var proxyMode = NormalizeProxyMode(input.ProxyMode); _ = NormalizeUpdateChannel(input.UpdateChannel);
        if (input.ProxyPort is < 1 or > 65535) throw new ArgumentException("代理端口必须在 1 到 65535 之间。");
        if (proxyMode == "custom" && (!Uri.TryCreate(input.ProxyHost?.Trim(), UriKind.Absolute, out var proxyUri) || proxyUri.Scheme is not ("http" or "https"))) throw new ArgumentException("自定义代理地址必须是 HTTP 或 HTTPS 地址。");
        if (input.LogRetentionDays is < 1 or > 3650) throw new ArgumentException("日志保留天数必须在 1 到 3650 之间。");
    }

    private static string NormalizeTheme(string value) => value.Trim().ToLowerInvariant() switch
    {
        "system" or "dark" or "light" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("主题仅支持 system、dark 或 light。")
    };
    private static string NormalizeProxyMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "direct" or "system" or "custom" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("代理模式仅支持 direct、system 或 custom。")
    };
    private static string NormalizeUpdateChannel(string value) => value.Trim().ToLowerInvariant() switch
    {
        "stable" or "preview" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("更新渠道仅支持 stable 或 preview。")
    };
    private static string? NormalizeOptionalModes(string? value) => string.IsNullOrWhiteSpace(value) ? null : NormalizeModes(value);
    private static string[] SplitModes(string value) { var modes = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); if (modes.Length == 0 || modes.Any(mode => mode is not ("openai" or "anthropic" or "ollama"))) throw new ArgumentException("协议仅支持 openai、anthropic 或 ollama。"); return modes; }
    private static string Serialize<T>(T? value) => JsonSerializer.Serialize(value ?? (object)new Dictionary<string, string>());
    private static string? ProtectApiKey(string? value) => string.IsNullOrWhiteSpace(value) ? null : ProtectedApiKeyStore.Protect(value.Trim());
    private static void ApplyApiKey(ProviderEntity entity, string? value, bool clear) { if (clear || value is not null) entity.ProtectedApiKey = ProtectApiKey(value); }
    private static void ApplyApiKey(ModelEntity entity, string? value, bool clear) { if (clear) entity.ProtectedApiKey = null; else if (!string.IsNullOrWhiteSpace(value)) entity.ProtectedApiKey = ProtectApiKey(value); }
    private static ProviderResponse ToResponse(ProviderEntity provider) => new(provider.Id, provider.BusinessId, provider.DisplayName, provider.BaseUrl, provider.ApiMode, provider.Enabled, provider.UseProxy, !string.IsNullOrWhiteSpace(provider.ProtectedApiKey), provider.Models.Count, provider.HeadersJson, provider.Models.OrderBy(model => model.SortOrder).Select(model => ToResponse(provider, model)).ToArray(), provider.ModelListUrl);
    private static AppSettingsResponse ToResponse(AppSettingsEntity settings) => new(settings.Id, settings.Language, settings.Theme, settings.OpenControlCenterOnStartup, settings.ProxyMode, settings.ProxyHost, settings.ProxyPort, settings.ProxyUsername, !string.IsNullOrWhiteSpace(settings.ProtectedProxyPassword), settings.AutoCheckUpdates, settings.UpdateChannel, settings.DiagnosticsEnabled, settings.LogRetentionDays);
    private static ModelResponse ToResponse(ProviderEntity provider, ModelEntity model) => new(model.Id, provider.BusinessId, model.ModelId, model.DisplayName, model.ConfigId, model.Family, model.BaseUrl, model.ApiMode, model.ContextLength, model.MaxTokens, model.Vision, model.Temperature, model.TopP, model.Enabled, !string.IsNullOrWhiteSpace(model.ProtectedApiKey), model.HeadersJson, model.ExtraJson);
}
