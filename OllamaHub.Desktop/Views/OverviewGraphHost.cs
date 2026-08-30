using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using OllamaHub.Activity;
using OllamaHub.Desktop.ViewModels;

namespace OllamaHub.Desktop.Views;

public interface IOverviewGraphHost : IDisposable
{
    void Attach(OverviewViewModel viewModel);
    void Detach();
    void Initialize();
}

public sealed class OverviewGraphHost(NativeWebView webView, ILogger<OverviewGraphHost> logger) : IOverviewGraphHost
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };
    private OverviewViewModel? viewModel;
    private bool initialized;
    private bool pageReady;
    private bool flushingTopology;
    private string? pendingTopology;
    private HttpListener? assetServer;
    private CancellationTokenSource? assetServerCancellation;

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        webView.NavigationCompleted += OnNavigationCompleted;
        webView.WebMessageReceived += OnWebMessageReceived;
        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Overview", "index.html");
        logger.LogInformation("概览 WebView 初始化，资源路径 {HtmlPath}，文件存在 {Exists}", htmlPath, File.Exists(htmlPath));
        if (!File.Exists(htmlPath)) return;
        var assetDirectory = Path.GetDirectoryName(Path.GetFullPath(htmlPath))!;
        var port = GetFreeLoopbackPort();
        assetServer = new HttpListener();
        assetServer.Prefixes.Add($"http://127.0.0.1:{port}/");
        assetServer.Start();
        assetServerCancellation = new CancellationTokenSource();
        _ = ServeAssetsAsync(assetServer, assetDirectory, assetServerCancellation.Token);
        var pageUri = new Uri($"http://127.0.0.1:{port}/index.html");
        logger.LogInformation("概览 WebView 启动本地资源服务 {AssetDirectory}，地址 {PageUri}", assetDirectory, pageUri);
        webView.Navigate(pageUri);
    }

    private static int GetFreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private async Task ServeAssetsAsync(HttpListener listener, string assetDirectory, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(assetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = ServeAssetRequestAsync(context, root);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "概览本地资源服务异常"); }
    }

    private async Task ServeAssetRequestAsync(HttpListenerContext context, string root)
    {
        try
        {
            var relativePath = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty);
            var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }
            var bytes = await File.ReadAllBytesAsync(path);
            context.Response.ContentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                _ => "application/octet-stream"
            };
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception exception) { logger.LogWarning(exception, "概览本地资源请求失败 {Path}", context.Request.Url?.AbsolutePath); }
        finally { context.Response.Close(); }
    }

    public void Attach(OverviewViewModel next)
    {
        if (ReferenceEquals(viewModel, next)) return;
        Detach();
        viewModel = next;
        viewModel.TopologyChanged += OnTopologyChanged;
        viewModel.GraphTelemetryPublished += OnTelemetryPublished;
        pendingTopology = viewModel.TopologyJson;
        logger.LogInformation("概览宿主绑定 ViewModel，拓扑 {TopologyLength} 字节", pendingTopology.Length);
        _ = FlushPendingAsync();
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
        logger.LogDebug("概览拓扑变更，待发送 {TopologyLength} 字节，页面就绪 {PageReady}", pendingTopology?.Length ?? 0, pageReady);
        _ = FlushPendingAsync();
    }

    private void OnTelemetryPublished(object? sender, RequestTelemetryEvent telemetryEvent)
    {
        logger.LogDebug("概览收到遥测 {Kind} {RequestId} {EndpointKey}/{ProviderId}/{ModelId}，页面就绪 {PageReady}", telemetryEvent.Kind, telemetryEvent.RequestId, telemetryEvent.EndpointKey, telemetryEvent.ProviderId, telemetryEvent.ModelId, pageReady);
        if (!pageReady) return;
        _ = InvokeAsync($"window.receiveTelemetry({JsonSerializer.Serialize(telemetryEvent, JsonOptions)});");
    }

    private async Task FlushPendingAsync()
    {
        if (!pageReady || flushingTopology || string.IsNullOrWhiteSpace(pendingTopology))
        {
            logger.LogDebug("概览跳过拓扑发送，页面就绪 {PageReady}、发送中 {Flushing}、有待发送数据 {HasPending}", pageReady, flushingTopology, !string.IsNullOrWhiteSpace(pendingTopology));
            return;
        }
        flushingTopology = true;
        var topologyJson = pendingTopology;
        var json = JsonSerializer.Serialize(topologyJson, JsonOptions);
        try
        {
            var result = await webView.InvokeScript($"window.applyTopology({json});");
            logger.LogInformation("概览拓扑已发送 {TopologyLength} 字节，脚本返回长度 {ResultLength}", topologyJson.Length, result?.Length ?? 0);
            await LogPageDiagnosticsAsync("拓扑发送后");
            if (string.Equals(pendingTopology, topologyJson, StringComparison.Ordinal)) pendingTopology = null;
        }
        catch (InvalidOperationException exception) { logger.LogWarning(exception, "概览 WebView 当前不可调用脚本"); }
        catch (PlatformNotSupportedException exception) { logger.LogWarning(exception, "概览 WebView 平台不支持脚本调用"); }
        catch (Exception exception) { logger.LogError(exception, "概览 WebView 拓扑脚本调用失败"); }
        finally
        {
            flushingTopology = false;
            if (pageReady && !string.IsNullOrWhiteSpace(pendingTopology)) _ = FlushPendingAsync();
        }
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs args)
    {
        logger.LogDebug("概览收到网页消息，长度 {BodyLength}", args.Body?.Length ?? 0);
        if (string.Equals(args.Body, "graph-ready", StringComparison.Ordinal))
        {
            pageReady = true;
            logger.LogInformation("概览网页报告 graph-ready");
            _ = FlushPendingAsync();
            return;
        }
        if (args.Body?.StartsWith("graph-debug:", StringComparison.Ordinal) == true)
        {
            logger.LogInformation("概览网页诊断 {Diagnostics}", args.Body["graph-debug:".Length..]);
        }
        else if (args.Body?.StartsWith("graph-error:", StringComparison.Ordinal) == true)
        {
            logger.LogError("概览网页脚本错误 {Error}", args.Body["graph-error:".Length..]);
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        // WebView2 某些版本不转发网页消息，导航完成后仍需刷新首个快照。
        pageReady = true;
        logger.LogInformation("概览 WebView 导航完成，使用导航兜底标记页面就绪");
        _ = FlushPendingAsync();
    }

    private async Task InvokeAsync(string script)
    {
        try { await webView.InvokeScript(script); }
        catch (InvalidOperationException exception) { logger.LogWarning(exception, "概览 WebView 遥测脚本调用失败"); }
        catch (PlatformNotSupportedException exception) { logger.LogWarning(exception, "概览 WebView 平台不支持遥测脚本调用"); }
        catch (Exception exception) { logger.LogError(exception, "概览 WebView 遥测脚本调用异常"); }
    }

    private async Task LogPageDiagnosticsAsync(string reason)
    {
        try
        {
            var result = await webView.InvokeScript("window.__overviewDiagnostics ? JSON.stringify(window.__overviewDiagnostics()) : 'missing'");
            logger.LogInformation("概览网页诊断 {Reason}: {Diagnostics}", reason, result ?? "null");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "概览网页诊断读取失败 {Reason}", reason);
        }
    }

    public void Dispose()
    {
        Detach();
        assetServerCancellation?.Cancel();
        assetServer?.Stop();
        assetServer?.Close();
        assetServerCancellation?.Dispose();
        webView.NavigationCompleted -= OnNavigationCompleted;
        webView.WebMessageReceived -= OnWebMessageReceived;
    }
}
