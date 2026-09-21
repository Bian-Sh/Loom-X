using Avalonia.Controls;
using LiveMarkdown.Avalonia;
using LoomX.ViewModels;
using System.Diagnostics;

namespace LoomX.Views;

public partial class ReleaseNotesView : UserControl
{
    public ReleaseNotesView()
    {
        InitializeComponent();
    }

    private void MarkdownRenderer_OnLinkClick(object? sender, LinkClickedEventArgs e)
    {
        e.Handled = true;
        if (!TryResolveExternalLink(
                e.HRef,
                (DataContext as ReleaseNotesContentViewModel)?.ReleaseUrl,
                out var target)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    internal static bool TryResolveExternalLink(Uri? href, string? releaseUrl, out Uri target)
    {
        target = null!;
        if (href is null) return false;

        if (href.IsAbsoluteUri)
        {
            target = href;
        }
        else
        {
            if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out var releasePage)
                || !Uri.TryCreate(releasePage, href, out var resolved))
                return false;
            target = resolved;
        }

        return target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps;
    }
}
