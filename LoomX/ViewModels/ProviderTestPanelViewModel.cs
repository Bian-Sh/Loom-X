using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using LoomX.Services;

namespace LoomX.ViewModels;

public sealed class ProviderTestPanelViewModel : NotifyViewModel
{
    private const int MaxDisplayCharacters = 1_000_000;
    private const int MaxLivePreviewCharacters = 32_768;
    private static readonly TimeSpan ProgressFlushInterval = TimeSpan.FromMilliseconds(75);
    private readonly IProviderTestService service;
    private readonly ObservableCollection<ModelEditorViewModel> testableModels = [];
    private readonly HashSet<ModelEditorViewModel> subscribedModels = [];
    private ProviderEditorViewModel? provider;
    private ModelEditorViewModel? selectedModel;
    private ProviderTestMode selectedMode = ProviderTestMode.Regular;
    private string prompt = "每日一言";
    private string responseText = "";
    private string requestSummary = "";
    private ProviderTestSummary? summary;
    private bool isRunning;
    private bool hasResult;
    private bool hasError;
    private CancellationTokenSource? cancellation;
    private long requestVersion;

    public ProviderTestPanelViewModel(IProviderTestService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        TestableModels = new ReadOnlyObservableCollection<ModelEditorViewModel>(testableModels);
        SendCommand = new AsyncCommand(SendAsync, () => CanSend);
        StopCommand = new AsyncCommand(StopAsync, () => IsRunning);
    }

    public ReadOnlyObservableCollection<ModelEditorViewModel> TestableModels { get; }
    public bool HasTestableModels => testableModels.Count > 0;

    public ModelEditorViewModel? SelectedModel
    {
        get => selectedModel;
        set
        {
            if (!SetProperty(ref selectedModel, value)) return;
            OnPropertyChanged(nameof(CanSend));
            ((AsyncCommand)SendCommand).RaiseCanExecuteChanged();
        }
    }

    public ProviderTestMode SelectedMode { get => selectedMode; set => SetProperty(ref selectedMode, value); }

    public string Prompt
    {
        get => prompt;
        set
        {
            if (!SetProperty(ref prompt, value ?? "")) return;
            OnPropertyChanged(nameof(CanSend));
            ((AsyncCommand)SendCommand).RaiseCanExecuteChanged();
        }
    }

    public string ResponseText
    {
        get => responseText;
        private set
        {
            if (!SetProperty(ref responseText, value)) return;
            OnPropertyChanged(nameof(HasResponseText));
        }
    }
    public bool HasResponseText => !string.IsNullOrEmpty(ResponseText);
    public string RequestSummary { get => requestSummary; private set => SetProperty(ref requestSummary, value); }
    public ProviderTestSummary? Summary { get => summary; private set => SetProperty(ref summary, value); }

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (!SetProperty(ref isRunning, value)) return;
            OnPropertyChanged(nameof(CanSend));
            ((AsyncCommand)SendCommand).RaiseCanExecuteChanged();
            ((AsyncCommand)StopCommand).RaiseCanExecuteChanged();
        }
    }

    public bool HasResult { get => hasResult; private set => SetProperty(ref hasResult, value); }
    public bool HasError { get => hasError; private set => SetProperty(ref hasError, value); }
    public bool CanSend => provider is not null && SelectedModel is { IsRealModel: true } model && testableModels.Contains(model) && !string.IsNullOrWhiteSpace(Prompt) && !IsRunning;
    public ICommand SendCommand { get; }
    public ICommand StopCommand { get; }

    public void BindProvider(ProviderEditorViewModel? value)
    {
        CancelPendingRequest();
        UnsubscribeProvider();

        provider = value;
        if (provider is not null)
        {
            provider.PropertyChanged += ProviderOnPropertyChanged;
            provider.Models.CollectionChanged += ProviderModelsOnCollectionChanged;
            UpdateModelSubscriptions();
        }

        RefreshTestableModels();
        Clear();
        RefreshRequestSummary();
        OnPropertyChanged(nameof(CanSend));
        ((AsyncCommand)SendCommand).RaiseCanExecuteChanged();
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Prompt));
        RefreshRequestSummary();
    }

    private async Task SendAsync() => await ExecuteAsync(BuildRequest());

    private Task StopAsync()
    {
        if (IsRunning)
            cancellation?.Cancel();
        return Task.CompletedTask;
    }

    private async Task ExecuteAsync(ProviderTestRequest request)
    {
        var version = ++requestVersion;
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        var currentCancellation = cancellation;
        IsRunning = true;
        HasResult = false;
        HasError = false;
        ResponseText = "";
        Summary = null;
        try
        {
            var progress = new BufferedProviderTestProgress();
            var executionTask = Task.Run(() => service.ExecuteAsync(request, progress, currentCancellation.Token));
            while (!executionTask.IsCompleted)
            {
                await Task.WhenAny(executionTask, Task.Delay(ProgressFlushInterval));
                FlushProgress(progress, version);
            }

            var result = await executionTask;
            FlushProgress(progress, version);
            if (version != requestVersion) return;
            Summary = result.Summary;
            ResponseText = result.ResponseText;
            HasError = result.Status == ProviderTestStatus.Failed;
            HasResult = result.Status is ProviderTestStatus.Completed or ProviderTestStatus.Failed;
        }
        finally
        {
            currentCancellation.Dispose();
            if (version == requestVersion)
            {
                if (ReferenceEquals(cancellation, currentCancellation))
                    cancellation = null;
                IsRunning = false;
            }
        }
    }

    private void FlushProgress(BufferedProviderTestProgress progress, long version)
    {
        var pending = progress.Drain();
        if (version != requestVersion) return;
        if (!string.IsNullOrEmpty(pending.TextDelta))
        {
            var remaining = Math.Max(0, MaxLivePreviewCharacters - ResponseText.Length);
            if (remaining > 0)
                ResponseText += pending.TextDelta[..Math.Min(remaining, pending.TextDelta.Length)];
        }
        if (pending.HasFailure)
            HasError = true;
    }

    private ProviderTestRequest BuildRequest()
    {
        var current = provider ?? throw new InvalidOperationException("未绑定提供商。");
        var model = SelectedModel ?? throw new InvalidOperationException("未选择模型。");
        return new ProviderTestRequest(
            Guid.NewGuid().ToString("N"),
            current.BusinessId,
            model.ModelId,
            current.BaseUrl,
            current.ApiMode,
            current.EndpointFormat,
            current.ApiKey,
            ParseHeaders(current.HeadersJson),
            current.UseProxy,
            Prompt,
            SelectedMode,
            MaxDisplayCharacters);
    }

    private void ProviderModelsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UpdateModelSubscriptions();
        RefreshTestableModels();
    }

    private void ModelOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ModelEditorViewModel.Enabled))
            RefreshTestableModels();
    }

    private void UpdateModelSubscriptions()
    {
        var currentModels = provider?.Models.ToHashSet() ?? [];
        foreach (var model in subscribedModels.Except(currentModels).ToArray())
        {
            model.PropertyChanged -= ModelOnPropertyChanged;
            subscribedModels.Remove(model);
        }

        foreach (var model in currentModels.Except(subscribedModels))
        {
            model.PropertyChanged += ModelOnPropertyChanged;
            subscribedModels.Add(model);
        }
    }

    private void RefreshTestableModels()
    {
        var previousSelection = SelectedModel;
        var available = provider?.Models.Where(model => model.IsRealModel && model.Enabled).ToArray() ?? [];

        testableModels.Clear();
        foreach (var model in available)
            testableModels.Add(model);

        OnPropertyChanged(nameof(HasTestableModels));
        SelectedModel = previousSelection is not null && available.Contains(previousSelection)
            ? previousSelection
            : available.FirstOrDefault();
    }

    private void UnsubscribeProvider()
    {
        if (provider is not null)
        {
            provider.PropertyChanged -= ProviderOnPropertyChanged;
            provider.Models.CollectionChanged -= ProviderModelsOnCollectionChanged;
        }

        foreach (var model in subscribedModels)
            model.PropertyChanged -= ModelOnPropertyChanged;
        subscribedModels.Clear();
    }

    private void ProviderOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProviderEditorViewModel.BaseUrl)
            or nameof(ProviderEditorViewModel.ApiMode)
            or nameof(ProviderEditorViewModel.EndpointFormat)
            or nameof(ProviderEditorViewModel.UseProxy)
            or nameof(ProviderEditorViewModel.HeadersJson)
            or nameof(ProviderEditorViewModel.CurrentCliIdentitySummary))
        {
            RefreshRequestSummary();
        }
    }

    private void RefreshRequestSummary()
    {
        var current = provider;
        if (current is null)
        {
            RequestSummary = "";
            return;
        }

        var endpoint = Uri.TryCreate(current.BaseUrl.Trim(), UriKind.Absolute, out _)
            ? $"POST {ProviderTestService.ResolveEndpoint(current.BaseUrl, current.ApiMode, current.EndpointFormat)}"
            : "POST";
        var parts = new List<string>
        {
            endpoint,
            current.UseProxy ? "proxy" : "direct",
            $"{ParseHeaders(current.HeadersJson).Count} Headers",
        };
        if (current.CurrentCliIdentity is not null)
            parts.Add(current.CurrentCliIdentitySummary);
        RequestSummary = string.Join(" · ", parts);
    }

    private static Dictionary<string, string> ParseHeaders(string? headersJson)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(headersJson))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void CancelPendingRequest()
    {
        ++requestVersion;
        var pendingCancellation = cancellation;
        cancellation = null;
        pendingCancellation?.Cancel();
        IsRunning = false;
    }

    internal bool DeleteResponseSelection(int selectionStart, int selectionEnd)
    {
        if (IsRunning || ResponseText.Length == 0) return false;

        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, ResponseText.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, ResponseText.Length);
        if (start == end) return false;

        if (start == 0 && end == ResponseText.Length)
        {
            Clear();
            return true;
        }

        ResponseText = ResponseText.Remove(start, end - start);
        return true;
    }

    private void Clear()
    {
        ResponseText = "";
        Summary = null;
        HasResult = false;
        HasError = false;
    }
    private sealed class BufferedProviderTestProgress : IProgress<ProviderTestProgress>
    {
        private readonly object gate = new();
        private readonly StringBuilder pendingText = new();
        private bool hasFailure;

        public void Report(ProviderTestProgress value)
        {
            lock (gate)
            {
                if (!string.IsNullOrEmpty(value.TextDelta))
                    pendingText.Append(value.TextDelta);
                if (value.Status == ProviderTestStatus.Failed)
                    hasFailure = true;
            }
        }

        public (string TextDelta, bool HasFailure) Drain()
        {
            lock (gate)
            {
                var text = pendingText.ToString();
                pendingText.Clear();
                var failed = hasFailure;
                hasFailure = false;
                return (text, failed);
            }
        }
    }
}
