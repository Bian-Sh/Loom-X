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
    private ObservableStringBuilder markdown = null!;
    private bool isEmpty = true;
    private bool disposed;

    public ReleaseNotesContentViewModel()
    {
        RunOnUiThread(() => markdown = new ObservableStringBuilder());
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public string Title { get => title; private set => SetProperty(ref title, value); }
    public string PublishedAtText => release?.PublishedAt?.ToLocalTime().ToString("d", LocaleService.CurrentCulture) ?? string.Empty;
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
    public string EmptyText => ResourceLookup.Resolve("release.notes.empty", LocaleService.CurrentCulture);

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
            OnPropertyChanged(nameof(PublishedAtText));
            OnPropertyChanged(nameof(EmptyText));
        });
    }

    private void OnCultureChanged(object? sender, CultureInfo culture) => RunOnUiThread(() =>
    {
        OnPropertyChanged(nameof(PublishedAtText));
        OnPropertyChanged(nameof(EmptyText));
    });

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
