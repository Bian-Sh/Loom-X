using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;
using LoomX.Localization;
using LoomX.Services;

namespace LoomX.ViewModels;

/// <summary>单个可折叠的 Release Notes 模块。</summary>
public sealed class ReleaseNoteSectionViewModel : NotifyViewModel
{
    private bool isExpanded = true;

    public ReleaseNoteSectionViewModel(string title, ObservableStringBuilder markdown)
    {
        Title = title;
        Markdown = markdown;
        HeadingMarkdown = new ObservableStringBuilder();
        HeadingMarkdown.Append($"## {title}");
    }

    public string Title { get; }
    public ObservableStringBuilder HeadingMarkdown { get; }
    public ObservableStringBuilder Markdown { get; }
    public bool IsExpanded { get => isExpanded; set => SetProperty(ref isExpanded, value); }
}

/// <summary>把单个正式版本投影为可安全交给共享 Markdown 视图的内容。</summary>
public sealed class ReleaseNotesContentViewModel : NotifyViewModel, IDisposable
{
    private static readonly HashSet<string> FoldoutTitles = new(StringComparer.Ordinal)
    {
        "🐞 \u4FEE\u590D\u95EE\u9898",
        "✨ \u65B0\u589E\u529F\u80FD",
        "🚀 \u4F18\u5316\u6539\u8FDB"
    };

    private UpdateRelease? release;
    private string title = string.Empty;
    private ObservableStringBuilder markdown = null!;
    private bool isEmpty = true;
    private bool isSectioned;
    private bool disposed;

    public ReleaseNotesContentViewModel()
    {
        RunOnUiThread(() => markdown = new ObservableStringBuilder());
        LocaleService.CultureChanged += OnCultureChanged;
    }

    public string Title { get => title; private set => SetProperty(ref title, value); }
    public string PublishedAtText => release?.PublishedAt?.ToLocalTime().ToString("d", LocaleService.CurrentCulture) ?? string.Empty;
    public string ReleaseUrl => release?.HtmlUrl ?? string.Empty;
    public ObservableStringBuilder Markdown { get => markdown; private set => SetProperty(ref markdown, value); }
    public ObservableCollection<ReleaseNoteSectionViewModel> Sections { get; } = [];
    public bool IsSectioned
    {
        get => isSectioned;
        private set
        {
            if (SetProperty(ref isSectioned, value)) OnPropertyChanged(nameof(HasUnsectionedContent));
        }
    }
    public bool IsEmpty
    {
        get => isEmpty;
        private set
        {
            if (!SetProperty(ref isEmpty, value)) return;
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(HasUnsectionedContent));
        }
    }
    public bool HasContent => !IsEmpty;
    public bool HasUnsectionedContent => HasContent && !IsSectioned;
    public string EmptyText => ResourceLookup.Resolve("release.notes.empty", LocaleService.CurrentCulture);

    public void SetRelease(UpdateRelease? value)
    {
        var sanitized = ReleaseNotesMarkdownPolicy.Sanitize(value?.Body);
        var sectionContents = SplitSections(sanitized);
        RunOnUiThread(() =>
        {
            release = value;
            Title = value is null ? string.Empty : $"v{value.Version}";

            var nextMarkdown = new ObservableStringBuilder();
            if (!string.IsNullOrWhiteSpace(sanitized)) nextMarkdown.Append(sanitized);
            Markdown = nextMarkdown;

            Sections.Clear();
            foreach (var sectionContent in sectionContents)
            {
                var sectionMarkdown = new ObservableStringBuilder();
                if (!string.IsNullOrWhiteSpace(sectionContent.Markdown)) sectionMarkdown.Append(sectionContent.Markdown);
                Sections.Add(new ReleaseNoteSectionViewModel(sectionContent.Title, sectionMarkdown));
            }

            IsSectioned = Sections.Count > 0;
            IsEmpty = string.IsNullOrWhiteSpace(sanitized);
            OnPropertyChanged(nameof(PublishedAtText));
            OnPropertyChanged(nameof(ReleaseUrl));
            OnPropertyChanged(nameof(EmptyText));
        });
    }

    private static IReadOnlyList<SectionContent> SplitSections(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var sections = new List<SectionContent>();
        var preamble = new StringBuilder();
        StringBuilder? currentBody = null;
        string? currentTitle = null;

        foreach (var line in normalized.Split('\n'))
        {
            if (TryGetFoldoutTitle(line, out var foldoutTitle))
            {
                if (currentTitle is not null && currentBody is not null)
                    sections.Add(new SectionContent(currentTitle, currentBody.ToString().Trim()));

                currentTitle = foldoutTitle;
                currentBody = new StringBuilder();
                if (sections.Count == 0 && preamble.Length > 0)
                {
                    currentBody.AppendLine(preamble.ToString().Trim());
                    currentBody.AppendLine();
                }
                continue;
            }

            if (currentBody is null) preamble.AppendLine(line);
            else currentBody.AppendLine(line);
        }

        if (currentTitle is not null && currentBody is not null)
            sections.Add(new SectionContent(currentTitle, currentBody.ToString().Trim()));

        return sections;
    }

    private static bool TryGetFoldoutTitle(string line, out string title)
    {
        title = string.Empty;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("## ", StringComparison.Ordinal)) return false;

        var candidate = trimmed[3..].Trim().TrimEnd('#').Trim();
        if (!FoldoutTitles.Contains(candidate)) return false;
        title = candidate;
        return true;
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

    private sealed record SectionContent(string Title, string Markdown);
}
