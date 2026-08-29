using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using OllamaHub.Activity;
using OllamaHub.Desktop.Services;

namespace OllamaHub.Desktop.ViewModels;

public sealed class ActivityViewModel : NotifyViewModel, IDisposable
{
    private readonly ActivityQueryService queryService = new();
    private readonly GatewayProcessService gatewayService;
    private readonly EventHandler<ActivityEventInput> activityHandler;
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

    public ObservableCollection<ActivityItemViewModel> Items { get; } = [];
    public IReadOnlyList<string> StatusOptions { get; } = ["全部状态", "成功", "失败", "警告"];
    public IReadOnlyList<string> ProtocolOptions { get; } = ["全部入口协议", "OpenAI", "Anthropic", "Ollama"];
    public string SearchText { get => searchText; set { if (SetProperty(ref searchText, value ?? string.Empty)) _ = RefreshAsync(); } }
    public string SelectedStatus { get => selectedStatus; set { if (SetProperty(ref selectedStatus, value ?? "全部状态")) _ = RefreshAsync(); } }
    public string SelectedProtocol { get => selectedProtocol; set { if (SetProperty(ref selectedProtocol, value ?? "全部入口协议")) _ = RefreshAsync(); } }
    public ActivityItemViewModel? SelectedItem { get => selectedItem; private set => SetProperty(ref selectedItem, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string ResultCountLabel => $"显示 {Items.Count} 条活动";
    public int TotalCount { get => totalCount; private set => SetProperty(ref totalCount, value); }
    public int ConversionCount { get => conversionCount; private set => SetProperty(ref conversionCount, value); }
    public int FailureCount { get => failureCount; private set => SetProperty(ref failureCount, value); }
    public string P95Latency { get => p95Latency; private set => SetProperty(ref p95Latency, value); }
    public ICommand SelectCommand { get; }

    public ActivityViewModel(GatewayProcessService gatewayService)
    {
        this.gatewayService = gatewayService;
        SelectCommand = new AsyncCommand(parameter => { SelectedItem = parameter as ActivityItemViewModel; return Task.CompletedTask; });
        activityHandler = (_, input) => Dispatcher.UIThread.Post(() => AddPushedActivity(input));
        gatewayService.ActivityEnqueued += activityHandler;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (isRefreshing) return;
        isRefreshing = true;
        try
        {
            var selectedId = SelectedItem?.Id;
            var records = await queryService.QueryAsync(new ActivityQuery(SearchText, ToStatusValue(SelectedStatus), ToProtocolValue(SelectedProtocol)));
            Items.Clear();
            foreach (var record in records) Items.Add(ActivityItemViewModel.FromRecord(record));
            SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId) ?? Items.FirstOrDefault();
            UpdateSummary(records);
            Status = records.Count == 0 ? "暂无请求活动" : $"已加载 {records.Count} 条活动";
        }
        catch (Exception exception)
        {
            Status = $"活动加载失败：{exception.Message}";
        }
        finally { isRefreshing = false; }
    }

    private void UpdateSummary(IReadOnlyList<ActivityEventRecord> records)
    {
        TotalCount = records.Count;
        ConversionCount = records.Count(item => item.Route.Contains('→'));
        FailureCount = records.Count(item => item.StatusCode >= 500);
        var values = records.Select(item => item.ElapsedMs).OrderBy(item => item).ToArray();
        P95Latency = values.Length == 0 ? "—" : $"{values[(int)Math.Ceiling(values.Length * .95) - 1]} ms";
        OnPropertyChanged(nameof(ResultCountLabel));
    }

    private static string? ToStatusValue(string value) => value switch { "成功" => "ok", "失败" => "fail", "警告" => "warn", _ => null };
    private static string? ToProtocolValue(string value) => value is "全部入口协议" ? null : value;

    private void AddPushedActivity(ActivityEventInput input)
    {
        var item = ActivityItemViewModel.FromInput(input);
        if (!MatchesFilters(item)) return;
        Items.Insert(0, item);
        while (Items.Count > 500) Items.RemoveAt(Items.Count - 1);
        UpdateSummaryFromItems();
        Status = $"已收到新请求 · {Items.Count} 条活动";
    }

    private bool MatchesFilters(ActivityItemViewModel item)
    {
        var statusMatches = SelectedStatus switch
        {
            "成功" => item.StatusLabel == "成功",
            "失败" => item.StatusLabel == "失败",
            "警告" => item.StatusLabel == "警告",
            _ => true
        };
        if (!statusMatches || (SelectedProtocol is not "全部入口协议" && item.Protocol != SelectedProtocol)) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var search = SearchText.Trim();
        return item.RequestId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.ProviderId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.ModelId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Route.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSummaryFromItems()
    {
        TotalCount = Items.Count;
        ConversionCount = Items.Count(item => item.Route.Contains('→'));
        FailureCount = Items.Count(item => item.StatusCode >= 500);
        var values = Items.Select(item => item.ElapsedMs).OrderBy(item => item).ToArray();
        P95Latency = values.Length == 0 ? "—" : $"{values[(int)Math.Ceiling(values.Length * .95) - 1]} ms";
        OnPropertyChanged(nameof(ResultCountLabel));
    }

    public void Dispose()
    {
        gatewayService.ActivityEnqueued -= activityHandler;
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
    public static ActivityItemViewModel FromInput(ActivityEventInput input) => new(new ActivityEventRecord(0, input.CreatedAt, input.RequestId, input.Method, input.IncomingPath, input.Protocol, input.Route, input.ProviderId, input.ModelId, input.StatusCode, input.ElapsedMs, input.ResponseBytes, input.IsStreaming, input.ErrorType));
}
