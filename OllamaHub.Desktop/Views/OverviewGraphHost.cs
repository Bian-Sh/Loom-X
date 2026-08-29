using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OllamaHub.Activity;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public interface IOverviewGraphHost : IDisposable
{
    void Attach(OverviewViewModel viewModel);
    void Detach();
    void Initialize();
}

public sealed class OverviewGraphHost(NativeWebView webView) : IOverviewGraphHost
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };
    private OverviewViewModel? viewModel;
    private bool initialized;
    private bool pageReady;
    private string? pendingTopology;

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        webView.NavigationCompleted += (_, _) =>
        {
            pageReady = true;
            FlushPending();
        };
        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Overview", "index.html");
        if (!File.Exists(htmlPath)) return;
        var baseUri = new Uri(AppContext.BaseDirectory.EndsWith(Path.DirectorySeparatorChar) ? AppContext.BaseDirectory : AppContext.BaseDirectory + Path.DirectorySeparatorChar);
        webView.NavigateToString(File.ReadAllText(htmlPath), baseUri);
    }

    public void Attach(OverviewViewModel next)
    {
        if (ReferenceEquals(viewModel, next)) return;
        Detach();
        viewModel = next;
        viewModel.TopologyChanged += OnTopologyChanged;
        viewModel.GraphTelemetryPublished += OnTelemetryPublished;
        pendingTopology = viewModel.TopologyJson;
        FlushPending();
    }

    public void Detach()
    {
        if (viewModel is null) return;
        viewModel.TopologyChanged -= OnTopologyChanged;
        viewModel.GraphTelemetryPublished -= OnTelemetryPublished;
        viewModel = null;
    }

    private void OnTopologyChanged(object? sender, EventArgs args)
    {
        if (viewModel is not null) pendingTopology = viewModel.TopologyJson;
        FlushPending();
    }

    private void OnTelemetryPublished(object? sender, RequestTelemetryEvent telemetryEvent)
    {
        if (!pageReady) return;
        Invoke($"window.receiveTelemetry({JsonSerializer.Serialize(telemetryEvent, JsonOptions)});");
    }

    private void FlushPending()
    {
        if (!pageReady || string.IsNullOrWhiteSpace(pendingTopology)) return;
        var json = JsonSerializer.Serialize(pendingTopology, JsonOptions);
        Invoke($"window.applyTopology({json});");
        pendingTopology = null;
    }

    private void Invoke(string script)
    {
        try { webView.InvokeScript(script); }
        catch (InvalidOperationException) { }
        catch (PlatformNotSupportedException) { }
    }

    public void Dispose()
    {
        Detach();
    }
}
