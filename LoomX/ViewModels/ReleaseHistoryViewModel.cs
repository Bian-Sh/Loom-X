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
    private readonly Func<string, string> localize;
    private bool isLatest;

    internal ReleaseHistoryItemViewModel(
        UpdateRelease release,
        bool isLatest,
        bool isCurrent,
        Func<string, string> localize,
        CultureInfo culture)
    {
        Release = release;
        this.isLatest = isLatest;
        IsCurrent = isCurrent;
        this.localize = localize;
        RefreshLocalizedText(culture);
    }

    public UpdateRelease Release { get; }
    public string VersionText => $"v{NormalizeVersion(Release.Version)}";
    public bool IsLatest { get => isLatest; private set => SetProperty(ref isLatest, value); }
    public bool IsCurrent { get; }
    public string PublishedAtText => Release.PublishedAt?.ToLocalTime().ToString("d", LocaleService.CurrentCulture) ?? string.Empty;
    public string LatestBadgeText => localize("settings.update.history.badge.latest");
    public string CurrentBadgeText => localize("settings.update.history.badge.current");

    internal void SetLatest(bool value) => IsLatest = value;

    internal void RefreshLocalizedText(CultureInfo culture)
    {
        OnPropertyChanged(nameof(PublishedAtText));
        OnPropertyChanged(nameof(LatestBadgeText));
        OnPropertyChanged(nameof(CurrentBadgeText));
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
    private readonly CancellationTokenSource lifetime = new();
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
    private volatile bool disposed;
    private int currentPage;

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
        LoadCommand = new AsyncCommand(() => EnsureLoadedAsync(), () => !disposed && !HasLoadedContent && !IsBusy, this.logger);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !disposed && !IsBusy, this.logger);
        LoadMoreCommand = new AsyncCommand(LoadMoreAsync, () => !disposed && HasMore && !IsBusy, this.logger);
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
        set => DispatchIfActive(() => SelectRelease(value, updateContent: true));
    }

    public ReleaseNotesContentViewModel Content { get; }
    public bool IsInitialLoading { get => isInitialLoading; private set => SetState(ref isInitialLoading, value); }
    public bool IsRefreshing { get => isRefreshing; private set => SetState(ref isRefreshing, value); }
    public bool IsLoadingMore { get => isLoadingMore; private set => SetState(ref isLoadingMore, value); }
    public bool IsEmpty { get => isEmpty; private set => SetProperty(ref isEmpty, value); }
    public bool HasError
    {
        get => hasError;
        private set
        {
            if (SetProperty(ref hasError, value)) OnPropertyChanged(nameof(ErrorText));
        }
    }
    public bool HasCachedContent { get => hasCachedContent; private set => SetProperty(ref hasCachedContent, value); }
    public bool HasMore { get => hasMore; private set => SetState(ref hasMore, value); }
    public bool CanShowLoadMore => HasMore && !IsLoadingMore;
    public string ErrorText => HasError ? Loc("settings.update.history.error.load") : string.Empty;
    private bool IsBusy => IsInitialLoading || IsRefreshing || IsLoadingMore;
    private bool HasLoadedContent => hasLoaded;

    public ICommand LoadCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand LoadMoreCommand { get; }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            token => hasLoaded ? Task.CompletedTask : LoadFirstPageAsync(isRefresh: false, token),
            cancellationToken);

    public Task RefreshAsync() =>
        RunSerializedAsync(token => LoadFirstPageAsync(isRefresh: true, token));

    private Task LoadMoreAsync() =>
        RunSerializedAsync(LoadMoreCoreAsync);

    private async Task RunSerializedAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        if (disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var entered = false;
        try
        {
            await operationGate.WaitAsync(linked.Token);
            entered = true;
            linked.Token.ThrowIfCancellationRequested();
            if (disposed) return;
            await operation(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        finally
        {
            if (entered) operationGate.Release();
        }
    }

    private async Task LoadFirstPageAsync(bool isRefresh, CancellationToken cancellationToken)
    {
        DispatchIfActive(() =>
        {
            if (isRefresh) IsRefreshing = true;
            else IsInitialLoading = true;
            ClearError();
        });

        var operation = isRefresh ? "refresh" : "initial";
        logger.LogInformation("正式版本历史加载开始 {Operation} {Page} {PageSize}", operation, 1, PageSize);
        try
        {
            var settings = await proxySettingsReader(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var page = await updateService.GetStableReleasesAsync(settings, 1, PageSize, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var previousVersion = ReleaseHistoryItemViewModel.NormalizeVersion(SelectedRelease?.Release.Version);
            var nextItems = BuildItems(page.Items);

            DispatchIfActive(() =>
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

            logger.LogInformation("正式版本历史加载完成 {Operation} {Page} {Count} {HasMore}", operation, page.Page, nextItems.Count, page.HasMore);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("正式版本历史加载已取消 {Operation}", operation);
        }
        catch (Exception exception)
        {
            DispatchIfActive(ApplyFailure);
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, operation);
            logger.LogWarning(diagnostic, "正式版本历史加载失败 {Operation} {HasCachedContent} {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                operation,
                Releases.Count > 0,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
        }
        finally
        {
            DispatchIfActive(() =>
            {
                if (isRefresh) IsRefreshing = false;
                else IsInitialLoading = false;
                RaiseCommandStates();
            });
        }
    }

    private async Task LoadMoreCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasMore || !hasLoaded) return;
        DispatchIfActive(() =>
        {
            IsLoadingMore = true;
            ClearError();
        });

        var nextPage = currentPage + 1;
        logger.LogInformation("正式版本历史加载更多开始 {Page} {PageSize}", nextPage, PageSize);
        try
        {
            var settings = await proxySettingsReader(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var page = await updateService.GetStableReleasesAsync(settings, nextPage, PageSize, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var existingVersions = Releases
                .Select(item => ReleaseHistoryItemViewModel.NormalizeVersion(item.Release.Version))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var additions = BuildItemsForAppend(page.Items, existingVersions);

            DispatchIfActive(() =>
            {
                foreach (var item in additions) Releases.Add(item);
                RecalculateLatestFlags();
                currentPage = page.Page;
                HasMore = page.HasMore;
                ClearError();
                RaiseCommandStates();
            });

            logger.LogInformation("正式版本历史加载更多完成 {Page} {AddedCount} {HasMore}", page.Page, additions.Count, page.HasMore);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("正式版本历史加载更多已取消 {Page}", nextPage);
        }
        catch (Exception exception)
        {
            DispatchIfActive(ApplyFailure);
            var diagnostic = SafeUpdateDiagnosticException.Create(exception, "load-more");
            logger.LogWarning(diagnostic, "正式版本历史加载更多失败 {Page} {HasCachedContent} {ExceptionType} {HResult} {HttpStatusCode} {Stage}",
                nextPage,
                Releases.Count > 0,
                diagnostic.OriginalExceptionType,
                diagnostic.OriginalHResult,
                diagnostic.HttpStatusCode,
                diagnostic.Stage);
        }
        finally
        {
            DispatchIfActive(() =>
            {
                IsLoadingMore = false;
                RaiseCommandStates();
            });
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
        var currentKey = ReleaseHistoryItemViewModel.NormalizeVersion(AppVersion.Current);
        var additions = new List<ReleaseHistoryItemViewModel>(source.Count);

        foreach (var release in source)
        {
            var key = ReleaseHistoryItemViewModel.NormalizeVersion(release.Version);
            if (!existingVersions.Add(key)) continue;
            additions.Add(CreateItem(release, key, string.Empty, currentKey));
        }

        return additions;
    }

    private void RecalculateLatestFlags()
    {
        StableVersion? latest = null;
        foreach (var item in Releases)
        {
            if (!AppVersion.TryParse(item.Release.Version, out var version)) continue;
            if (!latest.HasValue || version.CompareTo(latest.Value) > 0) latest = version;
        }

        foreach (var item in Releases)
        {
            var isLatest = latest.HasValue
                && AppVersion.TryParse(item.Release.Version, out var version)
                && version.CompareTo(latest.Value) == 0;
            item.SetLatest(isLatest);
        }
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
        RaiseCommandStates();
    }

    private void ClearError()
    {
        HasError = false;
        HasCachedContent = false;
    }

    private string Loc(string key)
    {
        var value = localizer[key];
        return value.ResourceNotFound || string.Equals(value.Value, key, StringComparison.Ordinal) ? key : value.Value;
    }

    private void OnCultureChanged(object? sender, CultureInfo culture) => DispatchIfActive(() =>
    {
        foreach (var item in Releases) item.RefreshLocalizedText(culture);
        if (HasError) OnPropertyChanged(nameof(ErrorText));
    });

    private bool SetState(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName)) return false;
        if (propertyName is nameof(HasMore) or nameof(IsLoadingMore))
            OnPropertyChanged(nameof(CanShowLoadMore));
        RaiseCommandStates();
        return true;
    }

    private void RaiseCommandStates()
    {
        (LoadCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (LoadMoreCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private void DispatchIfActive(Action action)
    {
        if (disposed) return;
        dispatch(() =>
        {
            if (!disposed) action();
        });
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
        lifetime.Cancel();
        Content.Dispose();
    }
}
