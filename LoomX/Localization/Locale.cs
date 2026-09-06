using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Localization;

namespace LoomX.Localization;

/// <summary>
/// Global UI-culture state. <see cref="SetCulture"/> mutates <see cref="CurrentCulture"/>
/// and raises <see cref="CultureChanged"/> so XAML bindings and ViewModels can react.
/// Thread culture is set in a static constructor so resource lookup works even before
/// any explicit SetCulture call.
/// </summary>
public static class LocaleService
{
    /// <summary>Initial/fallback culture when no user preference is stored.</summary>
    public const string DefaultCultureName = "zh-CN";

    private static CultureInfo _current = new CultureInfo(DefaultCultureName);
    private static readonly object _gate = new();

    static LocaleService()
    {
        CultureInfo.DefaultThreadCurrentCulture = _current;
        CultureInfo.DefaultThreadCurrentUICulture = _current;
    }

    public static CultureInfo CurrentCulture
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>Fires after <see cref="CurrentCulture"/> changes.</summary>
    public static event EventHandler<CultureInfo>? CultureChanged;

    /// <summary>
    /// Switches the UI culture. No-op for blank input. Always refreshes
    /// <see cref="CultureInfo.DefaultThreadCurrentUICulture"/> so satellite resource
    /// lookup uses the new culture; raises <see cref="CultureChanged"/> only when the
    /// culture actually changes.
    /// </summary>
    public static void SetCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return;
        CultureInfo next;
        try { next = new CultureInfo(cultureName); }
        catch { return; }

        bool changed;
        lock (_gate)
        {
            changed = !string.Equals(_current.Name, next.Name, StringComparison.OrdinalIgnoreCase);
            if (changed) _current = next;
        }

        CultureInfo.DefaultThreadCurrentCulture = next;
        CultureInfo.DefaultThreadCurrentUICulture = next;

        if (changed) CultureChanged?.Invoke(null, next);
    }
}

/// <summary>
/// Resolves localization keys against the embedded <c>LoomX.Resources.Strings</c>
/// resource set. Missing keys fall back to the key itself (keeps the UI from throwing
/// when a translator forgets a string).
/// </summary>
public static class ResourceLookup
{
    private static readonly ResourceManager _res = new(
        "LoomX.Resources.Strings",
        typeof(ResourceLookup).Assembly);

    public static string Resolve(string? key)
    {
        if (string.IsNullOrEmpty(key)) return key ?? string.Empty;
        try { return _res.GetString(key) ?? key; }
        catch (MissingManifestResourceException) { return key; }
        catch (MissingSatelliteAssemblyException) { return key; }
    }
}

/// <summary>
/// Avalonia binding target returned by <c>{l:Locale "key"}</c>. Resolves the key on
/// construction and re-resolves when <see cref="LocaleService.CultureChanged"/> fires,
/// notifying bound controls through <see cref="INotifyPropertyChanged"/>.
/// </summary>
public sealed class LocaleBinding : INotifyPropertyChanged
{
    public string Key { get; }
    public string Value { get; private set; }

    public LocaleBinding(string key)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = ResourceLookup.Resolve(key);
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnCultureChanged(object? sender, CultureInfo culture)
    {
        var next = ResourceLookup.Resolve(Key);
        if (string.Equals(Value, next, StringComparison.Ordinal)) return;
        Value = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}

/// <summary>
/// XAML markup extension. Usage in AXAML:
/// <code>
///   xmlns:l="using:LoomX.Localization"
///   &lt;TextBlock Text="{l:Locale nav.overview}" /&gt;
///   &lt;Button Content="{l:Locale settings.language.label}" /&gt;
/// </code>
/// Returns a <see cref="Binding"/> rooted at <see cref="LocaleBinding.Value"/> so
/// culture changes propagate reactively.
/// </summary>
public class LocaleExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocaleExtension() { }
    public LocaleExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var localeBinding = new LocaleBinding(Key);
        return new Binding(nameof(LocaleBinding.Value)) { Source = localeBinding };
    }
}

/// <summary>
/// Builds <see cref="IStringLocalizer{T}"/> instances bound to the shared
/// <c>LoomX.Resources.Strings</c> resource set, regardless of the type used as
/// <c>resourceSource</c>. This lets us keep a single central Strings.resx while
/// still using the idiomatic <c>IStringLocalizer&lt;MyViewModel&gt;</c> pattern.
/// </summary>
public sealed class LoomXStringLocalizerFactory : IStringLocalizerFactory
{
    private static readonly ResourceManager _res = new(
        "LoomX.Resources.Strings",
        typeof(LoomXStringLocalizerFactory).Assembly);

    public IStringLocalizer Create(Type resourceSource)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);
        return new LoomXStringLocalizer<object>(_res);
    }

    public IStringLocalizer Create(string baseName, string location)
    {
        return new LoomXStringLocalizer<object>(_res);
    }

    public IStringLocalizer<T> Create<T>() => new LoomXStringLocalizer<T>(_res);

    private sealed class LoomXStringLocalizer<T> : IStringLocalizer<T>
    {
        private readonly ResourceManager _res;
        public LoomXStringLocalizer(ResourceManager res) { _res = res; }

        public LocalizedString this[string name]
        {
            get
            {
                var value = _res.GetString(name, CultureInfo.DefaultThreadCurrentUICulture);
                return new LocalizedString(name, value ?? name);
            }
        }

        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                var entry = this[name];
                if (arguments is null || arguments.Length == 0) return entry;
                return new LocalizedString(name, string.Format(CultureInfo.CurrentCulture, entry.Value, arguments));
            }
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParent)
        {
            return Array.Empty<LocalizedString>();
        }
    }
}

/// <summary>
/// Convenience entry point used by ViewModels when constructed outside DI (unit
/// tests, legacy constructors). Falls back to the same central <c>Strings.resx</c>.
/// </summary>
public static class LocalizerFactory
{
    private static readonly LoomXStringLocalizerFactory _factory = new();

    public static IStringLocalizerFactory Instance => _factory;

    public static IStringLocalizer<T> Create<T>() => _factory.Create<T>();
}
