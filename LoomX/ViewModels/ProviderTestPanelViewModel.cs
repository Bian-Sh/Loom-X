using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using LoomX.Services;

namespace LoomX.ViewModels;

public sealed class ProviderTestPanelViewModel : NotifyViewModel
{
    private readonly IProviderTestService service;
    private ProviderEditorViewModel? provider;
    private ModelEditorViewModel? selectedModel;
    private ProviderTestMode selectedMode = ProviderTestMode.Regular;
    private string prompt = "每日一言";
    private string responseText = "";
    private ProviderTestSummary? summary;
    private bool isRunning;
    private bool hasResult;
    private bool hasError;
    private ProviderTestRequest? lastRequest;
    private CancellationTokenSource? cancellation;
    private long requestVersion;

    public ProviderTestPanelViewModel(IProviderTestService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        SendCommand = new AsyncCommand(SendAsync, () => CanSend);
        StopCommand = new DelegateCommand(Stop);
        RetryCommand = new AsyncCommand(RetryAsync, () => lastRequest is not null && !IsRunning);
        ClearCommand = new DelegateCommand(Clear);
    }

    public ModelEditorViewModel? SelectedModel { get => selectedModel; set { if (!SetProperty(ref selectedModel, value)) return; OnPropertyChanged(nameof(CanSend)); ((AsyncCommand)SendCommand).RaiseCanExecuteChanged(); } }
    public ProviderTestMode SelectedMode { get => selectedMode; set => SetProperty(ref selectedMode, value); }
    public string Prompt { get => prompt; set { if (!SetProperty(ref prompt, value ?? "")) return; OnPropertyChanged(nameof(CanSend)); ((AsyncCommand)SendCommand).RaiseCanExecuteChanged(); } }
    public string ResponseText { get => responseText; private set => SetProperty(ref responseText, value); }
    public ProviderTestSummary? Summary { get => summary; private set => SetProperty(ref summary, value); }
    public bool IsRunning { get => isRunning; private set { if (!SetProperty(ref isRunning, value)) return; OnPropertyChanged(nameof(CanSend)); ((AsyncCommand)SendCommand).RaiseCanExecuteChanged(); ((AsyncCommand)RetryCommand).RaiseCanExecuteChanged(); } }
    public bool HasResult { get => hasResult; private set => SetProperty(ref hasResult, value); }
    public bool HasError { get => hasError; private set => SetProperty(ref hasError, value); }
    public bool CanSend => provider is not null && SelectedModel is { Enabled: true, IsRealModel: true } && !string.IsNullOrWhiteSpace(Prompt) && !IsRunning;
    public ICommand SendCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand ClearCommand { get; }

    public void BindProvider(ProviderEditorViewModel? value)
    {
        Stop();
        provider = value;
        SelectedModel = provider?.Models.FirstOrDefault(model => model.IsRealModel && model.Enabled);
        Clear();
        OnPropertyChanged(nameof(CanSend));
        ((AsyncCommand)SendCommand).RaiseCanExecuteChanged();
    }

    public void RefreshLocalization() => OnPropertyChanged(nameof(Prompt));

    private async Task SendAsync() => await ExecuteAsync(BuildRequest());
    private async Task RetryAsync() { if (lastRequest is not null) await ExecuteAsync(lastRequest); }

    private async Task ExecuteAsync(ProviderTestRequest request)
    {
        var version = ++requestVersion;
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        IsRunning = true;
        HasResult = false;
        HasError = false;
        ResponseText = "";
        Summary = null;
        lastRequest = request;
        try
        {
            var progress = new Progress<ProviderTestProgress>(item =>
            {
                if (version != requestVersion) return;
                if (!string.IsNullOrEmpty(item.TextDelta)) ResponseText += item.TextDelta;
                if (item.Status is ProviderTestStatus.Failed or ProviderTestStatus.Cancelled) HasError = true;
            });
            var result = await service.ExecuteAsync(request, progress, cancellation.Token);
            if (version != requestVersion) return;
            Summary = result.Summary;
            ResponseText = result.ResponseText;
            HasError = result.Status == ProviderTestStatus.Failed;
            HasResult = result.Status == ProviderTestStatus.Completed;
        }
        finally
        {
            if (version == requestVersion) IsRunning = false;
        }
    }

    private ProviderTestRequest BuildRequest()
    {
        var current = provider ?? throw new InvalidOperationException("未绑定提供商。");
        var model = SelectedModel ?? throw new InvalidOperationException("未选择模型。");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!string.IsNullOrWhiteSpace(current.HeadersJson))
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(current.HeadersJson);
                if (parsed is not null) headers = parsed;
            }
        }
        catch (JsonException) { }
        return new ProviderTestRequest(Guid.NewGuid().ToString("N"), current.BusinessId, model.ModelId, current.BaseUrl, current.ApiMode, current.EndpointFormat, current.ApiKey, headers, current.UseProxy, Prompt, SelectedMode, 12000);
    }

    private void Stop()
    {
        ++requestVersion;
        cancellation?.Cancel();
        IsRunning = false;
    }

    private void Clear()
    {
        ResponseText = "";
        Summary = null;
        HasResult = false;
        HasError = false;
    }
}
