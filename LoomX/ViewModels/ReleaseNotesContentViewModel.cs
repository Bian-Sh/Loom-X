using System.Globalization;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;
using LoomX.Localization;
using LoomX.Services;

namespace LoomX.ViewModels;

/// <summary>把单个正式版本投影为可安全交给共享 Markdown 视图的内容。</summary>
public sealed class ReleaseNotesContentViewModel : NotifyViewModel, IDisposable
{
    private UpdateRelease? release;
    private string title = string.Empty;
    private string publishedAtText = string.Empty;
    private ObservableStringBuilder markdown = null!;
    private bool isEmpty = true;
    private string emptyText = string.Empty;
    private bool disposed;

    public ReleaseNotesContentViewModel()
    {
        RunOnUiThread(() => markdown = new ObservableStringBuilder());
        RefreshLocalizedText(LocaleService.CurrentCulture);
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public string Title { get => title; private set => SetProperty(ref title, value); }
    public string PublishedAtText { get => publishedAtText; private set => SetProperty(ref publishedAtText, value); }
    public ObservableStringBuilder Markdown { get => markdown; private set => SetProperty(ref markdown, value); }
    public bool IsEmpty
    {
        get => isEmpty;
        private set
        {
            if (SetProperty(ref isEmpty, value)) OnPropertyChanged(nameof(HasContent));
        }
    }
    public bool HasContent => !IsEmpty;
    public string EmptyText { get => emptyText; private set => SetProperty(ref emptyText, value); }

    public void SetRelease(UpdateRelease? value)
    {
        var sanitized = ReleaseNotesMarkdownPolicy.Sanitize(value?.Body);
        RunOnUiThread(() =>
        {
            release = value;
            Title = value is null ? string.Empty : $"v{value.Version}";

            var nextMarkdown = new ObservableStringBuilder();
            if (!string.IsNullOrWhiteSpace(sanitized)) nextMarkdown.Append(sanitized);
            Markdown = nextMarkdown;
            IsEmpty = string.IsNullOrWhiteSpace(sanitized);
            RefreshLocalizedText(LocaleService.CurrentCulture);
        });
    }

    private void OnCultureChanged(object? sender, CultureInfo culture) =>
        RunOnUiThread(() => RefreshLocalizedText(culture, notifyEmptyText: true));

    private void RefreshLocalizedText(CultureInfo culture, bool notifyEmptyText = false)
    {
        PublishedAtText = release?.PublishedAt?.ToLocalTime().ToString("d", culture) ?? string.Empty;
        var nextEmptyText = ResourceLookup.Resolve("release.notes.empty", culture);
        if (!SetProperty(ref emptyText, nextEmptyText, nameof(EmptyText)) && notifyEmptyText)
            OnPropertyChanged(nameof(EmptyText));
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        LocaleService.CultureChanged -= OnCultureChanged;
    }
}
