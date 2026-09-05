using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using OllamaHub.Activity;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop.ViewModels;

public sealed class ActivityViewModel : NotifyViewModel, IDisposable
{
    private readonly AppDataStore dataStore;
    private readonly ILogger<ActivityViewModel> logger;
    private readonly EventHandler storeActivityHandler;
    private CancellationTokenSource? refreshCancellation;
    private string searchText = string.Empty;
    private string selectedStatus = "全部状态";
    private string selectedProtocol = "全部入口协议";
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
    public IReadOnlyList<string> StatusOptions { get; } = ["全部状态", "成功", "失败", "警告"];
    public IReadOnlyList<string> ProtocolOptions { get; } = ["全部入口协议", "OpenAI", "Anthropic", "Ollama"];
    public event EventHandler? ScrollToTopRequested;
    public string SearchText { get => searchText; set { if (SetProperty(ref searchText, value ?? string.Empty)) QueueRefresh(); } }
    public string SelectedStatus { get => selectedStatus; set { if (SetProperty(ref selectedStatus, value ?? "全部状态")) QueueRefresh(); } }
    public string SelectedProtocol { get => selectedProtocol; set { if (SetProperty(ref selectedProtocol, value ?? "全部入口协议")) QueueRefresh(); } }
    public ActivityItemViewModel? SelectedItem { get => selectedItem; private set => SetProperty(ref selectedItem, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string ResultCountLabel => $"显示 {Items.Count} 条活动";
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
    public string PendingActivityLabel => PendingActivityCount > 0 ? $"有 {PendingActivityCount} 条新活动，回到最新" : "回到最新";
    public double PullDistance { get => pullDistance; private set { if (SetProperty(ref pullDistance, value)) OnPropertyChanged(nameof(IsPullToRefreshVisible)); } }
    public bool IsPullToRefreshVisible => PullDistance >= 24 || IsLoadingMore;
    public string LoadMoreLabel => IsLoadingMore ? "正在加载历史活动…" : HasMore ? "继续上拉加载更早活动" : "已到活动历史末尾";
    public ICommand SelectCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public ICommand ReturnToLatestCommand { get; }

    public ActivityViewModel(AppDataStore dataStore, ILogger<ActivityViewModel>? logger = null)
    {
        this.dataStore = dataStore;
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ActivityViewModel>.Instance;
        SelectCommand = new AsyncCommand(parameter => { SelectedItem = parameter as ActivityItemViewModel; return Task.CompletedTask; });
        LoadMoreCommand = new AsyncCommand(_ => LoadMoreAsync());
        ReturnToLatestCommand = new AsyncCommand(_ => ReturnToLatestAsync());
        storeActivityHandler = (_, _) => OnStoreActivityChanged();
        dataStore.ActivityWindowChanged += storeActivityHandler;
        _ = RefreshAsync();
    }

    public ActivityViewModel(GatewayProcessService gatewayService, ILogger<ActivityViewModel>? logger = null)
        : this(new AppDataStore(new ConfigSnapshotService(), gatewayService), logger) { }

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
            Status = page.Items.Count == 0 ? "暂无请求活动" : $"已加载 {page.Items.Count} 条活动";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = $"活动加载失败：{exception.Message}";
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
            Status = page.HasMore ? $"已加载 {page.Items.Count} 条活动，可继续查看历史" : $"已加载 {page.Items.Count} 条活动，已到历史末尾";
        }
        catch (Exception exception)
        {
            Status = $"历史活动加载失败：{exception.Message}";
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
            Status = page.Items.Count == 0 ? "暂无请求活动" : "已回到最新活动";
        }
        catch (Exception exception)
        {
            Status = $"返回最新活动失败：{exception.Message}";
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

    private ActivityQuery BuildQuery() => new(SearchText, ToStatusValue(SelectedStatus), ToProtocolValue(SelectedProtocol), AppDataStore.ActivityWindowLimit);

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
        P95Latency = values.Length == 0 ? "—" : $"{values[(int)Math.Ceiling(values.Length * .95) - 1]} ms";
    }

    private static string? ToStatusValue(string value) => value switch { "成功" => "ok", "失败" => "fail", "警告" => "warn", _ => null };
    private static string? ToProtocolValue(string value) => value is "全部入口协议" ? null : value;

    public void Dispose()
    {
        dataStore.ActivityWindowChanged -= storeActivityHandler;
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
        ModelId = string.IsNullOrWhiteSpace(record.ModelId) ? "未识别模型" : record.ModelId;
        ProviderId = string.IsNullOrWhiteSpace(record.ProviderId) ? "未匹配 Provider" : record.ProviderId;
        Route = record.Route;
        Protocol = record.Protocol;
        StatusCode = record.StatusCode;
        StatusLabel = record.StatusCode is >= 200 and < 300 ? "成功" : record.StatusCode is >= 400 and < 500 ? "警告" : "失败";
        StatusColor = StatusLabel switch { "成功" => "#23835A", "警告" => "#A26B16", _ => "#B83E48" };
        Latency = $"{record.ElapsedMs} ms";
        ElapsedMs = record.ElapsedMs;
        DetailRoute = record.IncomingPath;
        Transform = record.Route;
        ResponseBytes = record.ResponseBytes > 0 ? $"{record.ResponseBytes:N0} B" : "—";
        ErrorType = record.ErrorType ?? "—";
        LogSummary = $"{record.Method} {record.IncomingPath}\nmodel: {ModelId}\nroute: {Route}\nstatus: {StatusCode}\nrequest_id: {RequestId}\nresponse_bytes: {ResponseBytes}";
    }

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
