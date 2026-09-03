using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OllamaHub.Configuration;

public sealed record ProviderInput(string BusinessId, string DisplayName, string BaseUrl, string ApiMode, bool Enabled, string? ApiKey, bool ClearApiKey, Dictionary<string, string>? Headers, bool UseProxy = false, string? ModelListUrl = null, string? EndpointFormat = "responses");
public sealed record AppSettingsInput(string Language, string Theme, string ProxyMode, string ProxyHost, int ProxyPort, string? ProxyUsername, string? ProxyPassword, bool ClearProxyPassword, bool AutoCheckUpdates, string UpdateChannel, bool DiagnosticsEnabled, int LogRetentionDays, bool LogStackTrace = false, bool TransparencyEnabled = true, int TransparencyOpacity = 86, int BlurAmount = 24, string TransparencyAlgorithm = "acrylic");
public sealed record AppSettingsResponse(int Id, string Language, string Theme, string ProxyMode, string ProxyHost, int ProxyPort, string? ProxyUsername, bool HasProxyPassword, bool AutoCheckUpdates, string UpdateChannel, bool DiagnosticsEnabled, int LogRetentionDays, bool LogStackTrace = false, bool TransparencyEnabled = true, int TransparencyOpacity = 86, int BlurAmount = 24, string TransparencyAlgorithm = "acrylic");
public sealed record ModelInput(string ModelId, string DisplayName, string? ConfigId, string Family, string? BaseUrl, string? ApiMode, int ContextLength, int MaxTokens, bool Vision, double? Temperature, double? TopP, bool Enabled, string? ApiKey, bool ClearApiKey, Dictionary<string, string>? Headers, Dictionary<string, JsonElement>? Extra);
public sealed record ProviderResponse(Guid Id, string BusinessId, string DisplayName, string BaseUrl, string ApiMode, bool Enabled, bool UseProxy, bool HasApiKey, int ModelCount, string HeadersJson, IReadOnlyList<ModelResponse> Models, string? ModelListUrl = null, string EndpointFormat = "responses", string? ApiKey = null);
public sealed record ModelResponse(Guid Id, string ProviderId, string ModelId, string DisplayName, string? ConfigId, string Family, string? BaseUrl, string? ApiMode, int ContextLength, int MaxTokens, bool Vision, double? Temperature, double? TopP, bool Enabled, bool HasApiKey, string HeadersJson, string ExtraJson);
public sealed record GatewayModelSourceResponse(Guid Id, string ModelName, string ProviderName);
public sealed record GatewayComboInput(string Name, bool Enabled, int SortOrder);
public sealed record GatewayComboResponse(Guid Id, string EndpointKey, string Name, bool Enabled, int SortOrder, IReadOnlyList<GatewayRouteResponse> Routes);
public sealed record GatewayRouteInput(Guid ModelId, bool Enabled, int SortOrder);
public sealed record GatewayRouteResponse(Guid Id, Guid ComboId, Guid ModelId, string ModelName, string ProviderName, bool Enabled, int SortOrder);
public sealed record GatewayEndpointResponse(string Key, string DisplayName, string PublicPath, bool Enabled, IReadOnlyList<GatewayComboResponse> Combos);
public sealed record GatewayEndpointToggleInput(bool Enabled);

internal static class AppearanceSettingsLimits
{
    public const int MinimumBlurAmount = 0;
    public const int MaximumBlurAmount = 64;

    public static int NormalizeBlurAmount(int value) => Math.Clamp(value, MinimumBlurAmount, MaximumBlurAmount);
}

public sealed class ConfigurationManagementService(IDbContextFactory<ConfigurationDbContext> dbContextFactory, IDatabaseConfigurationProvider configurationProvider)
{
    public async Task<IReadOnlyList<ProviderResponse>> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var providers = await db.Providers.AsNoTracking().Include(provider => provider.Models).OrderBy(provider => provider.SortOrder).ToListAsync(cancellationToken);
        return providers.Select(ToResponse).ToArray();
    }

    public async Task<IReadOnlyList<GatewayModelSourceResponse>> ListEnabledGatewayModelsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Models.AsNoTracking()
            .Where(model => model.Enabled && model.Provider.Enabled)
            .OrderBy(model => model.Provider.SortOrder)
            .ThenBy(model => model.SortOrder)
            .ThenBy(model => model.ModelId)
            .Select(model => new GatewayModelSourceResponse(model.Id, model.DisplayName, model.Provider.DisplayName))
            .ToArrayAsync(cancellationToken);
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
        settings.LogStackTrace = input.LogStackTrace;
        settings.TransparencyEnabled = input.TransparencyEnabled;
        settings.TransparencyOpacity = input.TransparencyOpacity;
        settings.BlurAmount = AppearanceSettingsLimits.NormalizeBlurAmount(input.BlurAmount);
        settings.TransparencyAlgorithm = NormalizeTransparencyAlgorithm(input.TransparencyAlgorithm);
        await db.SaveChangesAsync(cancellationToken);
        await configurationProvider.ReloadAsync(cancellationToken);
        return ToResponse(settings);
    }

    public async Task<ProviderResponse> CreateProviderAsync(ProviderInput input, CancellationToken cancellationToken = default)
    {
        ValidateProvider(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Providers.AnyAsync(provider => provider.BusinessId == input.BusinessId.Trim(), cancellationToken)) throw new InvalidOperationException("Provider ID 已存在。");
        var provider = new ProviderEntity { BusinessId = input.BusinessId.Trim(), DisplayName = input.DisplayName.Trim(), BaseUrl = NormalizeUrl(input.BaseUrl), ModelListUrl = NormalizeOptionalUrl(input.ModelListUrl), ApiMode = NormalizeProviderMode(input.ApiMode), EndpointFormat = NormalizeEndpointFormat(input.EndpointFormat), Enabled = input.Enabled, UseProxy = input.UseProxy, HeadersJson = Serialize(input.Headers) };
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
        provider.BusinessId = input.BusinessId.Trim(); provider.DisplayName = input.DisplayName.Trim(); provider.BaseUrl = NormalizeUrl(input.BaseUrl); provider.ModelListUrl = NormalizeOptionalUrl(input.ModelListUrl); provider.ApiMode = NormalizeProviderMode(input.ApiMode); provider.EndpointFormat = NormalizeEndpointFormat(input.EndpointFormat); provider.Enabled = input.Enabled; provider.UseProxy = input.UseProxy; provider.HeadersJson = Serialize(input.Headers);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var model = await db.Models.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Model 不存在。");
        if (await db.GatewayRoutes.AnyAsync(item => item.ModelId == id, cancellationToken)) throw new InvalidOperationException("模型仍被网关路由引用，请先从 Combo 中移除模型。");
        db.Models.Remove(model); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayEndpointResponse>> ListGatewayEndpointsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var endpoints = await db.GatewayEndpoints.AsNoTracking().Include(item => item.Combos).ThenInclude(item => item.Routes).ThenInclude(item => item.Model).ThenInclude(item => item.Provider).OrderBy(item => item.Key).ToListAsync(cancellationToken);
        return endpoints.Select(ToGatewayResponse).ToArray();
    }

    public async Task<GatewayEndpointResponse> SetGatewayEndpointEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var endpoint = await db.GatewayEndpoints.Include(item => item.Combos).ThenInclude(item => item.Routes).ThenInclude(item => item.Model).ThenInclude(item => item.Provider).SingleOrDefaultAsync(item => item.Key == key, cancellationToken) ?? throw new KeyNotFoundException("Endpoint 不存在。");
        endpoint.Enabled = enabled; await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken); return ToGatewayResponse(endpoint);
    }

    public async Task<GatewayComboResponse> CreateGatewayComboAsync(string endpointKey, GatewayComboInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        _ = await db.GatewayEndpoints.SingleOrDefaultAsync(item => item.Key == endpointKey, cancellationToken) ?? throw new KeyNotFoundException("Endpoint 不存在。");
        var name = NormalizeComboName(input.Name);
        if (await db.GatewayCombos.AnyAsync(item => item.EndpointKey == endpointKey && item.Name.ToLower() == name.ToLower(), cancellationToken)) throw new InvalidOperationException("Combo 模型名已存在。");
        var combo = new GatewayComboEntity { EndpointKey = endpointKey, Name = name, Enabled = input.Enabled, SortOrder = input.SortOrder };
        db.GatewayCombos.Add(combo); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken); return ToGatewayComboResponse(combo);
    }

    public async Task<GatewayComboResponse> UpdateGatewayComboAsync(Guid id, GatewayComboInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var combo = await db.GatewayCombos.Include(item => item.Routes).ThenInclude(item => item.Model).ThenInclude(item => item.Provider).SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Combo 模型不存在。");
        var name = NormalizeComboName(input.Name);
        if (await db.GatewayCombos.AnyAsync(item => item.Id != id && item.EndpointKey == combo.EndpointKey && item.Name.ToLower() == name.ToLower(), cancellationToken)) throw new InvalidOperationException("Combo 模型名已存在。");
        combo.Name = name; combo.Enabled = input.Enabled; combo.SortOrder = input.SortOrder;
        await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken); return ToGatewayComboResponse(combo);
    }

    public async Task DeleteGatewayComboAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var combo = await db.GatewayCombos.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("Combo 模型不存在。");
        db.GatewayCombos.Remove(combo); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken);
    }

    public async Task<GatewayRouteResponse> CreateGatewayRouteAsync(Guid comboId, GatewayRouteInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var combo = await db.GatewayCombos.SingleOrDefaultAsync(item => item.Id == comboId, cancellationToken) ?? throw new KeyNotFoundException("Combo 模型不存在。");
        var model = await db.Models.Include(item => item.Provider).SingleOrDefaultAsync(item => item.Id == input.ModelId, cancellationToken) ?? throw new KeyNotFoundException("模型不存在。");
        if (await db.GatewayRoutes.AnyAsync(item => item.ComboId == comboId && item.ModelId == input.ModelId, cancellationToken)) throw new InvalidOperationException("模型已在当前 Combo 中。");
        var route = new GatewayRouteEntity { EndpointKey = combo.EndpointKey, ComboId = comboId, ModelId = input.ModelId, Enabled = input.Enabled, SortOrder = input.SortOrder };
        db.GatewayRoutes.Add(route); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken); route.Model = model; return ToGatewayRouteResponse(route);
    }

    public async Task<GatewayRouteResponse> UpdateGatewayRouteAsync(Guid id, GatewayRouteInput input, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var route = await db.GatewayRoutes.Include(item => item.Model).ThenInclude(item => item.Provider).SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("路由不存在。");
        route.Enabled = input.Enabled; route.SortOrder = input.SortOrder; await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken); return ToGatewayRouteResponse(route);
    }

    public async Task DeleteGatewayRouteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken); var route = await db.GatewayRoutes.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new KeyNotFoundException("路由不存在。"); db.GatewayRoutes.Remove(route); await db.SaveChangesAsync(cancellationToken); await configurationProvider.ReloadAsync(cancellationToken);
    }

    private static GatewayEndpointResponse ToGatewayResponse(GatewayEndpointEntity endpoint) => new(endpoint.Key, endpoint.DisplayName, endpoint.PublicPath, endpoint.Enabled, endpoint.Combos.OrderBy(item => item.SortOrder).Select(ToGatewayComboResponse).ToArray());
    private static GatewayComboResponse ToGatewayComboResponse(GatewayComboEntity combo) => new(combo.Id, combo.EndpointKey, combo.Name, combo.Enabled, combo.SortOrder, combo.Routes.OrderBy(item => item.SortOrder).Select(ToGatewayRouteResponse).ToArray());
    private static GatewayRouteResponse ToGatewayRouteResponse(GatewayRouteEntity route) => new(route.Id, route.ComboId ?? Guid.Empty, route.ModelId, route.Model.DisplayName, route.Model.Provider.DisplayName, route.Enabled, route.SortOrder);
    private static string NormalizeComboName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Combo 模型名不能为空。");
        var normalized = value.Trim();
        if (normalized.Length > 256) throw new ArgumentException("Combo 模型名不能超过 256 个字符。");
        return normalized;
    }

    private static void ValidateProvider(ProviderInput input)
    {
        if (string.IsNullOrWhiteSpace(input.BusinessId) || string.IsNullOrWhiteSpace(input.DisplayName)) throw new ArgumentException("Provider ID 和名称不能为空。");
        _ = NormalizeUrl(input.BaseUrl); _ = NormalizeProviderMode(input.ApiMode); _ = NormalizeEndpointFormat(input.EndpointFormat);
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
        if (input.TransparencyOpacity is < 0 or > 100) throw new ArgumentException("透明程度必须在 0 到 100 之间。");
        if (input.BlurAmount is < AppearanceSettingsLimits.MinimumBlurAmount or > AppearanceSettingsLimits.MaximumBlurAmount) throw new ArgumentException("磨砂程度必须在 0 到 64 之间。");
        _ = NormalizeTransparencyAlgorithm(input.TransparencyAlgorithm);
    }

    private static string NormalizeTheme(string value) => value.Trim().ToLowerInvariant() switch
    {
        "system" or "dark" or "light" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("主题仅支持 system、dark 或 light。")
    };
    private static string NormalizeEndpointFormat(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => "responses",
        "chat_completions" or "responses" => value.Trim().ToLowerInvariant(),
        _ => throw new ArgumentException("OpenAI 请求格式仅支持 chat_completions 或 responses。")
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
    private static string NormalizeTransparencyAlgorithm(string value) => value.Trim().ToLowerInvariant() switch
    {
        "acrylic" => "acrylic",
        "blur" or "mica" => "acrylic",
        _ => throw new ArgumentException("磨砂算法仅支持 acrylic。")
    };
    private static string? NormalizeOptionalModes(string? value) => string.IsNullOrWhiteSpace(value) ? null : NormalizeModes(value);
    private static string[] SplitModes(string value) { var modes = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); if (modes.Length == 0 || modes.Any(mode => mode is not ("openai" or "anthropic" or "ollama"))) throw new ArgumentException("协议仅支持 openai、anthropic 或 ollama。"); return modes; }
    private static string Serialize<T>(T? value) => JsonSerializer.Serialize(value ?? (object)new Dictionary<string, string>());
    private static string? ProtectApiKey(string? value) => string.IsNullOrWhiteSpace(value) ? null : ProtectedApiKeyStore.Protect(value.Trim());
    private static void ApplyApiKey(ProviderEntity entity, string? value, bool clear) { if (clear || value is not null) entity.ProtectedApiKey = ProtectApiKey(value); }
    private static void ApplyApiKey(ModelEntity entity, string? value, bool clear) { if (clear) entity.ProtectedApiKey = null; else if (!string.IsNullOrWhiteSpace(value)) entity.ProtectedApiKey = ProtectApiKey(value); }
    private static ProviderResponse ToResponse(ProviderEntity provider) => new(provider.Id, provider.BusinessId, provider.DisplayName, provider.BaseUrl, provider.ApiMode, provider.Enabled, provider.UseProxy, !string.IsNullOrWhiteSpace(provider.ProtectedApiKey), provider.Models.Count, provider.HeadersJson, provider.Models.OrderBy(model => model.SortOrder).Select(model => ToResponse(provider, model)).ToArray(), provider.ModelListUrl, provider.EndpointFormat, ReadApiKey(provider.ProtectedApiKey));
    private static AppSettingsResponse ToResponse(AppSettingsEntity settings) => new(settings.Id, settings.Language, settings.Theme, settings.ProxyMode, settings.ProxyHost, settings.ProxyPort, settings.ProxyUsername, !string.IsNullOrWhiteSpace(settings.ProtectedProxyPassword), settings.AutoCheckUpdates, settings.UpdateChannel, settings.DiagnosticsEnabled, settings.LogRetentionDays, settings.LogStackTrace, settings.TransparencyEnabled, settings.TransparencyOpacity, AppearanceSettingsLimits.NormalizeBlurAmount(settings.BlurAmount), "acrylic");
    private static ModelResponse ToResponse(ProviderEntity provider, ModelEntity model) => new(model.Id, provider.BusinessId, model.ModelId, model.DisplayName, model.ConfigId, model.Family, model.BaseUrl, model.ApiMode, model.ContextLength, model.MaxTokens, model.Vision, model.Temperature, model.TopP, model.Enabled, !string.IsNullOrWhiteSpace(model.ProtectedApiKey), model.HeadersJson, model.ExtraJson);
    private static string? ReadApiKey(string? protectedValue) => string.IsNullOrWhiteSpace(protectedValue) || !ProtectedApiKeyStore.TryUnprotect(protectedValue, out var plainText) ? null : plainText;
}
