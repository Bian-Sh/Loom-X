using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using LiveMarkdown.Avalonia;
using Markdig.Syntax.Inlines;

namespace LoomX.Assistant;

/// <summary>为助手 Markdown 显式分离普通文字与彩色 Emoji 的字体运行段。</summary>
public static class AssistantMarkdownTypography
{
    internal static readonly FontFamily TextFontFamily = new("Segoe UI, Microsoft YaHei UI, Arial, sans-serif");
    internal static readonly FontFamily EmojiFontFamily = new("Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji");
    private static readonly object ConfigurationLock = new();
    private static bool configured;

    public static void Configure()
    {
        lock (ConfigurationLock)
        {
            if (configured) return;
            MarkdownNode.Edit(builder => builder
                .Unregister<LiteralInlineNode>()
                .Unregister<AssistantLiteralInlineNode>()
                .Register<AssistantLiteralInlineNode>());
            configured = true;
        }
    }

    internal static IEnumerable<(string Text, bool IsEmoji)> Segment(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var buffer = new StringBuilder();
        bool? currentIsEmoji = null;
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var isEmoji = IsEmoji(element);
            if (currentIsEmoji.HasValue && currentIsEmoji.Value != isEmoji)
            {
                yield return (buffer.ToString(), currentIsEmoji.Value);
                buffer.Clear();
            }

            buffer.Append(element);
            currentIsEmoji = isEmoji;
        }

        if (buffer.Length > 0) yield return (buffer.ToString(), currentIsEmoji == true);
    }

    internal static IReadOnlyList<Run> CreateRuns(string text)
    {
        var runs = new List<Run>();
        foreach (var segment in Segment(text))
        {
            var run = new Run
            {
                Text = segment.Text,
                FontFamily = segment.IsEmoji ? EmojiFontFamily : TextFontFamily
            };
            run.Classes.Add("Literal");
            runs.Add(run);
        }

        return runs;
    }

    private static bool IsEmoji(string textElement)
    {
        foreach (var rune in textElement.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is 0xFE0F or 0x20E3 ||
                value is >= 0x1F000 and <= 0x1FAFF ||
                value is >= 0x2600 and <= 0x27BF ||
                value is >= 0x2B00 and <= 0x2BFF)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class AssistantLiteralInlineNode : InlineNode<LiteralInline>
{
    private readonly Span span = new();

    public override Avalonia.Controls.Documents.Inline Inline => span;

    public AssistantLiteralInlineNode() => span.Classes.Add("Literal");

    protected override bool UpdateCore(
        DocumentNode documentNode,
        LiteralInline literal,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        span.Inlines.Clear();
        foreach (var run in AssistantMarkdownTypography.CreateRuns(literal.Content.ToString()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            span.Inlines.Add(run);
        }

        return true;
    }
}