using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LoomX.Activity;
using LoomX.Localization;
using LoomX.Services;

namespace LoomX.ViewModels;

public sealed class ActivityFilterOption : INotifyPropertyChanged
{
    public ActivityFilterOption(string value, string? localizationKey = null)
    {
        Value = value;
        LocalizationKey = localizationKey;
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public string Value { get; }
    public string? LocalizationKey { get; }
    public string DisplayName => GetDisplayName(LocaleService.CurrentCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal string GetDisplayName(System.Globalization.CultureInfo culture) => LocalizationKey is null ? Value : ResourceLookup.Resolve(LocalizationKey, culture);
    internal void NotifyDisplayNameChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    private void OnCultureChanged(object? sender, System.Globalization.CultureInfo culture) => NotifyDisplayNameChanged();

    public override string ToString() => DisplayName;
}

public sealed class ActivityViewModel : NotifyViewModel, IDisposable
{
    private readonly AppDataStore dataStore;
    private readonly ILogger<ActivityViewModel> logger;
    private readonly EventHandler storeActivityHandler;
    private readonly IStringLocalizer<ActivityViewModel> _loc;
    private CancellationTokenSource? refreshCancellation;
    private string searchText = string.Empty;
    private ActivityFilterOption selectedStatus = StatusOptions[0];
    private ActivityFilterOption selectedProtocol = ProtocolOptions[0];
    private ActivityItemViewModel? selectedItem;
    private string status = "正在加载活动…";
    private int totalCount;
    private int conversionCount;
    private int failureCount;
    private string p95Latency = "—";
    private bool isRefreshing;
    private bool isLoadingMore;
    private bool isHistoryMode;
    private bool hasMore;
    private int pendingActivityCount;
    private double pullDistance;
    private int refreshVersion;

    public ObservableCollection<ActivityItemViewModel> Items { get; } = [];
    public static IReadOnlyList<ActivityFilterOption> StatusOptions { get; } =
    [
        new("all", "activity.filter.status.all"),
        new("ok", "activity.filter.status.success"),
        new("fail", "activity.filter.status.failed"),
        new("warn", "activity.filter.status.warning")
    ];
    public static IReadOnlyList<ActivityFilterOption> ProtocolOptions { get; } =
    [
        new("all", "activity.filter.protocol.all"),
        new("OpenAI"),
        new("Anthropic"),
        new("Ollama")
    ];
    public event EventHandler? ScrollToTopRequested;
    public string SearchText { get => searchText; set { if (SetProperty(ref searchText, value ?? string.Empty)) QueueRefresh(); } }
    public ActivityFilterOption SelectedStatus { get => selectedStatus; set { if (SetProperty(ref selectedStatus, value ?? StatusOptions[0])) QueueRefresh(); } }
    public ActivityFilterOption SelectedProtocol { get => selectedProtocol; set { if (SetProperty(ref selectedProtocol, value ?? ProtocolOptions[0])) QueueRefresh(); } }
    public ActivityItemViewModel? SelectedItem
    {
        get => selectedItem;
        private set
        {
            if (!SetProperty(ref selectedItem, value)) return;
            OnPropertyChanged(nameof(SelectedModelLabel));
            OnPropertyChanged(nameof(SelectedRequestIdLabel));
            OnPropertyChanged(nameof(SelectedLogSummary));
        }
    }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string SelectedModelLabel => SelectedItem?.ModelId ?? Loc("activity.detail.fallback.model");
    public string SelectedRequestIdLabel => SelectedItem?.RequestId ?? Loc("activity.detail.fallback.requestid");
    public string SelectedLogSummary => SelectedItem?.LogSummary ?? Loc("activity.detail.summary.fallback");
    public string ResultCountLabel => LocFormat("activity.result.count", Items.Count);
    public int TotalCount { get => totalCount; private set => SetProperty(ref totalCount, value); }
    public int ConversionCount { get => conversionCount; private set => SetProperty(ref conversionCount, value); }
    public int FailureCount { get => failureCount; private set => SetProperty(ref failureCount, value); }
    public string P95Latency { get => p95Latency; private set => SetProperty(ref p95Latency, value); }
    public bool IsRefreshing { get => isRefreshing; private set => SetProperty(ref isRefreshing, value); }
    public bool IsLoadingMore { get => isLoadingMore; private set => SetProperty(ref isLoadingMore, value); }
    public bool IsHistoryMode { get => isHistoryMode; private set { if (SetProperty(ref isHistoryMode, value)) OnPropertyChanged(nameof(IsLatestMode)); } }
    public bool IsLatestMode => !IsHistoryMode;
    public bool HasMore { get => hasMore; private set => SetProperty(ref hasMore, value); }
    public int PendingActivityCount { get => pendingActivityCount; private set { if (SetProperty(ref pendingActivityCount, value)) { OnPropertyChanged(nameof(HasPendingActivities)); OnPropertyChanged(nameof(PendingActivityLabel)); } } }
    public bool HasPendingActivities => PendingActivityCount > 0;
    public string PendingActivityLabel => PendingActivityCount > 0 ? LocFormat("activity.pending.count", PendingActivityCount) : Loc("activity.pending.back");
    public double PullDistance { get => pullDistance; private set { if (SetProperty(ref pullDistance, value)) OnPropertyChanged(nameof(IsPullToRefreshVisible)); } }
    public bool IsPullToRefreshVisible => PullDistance >= 24 || IsLoadingMore;
    public string LoadMoreLabel => IsLoadingMore ? Loc("activity.loadmore.loading") : HasMore ? Loc("activity.loadmore.continue") : Loc("activity.loadmore.end");
    public ICommand SelectCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public ICommand ReturnToLatestCommand { get; }

    public ActivityViewModel(AppDataStore dataStore, ILogger<ActivityViewModel>? logger = null, IStringLocalizer<ActivityViewModel>? localizer = null)
    {
        this.dataStore = dataStore;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ActivityViewModel>.Instance;
        _loc = localizer ?? LocalizerFactory.Create<ActivityViewModel>();
        SelectCommand = new AsyncCommand(parameter => { SelectedItem = parameter as ActivityItemViewModel; return Task.CompletedTask; });
        LoadMoreCommand = new AsyncCommand(_ => LoadMoreAsync());
        ReturnToLatestCommand = new AsyncCommand(_ => ReturnToLatestAsync());
        storeActivityHandler = (_, _) => OnStoreActivityChanged();
        dataStore.ActivityWindowChanged += storeActivityHandler;
        LocaleService.CultureChanged += OnCultureChanged;
        _ = RefreshAsync();
    }

    public ActivityViewModel(GatewayProcessService gatewayService, ILogger<ActivityViewModel>? logger = null)
        : this(new AppDataStore(new ConfigSnapshotService(), gatewayService), logger) { }

    private string Loc(string key) => _loc[key]?.Value ?? key;
    private string LocFormat(string key, params object[] args)
    {
        var value = Loc(key);
        return args.Length == 0 ? value : string.Format(System.Globalization.CultureInfo.CurrentCulture, value, args);
    }

    private void OnCultureChanged(object? sender, System.Globalization.CultureInfo culture)
    {
        Status = Loc("activity.status.loading");
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(PendingActivityLabel));
        OnPropertyChanged(nameof(LoadMoreLabel));
        OnPropertyChanged(nameof(SelectedModelLabel));
        OnPropertyChanged(nameof(SelectedRequestIdLabel));
        OnPropertyChanged(nameof(SelectedLogSummary));
        ApplyPage(new ActivityPage(dataStore.ActivityWindow, null, dataStore.ActivityHasMore));
    }

    private void QueueRefresh()
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        _ = RefreshAsync(refreshCancellation.Token);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref refreshVersion);
        IsRefreshing = true;
        SelectedItem = null;
        try
        {
            var page = await dataStore.LoadActivityPageAsync(BuildQuery(), cancellationToken);
            ApplyPage(page);
            RequestScrollToTop();
            Status = page.Items.Count == 0 ? Loc("activity.status.empty") : LocFormat("activity.status.loaded", page.Items.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = LocFormat("activity.status.load.failed", exception.Message);
            logger.LogError(exception, "活动加载失败");
        }
        finally
        {
            if (version == refreshVersion) IsRefreshing = false;
        }
    }

    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || !HasMore || IsRefreshing) return;
        IsLoadingMore = true;
        try
        {
            var page = await dataStore.LoadOlderActivityPageAsync(BuildQuery());
            IsHistoryMode = true;
            ApplyPage(page);
            Status = page.HasMore ? LocFormat("activity.status.history.more", page.Items.Count) : LocFormat("activity.status.history.end", page.Items.Count);
        }
        catch (Exception exception)
        {
            Status = LocFormat("activity.status.history.failed", exception.Message);
            logger.LogError(exception, "历史活动分页失败");
        }
        finally
        {
            IsLoadingMore = false;
            OnPropertyChanged(nameof(LoadMoreLabel));
        }
    }

    private async Task ReturnToLatestAsync()
    {
        try
        {
            var page = await dataStore.ReturnToLatestAsync(BuildQuery());
            IsHistoryMode = false;
            ApplyPage(page);
            RequestScrollToTop();
            Status = page.Items.Count == 0 ? Loc("activity.status.empty") : Loc("activity.status.returned");
        }
        catch (Exception exception)
        {
            Status = LocFormat("activity.status.return.failed", exception.Message);
            logger.LogError(exception, "返回最新活动失败");
        }
    }

    public void NotifyScrollMetrics(double offsetY, double extentHeight, double viewportHeight)
    {
        var distanceToBottom = extentHeight - viewportHeight - offsetY;
        PullDistance = Math.Clamp(Math.Max(0, 80 - distanceToBottom), 0, 80);
        if (distanceToBottom <= 1 && HasMore && !IsLoadingMore && !IsRefreshing)
            LoadMoreCommand.Execute(null);
    }

    private ActivityQuery BuildQuery() => new(
        SearchText,
        SelectedStatus.Value is "all" ? null : SelectedStatus.Value,
        SelectedProtocol.Value is "all" ? null : SelectedProtocol.Value,
        AppDataStore.ActivityWindowLimit);

    private void ApplyPage(ActivityPage page)
    {
        var selectedId = SelectedItem?.Id;
        Items.Clear();
        foreach (var record in page.Items) Items.Add(ActivityItemViewModel.FromRecord(record));
        SelectedItem = selectedId.HasValue ? Items.FirstOrDefault(item => item.Id == selectedId.Value) : null;
        HasMore = page.HasMore;
        PendingActivityCount = dataStore.PendingActivityCount;
        UpdateSummary(page.Items);
        OnPropertyChanged(nameof(ResultCountLabel));
        OnPropertyChanged(nameof(LoadMoreLabel));
    }

    private void OnStoreActivityChanged()
    {
        void Apply()
        {
            ApplyPage(new ActivityPage(dataStore.ActivityWindow, null, dataStore.ActivityHasMore));
            IsHistoryMode = dataStore.ActivityHistoryMode;
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    private void RequestScrollToTop()
    {
        if (Dispatcher.UIThread.CheckAccess()) ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
        else Dispatcher.UIThread.Post(() => ScrollToTopRequested?.Invoke(this, EventArgs.Empty));
    }

    private void UpdateSummary(IReadOnlyList<ActivityEventRecord> records)
    {
        TotalCount = records.Count;
        ConversionCount = records.Count(item => item.Route.Contains('→'));
        FailureCount = records.Count(item => item.StatusCode >= 500);
        var values = records.Select(item => item.ElapsedMs).OrderBy(item => item).ToArray();
        P95Latency = values.Length == 0 ? ResourceLookup.Resolve("activity.dash") : LocFormat("activity.latency.format", values[(int)Math.Ceiling(values.Length * .95) - 1]);
    }

    public void Dispose()
    {
        dataStore.ActivityWindowChanged -= storeActivityHandler;
        LocaleService.CultureChanged -= OnCultureChanged;
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
    }
}

public sealed class ActivityItemViewModel : NotifyViewModel
{
    private ActivityItemViewModel(ActivityEventRecord record)
    {
        Id = record.Id;
        RequestId = record.RequestId;
        Time = record.CreatedAt.ToLocalTime().ToString("HH:mm:ss");
        ModelId = string.IsNullOrWhiteSpace(record.ModelId) ? ResourceLookup.Resolve("activity.item.model.unknown") : record.ModelId;
        ProviderId = string.IsNullOrWhiteSpace(record.ProviderId) ? ResourceLookup.Resolve("activity.item.provider.unknown") : record.ProviderId;
        Route = LocalizeRoute(record.Route, System.Globalization.CultureInfo.CurrentUICulture);
        Protocol = record.Protocol;
        StatusCode = record.StatusCode;
        var successKey = "activity.filter.status.success";
        var warningKey = "activity.filter.status.warning";
        var failKey = "activity.filter.status.failed";
        StatusLabel = record.StatusCode is >= 200 and < 300
            ? ResourceLookup.Resolve(successKey)
            : record.StatusCode is >= 400 and < 500
                ? ResourceLookup.Resolve(warningKey)
                : ResourceLookup.Resolve(failKey);
        StatusColor = StatusLabel switch
        {
            var label when string.Equals(label, ResourceLookup.Resolve(successKey), StringComparison.Ordinal) => "#23835A",
            var label when string.Equals(label, ResourceLookup.Resolve(warningKey), StringComparison.Ordinal) => "#A26B16",
            _ => "#B83E48"
        };
        Latency = ResourceLookup.Resolve("activity.dash") == "-" ? $"{record.ElapsedMs} ms" : string.Format(System.Globalization.CultureInfo.CurrentCulture, ResourceLookup.Resolve("activity.latency.format"), record.ElapsedMs);
        ElapsedMs = record.ElapsedMs;
        DetailRoute = record.IncomingPath;
        Transform = Route;
        ResponseBytes = record.ResponseBytes > 0 ? string.Format(System.Globalization.CultureInfo.CurrentCulture, ResourceLookup.Resolve("activity.bytes.format"), record.ResponseBytes) : ResourceLookup.Resolve("activity.dash");
        ErrorType = record.ErrorType ?? ResourceLookup.Resolve("activity.dash");
        LogSummary = $"{record.Method} {record.IncomingPath}\nmodel: {ModelId}\nroute: {Route}\nstatus: {StatusCode}\nrequest_id: {RequestId}\nresponse_bytes: {ResponseBytes}";
    }

    internal static string LocalizeRoute(string route, System.Globalization.CultureInfo culture) => route switch
    {
        "OpenAI 直通" => ResourceLookup.Resolve("activity.route.openai.passthrough", culture),
        "Anthropic 直通" => ResourceLookup.Resolve("activity.route.anthropic.passthrough", culture),
        "Ollama 直通" => ResourceLookup.Resolve("activity.route.ollama.passthrough", culture),
        _ => route
    };

    public long Id { get; }
    public string RequestId { get; }
    public string Time { get; }
    public string ModelId { get; }
    public string ProviderId { get; }
    public string Protocol { get; }
    public string Route { get; }
    public int StatusCode { get; }
    public string StatusLabel { get; }
    public string StatusColor { get; }
    public string Latency { get; }
    public long ElapsedMs { get; }
    public string DetailRoute { get; }
    public string Transform { get; }
    public string ResponseBytes { get; }
    public string ErrorType { get; }
    public string LogSummary { get; }
    public static ActivityItemViewModel FromRecord(ActivityEventRecord record) => new(record);
}
