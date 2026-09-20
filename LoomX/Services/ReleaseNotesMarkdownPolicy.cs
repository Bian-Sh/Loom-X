using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace LoomX.Services;

/// <summary>在更新说明进入 Markdown 渲染器前移除可能触发外部内容或不安全导航的节点。</summary>
public static class ReleaseNotesMarkdownPolicy
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .Build();
    public static string Sanitize(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var document = Markdown.Parse(markdown, Pipeline);
        var edits = new List<SourceEdit>();

        foreach (var node in document.Descendants())
        {
            switch (node)
            {
                case HtmlBlock:
                case HtmlInline:
                    AddEdit(edits, node.Span, string.Empty, markdown.Length);
                    break;
                case LinkInline link when link.IsImage || !IsSafeHttps(link.Url):
                    AddEdit(edits, link.Span, GetVisibleText(markdown, link), markdown.Length);
                    break;
            }
        }

        if (edits.Count == 0) return markdown.Trim();

        var builder = new StringBuilder(markdown);
        foreach (var edit in RemoveContainedEdits(edits).OrderByDescending(item => item.Start))
        {
            builder.Remove(edit.Start, edit.Length);
            builder.Insert(edit.Start, edit.Replacement);
        }

        return builder.ToString().Trim();
    }

    private static bool IsSafeHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    private static string GetVisibleText(string source, LinkInline link)
    {
        var label = Slice(source, link.LabelSpan);
        if (string.IsNullOrEmpty(label)) label = link.Label ?? string.Empty;
        if (string.IsNullOrEmpty(label)) return string.Empty;

        var original = Slice(source, link.Span);
        return string.Equals(label, original, StringComparison.Ordinal)
            ? label
            : Sanitize(label);
    }

    private static string Slice(string source, SourceSpan span)
    {
        if (!IsValid(span, source.Length)) return string.Empty;
        return source.Substring(span.Start, span.End - span.Start + 1);
    }

    private static void AddEdit(List<SourceEdit> edits, SourceSpan span, string replacement, int sourceLength)
    {
        if (IsValid(span, sourceLength)) edits.Add(new SourceEdit(span.Start, span.End, replacement));
    }

    private static bool IsValid(SourceSpan span, int sourceLength) =>
        span.Start >= 0 && span.End >= span.Start && span.End < sourceLength;

    private static IEnumerable<SourceEdit> RemoveContainedEdits(IEnumerable<SourceEdit> edits)
    {
        SourceEdit? previous = null;
        foreach (var edit in edits.OrderBy(item => item.Start).ThenByDescending(item => item.End))
        {
            if (previous is { } outer && edit.End <= outer.End) continue;
            previous = edit;
            yield return edit;
        }
    }

    private readonly record struct SourceEdit(int Start, int End, string Replacement)
    {
        public int Length => End - Start + 1;
    }
}
