using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX.Localization;
using LoomX.Services;

namespace LoomX.ViewModels;

/// <summary>正式版本历史中的单个可展示版本。</summary>
public sealed class ReleaseHistoryItemViewModel : NotifyViewModel
{
    private readonly Func<string, string, string> localize;
    private string publishedAtText = string.Empty;
    private string latestBadgeText = string.Empty;
    private string currentBadgeText = string.Empty;

    internal ReleaseHistoryItemViewModel(
        UpdateRelease release,
        bool isLatest,
        bool isCurrent,
        Func<string, string, string> localize,
        CultureInfo culture)
    {
        Release = release;
        IsLatest = isLatest;
        IsCurrent = isCurrent;
        this.localize = localize;
        RefreshLocalizedText(culture);
    }

    public UpdateRelease Release { get; }
    public string VersionText => $"v{NormalizeVersion(Release.Version)}";
    public bool IsLatest { get; }
    public bool IsCurrent { get; }
    public string PublishedAtText { get => publishedAtText; private set => SetProperty(ref publishedAtText, value); }
    public string LatestBadgeText { get => latestBadgeText; private set => SetProperty(ref latestBadgeText, value); }
    public string CurrentBadgeText { get => currentBadgeText; private set => SetProperty(ref currentBadgeText, value); }

    internal void RefreshLocalizedText(CultureInfo culture)
    {
        PublishedAtText = Release.PublishedAt?.ToLocalTime().ToString("d", culture) ?? string.Empty;
        LatestBadgeText = localize("update.history.badge.latest", "最新");
        CurrentBadgeText = localize("update.history.badge.current", "当前");
    }

    internal static string NormalizeVersion(string? value)
    {
        if (AppVersion.TryParse(value, out var version)) return version.ToString();
        return value?.Trim().TrimStart('v', 'V') ?? string.Empty;
    }
}

/// <summary>维护正式 Release 历史的分页、缓存、选择与安全错误状态。</summary>
public sealed class ReleaseHistoryViewModel : NotifyViewModel, IDisposable
{
    private const int PageSize = 10;
    private readonly IUpdateService updateService;
    private readonly Func<CancellationToken, Task<UpdateProxySettings>> proxySettingsReader;
    private readonly ILogger<ReleaseHistoryViewModel> logger;
    private readonly IStringLocalizer<ReleaseHistoryViewModel> localizer;
    private readonly Action<Action> dispatch;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private ObservableCollection<ReleaseHistoryItemViewModel> releases = [];
    private ReleaseHistoryItemViewModel? selectedRelease;
    private bool isInitialLoading;
    private bool isRefreshing;
    private bool isLoadingMore;
    private bool isEmpty;
    private bool hasError;
    private bool hasCachedContent;
    private bool hasMore;
    private bool hasLoaded;
    private bool disposed;
    private int currentPage;
    private string errorText = string.Empty;

    public ReleaseHistoryViewModel(
        IUpdateService updateService,
        Func<CancellationToken, Task<UpdateProxySettings>> proxySettingsReader,
        ILogger<ReleaseHistoryViewModel>? logger = null,
        IStringLocalizer<ReleaseHistoryViewModel>? localizer = null,
        Action<Action>? dispatch = null)
    {
        this.updateService = updateService;
        this.proxySettingsReader = proxySettingsReader;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ReleaseHistoryViewModel>.Instance;
        this.localizer = localizer ?? LocalizerFactory.Create<ReleaseHistoryViewModel>();
        this.dispatch = dispatch ?? DispatchToUiThread;

        Content = new ReleaseNotesContentViewModel();
        LoadCommand = new AsyncCommand(() => EnsureLoadedAsync(), () => !HasLoadedContent && !IsBusy, this.logger);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy, this.logger);
        LoadMoreCommand = new AsyncCommand(LoadMoreAsync, () => HasMore && !IsBusy, this.logger);
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public ObservableCollection<ReleaseHistoryItemViewModel> Releases
    {
        get => releases;
        private set => SetProperty(ref releases, value);
    }

    public ReleaseHistoryItemViewModel? SelectedRelease
    {
        get => selectedRelease;
        set => dispatch(() => SelectRelease(value, updateContent: true));
    }

    public ReleaseNotesContentViewModel Content { get; }
    public bool IsInitialLoading { get => isInitialLoading; private set => SetState(ref isInitialLoading, value); }
    public bool IsRefreshing { get => isRefreshing; private set => SetState(ref isRefreshing, value); }
    public bool IsLoadingMore { get => isLoadingMore; private set => SetState(ref isLoadingMore, value); }
    public bool IsEmpty { get => isEmpty; private set => SetProperty(ref isEmpty, value); }
    public bool HasError { get => hasError; private set => SetProperty(ref hasError, value); }
    public bool HasCachedContent { get => hasCachedContent; private set => SetProperty(ref hasCachedContent, value); }
    public bool HasMore { get => hasMore; private set => SetState(ref hasMore, value); }
    public string ErrorText { get => errorText; private set => SetProperty(ref errorText, value); }
    private bool IsBusy => IsInitialLoading || IsRefreshing || IsLoadingMore;
    private bool HasLoadedContent => hasLoaded;

    public ICommand LoadCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadMoreCommand { get; }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hasLoaded) return;
            await LoadFirstPageAsync(isRefresh: false, cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task RefreshAsync()
    {
        await operationGate.WaitAsync();
        try
        {
            await LoadFirstPageAsync(isRefresh: true, CancellationToken.None);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async Task LoadFirstPageAsync(bool isRefresh, CancellationToken cancellationToken)
    {
        dispatch(() =>
        {
            if (isRefresh) IsRefreshing = true;
            else IsInitialLoading = true;
            ClearError();
        });

        var operation = isRefresh ? "刷新" : "首次加载";
        logger.LogInformation("正式版本历史加载开始 {Operation} {Page} {PageSize}", operation, 1, PageSize);
        try
        {
            var settings = await proxySettingsReader(cancellationToken);
            var page = await updateService.GetStableReleasesAsync(settings, 1, PageSize, cancellationToken);
            var previousVersion = ReleaseHistoryItemViewModel.NormalizeVersion(SelectedRelease?.Release.Version);
            var nextItems = BuildItems(page.Items);

            dispatch(() =>
            {
                Releases = new ObservableCollection<ReleaseHistoryItemViewModel>(nextItems);
                currentPage = page.Page;
                HasMore = page.HasMore;
                hasLoaded = true;
                IsEmpty = Releases.Count == 0;
                ClearError();

                var selection = Releases.FirstOrDefault(item =>
                    string.Equals(
                        ReleaseHistoryItemViewModel.NormalizeVersion(item.Release.Version),
                        previousVersion,
                        StringComparison.OrdinalIgnoreCase));
                selection ??= Releases.FirstOrDefault(item => item.IsLatest);
                SelectRelease(selection, updateContent: true);
                RaiseCommandStates();
            });

            logger.LogInformation(
                "正式版本历史加载完成 {Operation} {Page} {Count} {HasMore}",
                operation,
                page.Page,
                nextItems.Count,
                page.HasMore);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("正式版本历史加载已取消 {Operation}", operation);
        }
        catch (Exception exception)
        {
            dispatch(() => ApplyFailure());
            logger.LogWarning(
                exception,
                "正式版本历史加载失败 {Operation} {HasCachedContent}",
                operation,
                Releases.Count > 0);
        }
        finally
        {
            dispatch(() =>
            {
                if (isRefresh) IsRefreshing = false;
                else IsInitialLoading = false;
                RaiseCommandStates();
            });
        }
    }

    private async Task LoadMoreAsync()
    {
        await operationGate.WaitAsync();
        try
        {
            if (!HasMore || !hasLoaded) return;
            dispatch(() =>
            {
                IsLoadingMore = true;
                ClearError();
            });

            var nextPage = currentPage + 1;
            logger.LogInformation("正式版本历史加载更多开始 {Page} {PageSize}", nextPage, PageSize);
            try
            {
                var settings = await proxySettingsReader(CancellationToken.None);
                var page = await updateService.GetStableReleasesAsync(settings, nextPage, PageSize, CancellationToken.None);
                var existingVersions = Releases
                    .Select(item => ReleaseHistoryItemViewModel.NormalizeVersion(item.Release.Version))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var additions = BuildItemsForAppend(page.Items, existingVersions);

                dispatch(() =>
                {
                    foreach (var item in additions) Releases.Add(item);
                    currentPage = page.Page;
                    HasMore = page.HasMore;
                    ClearError();
                    RaiseCommandStates();
                });

                logger.LogInformation(
                    "正式版本历史加载更多完成 {Page} {AddedCount} {HasMore}",
                    page.Page,
                    additions.Count,
                    page.HasMore);
            }
            catch (Exception exception)
            {
                dispatch(() => ApplyFailure());
                logger.LogWarning(exception, "正式版本历史加载更多失败 {Page} {HasCachedContent}", nextPage, Releases.Count > 0);
            }
            finally
            {
                dispatch(() =>
                {
                    IsLoadingMore = false;
                    RaiseCommandStates();
                });
            }
        }
        finally
        {
            operationGate.Release();
        }
    }

    private List<ReleaseHistoryItemViewModel> BuildItems(IReadOnlyList<UpdateRelease> source)
    {
        var latestVersion = source
            .Select(release => AppVersion.TryParse(release.Version, out var version) ? version : (StableVersion?)null)
            .Where(version => version.HasValue)
            .Select(version => version!.Value)
            .DefaultIfEmpty()
            .Max();
        var hasStableVersion = source.Any(release => AppVersion.TryParse(release.Version, out _));
        var latestKey = hasStableVersion ? latestVersion.ToString() : ReleaseHistoryItemViewModel.NormalizeVersion(source.FirstOrDefault()?.Version);
        var currentKey = ReleaseHistoryItemViewModel.NormalizeVersion(AppVersion.Current);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ReleaseHistoryItemViewModel>(source.Count);

        foreach (var release in source)
        {
            var key = ReleaseHistoryItemViewModel.NormalizeVersion(release.Version);
            if (!seen.Add(key)) continue;
            items.Add(CreateItem(release, key, latestKey, currentKey));
        }

        return items;
    }

    private List<ReleaseHistoryItemViewModel> BuildItemsForAppend(
        IReadOnlyList<UpdateRelease> source,
        HashSet<string> existingVersions)
    {
        var latestKey = Releases.FirstOrDefault(item => item.IsLatest) is { } latest
            ? ReleaseHistoryItemViewModel.NormalizeVersion(latest.Release.Version)
            : string.Empty;
        var currentKey = ReleaseHistoryItemViewModel.NormalizeVersion(AppVersion.Current);
        var additions = new List<ReleaseHistoryItemViewModel>(source.Count);

        foreach (var release in source)
        {
            var key = ReleaseHistoryItemViewModel.NormalizeVersion(release.Version);
            if (!existingVersions.Add(key)) continue;
            additions.Add(CreateItem(release, key, latestKey, currentKey));
        }

        return additions;
    }

    private ReleaseHistoryItemViewModel CreateItem(
        UpdateRelease release,
        string normalizedVersion,
        string latestVersion,
        string currentVersion) =>
        new(
            release,
            string.Equals(normalizedVersion, latestVersion, StringComparison.OrdinalIgnoreCase),
            string.Equals(normalizedVersion, currentVersion, StringComparison.OrdinalIgnoreCase),
            Loc,
            LocaleService.CurrentCulture);

    private void SelectRelease(ReleaseHistoryItemViewModel? value, bool updateContent)
    {
        if (!SetProperty(ref selectedRelease, value, nameof(SelectedRelease))) return;
        if (updateContent) Content.SetRelease(value?.Release);
    }

    private void ApplyFailure()
    {
        HasError = true;
        HasCachedContent = Releases.Count > 0;
        IsEmpty = false;
        ErrorText = Loc("update.history.error.load", "无法加载版本历史，请重试。");
        RaiseCommandStates();
    }

    private void ClearError()
    {
        HasError = false;
        HasCachedContent = false;
        ErrorText = string.Empty;
    }

    private string Loc(string key, string fallback)
    {
        var value = localizer[key];
        return value.ResourceNotFound || string.Equals(value.Value, key, StringComparison.Ordinal)
            ? fallback
            : value.Value;
    }

    private void OnCultureChanged(object? sender, CultureInfo culture) => dispatch(() =>
    {
        foreach (var item in Releases) item.RefreshLocalizedText(culture);
        if (HasError) ErrorText = Loc("update.history.error.load", "无法加载版本历史，请重试。");
    });

    private bool SetState(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return false;
        RaiseCommandStates();
        return true;
    }

    private void RaiseCommandStates()
    {
        (LoadCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (LoadMoreCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        LocaleService.CultureChanged -= OnCultureChanged;
        Content.Dispose();
        operationGate.Dispose();
    }
}
