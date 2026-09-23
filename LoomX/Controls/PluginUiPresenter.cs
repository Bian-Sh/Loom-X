using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LoomX.Plugins;

namespace LoomX.Controls;

/// <summary>把经过 Runtime 校验的插件声明式 UI 映射为宿主主题化控件。</summary>
public sealed class PluginUiPresenter : ContentControl
{
    public static readonly StyledProperty<IReadOnlyList<PluginUiContribution>?> ContributionsProperty =
        AvaloniaProperty.Register<PluginUiPresenter, IReadOnlyList<PluginUiContribution>?>(nameof(Contributions));

    public IReadOnlyList<PluginUiContribution>? Contributions
    {
        get => GetValue(ContributionsProperty);
        set => SetValue(ContributionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContributionsProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        var controls = Contributions?
            .Select(contribution => BuildNode(contribution.Root))
            .Where(control => control is not null)
            .Cast<Control>()
            .ToArray() ?? [];

        Content = controls.Length switch
        {
            0 => null,
            1 => controls[0],
            _ => BuildContributionStack(controls),
        };
    }

    private static StackPanel BuildContributionStack(IEnumerable<Control> controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Classes.Add("plugin-ui-contributions");
        foreach (var control in controls)
            panel.Children.Add(control);
        return panel;
    }

    internal static Control? BuildNode(PluginUiNode node)
    {
        try
        {
            return node switch
            {
                PluginUiStackNode stack => BuildStack(stack),
                PluginUiGridNode grid => BuildGrid(grid),
                PluginUiSurfaceNode surface => BuildSurface(surface),
                PluginUiTextNode text => BuildText(text),
                PluginUiIconNode icon => BuildIcon(icon),
                PluginUiDividerNode divider => BuildDivider(divider),
                _ => null,
            };
        }
        catch (Exception) when (node is PluginUiIconNode)
        {
            // Runtime 已做结构与长度校验；Geometry 语法仍由宿主解析并安全丢弃。
            return null;
        }
    }

    private static Control BuildStack(PluginUiStackNode node)
    {
        var panel = new StackPanel
        {
            Orientation = node.Orientation == PluginUiOrientation.Horizontal
                ? Orientation.Horizontal
                : Orientation.Vertical,
            Spacing = node.Spacing,
        };
        panel.Classes.Add("plugin-ui-stack");
        foreach (var child in node.Children)
        {
            if (BuildNode(child) is { } control)
                panel.Children.Add(control);
        }
        return panel;
    }

    private static Control BuildGrid(PluginUiGridNode node)
    {
        var grid = new Grid
        {
            ColumnSpacing = node.ColumnSpacing,
            RowSpacing = node.RowSpacing,
        };
        grid.Classes.Add("plugin-ui-grid");
        for (var column = 0; column < node.Columns; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var rowCount = Math.Max(1, (int)Math.Ceiling(node.Children.Count / (double)node.Columns));
        for (var row = 0; row < rowCount; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var index = 0; index < node.Children.Count; index++)
        {
            var item = node.Children[index];
            if (BuildNode(item.Content) is not { } control) continue;
            Grid.SetColumn(control, index % node.Columns);
            Grid.SetRow(control, index / node.Columns);
            Grid.SetColumnSpan(control, item.ColumnSpan);
            control.HorizontalAlignment = MapAlignment(item.HorizontalAlignment);
            control.VerticalAlignment = MapVerticalAlignment(item.VerticalAlignment);
            grid.Children.Add(control);
        }
        return grid;
    }

    private static Control BuildSurface(PluginUiSurfaceNode node)
    {
        var border = new Border
        {
            Child = BuildNode(node.Content),
            Padding = new Thickness(node.Padding),
            CornerRadius = new CornerRadius(node.CornerRadius),
            BorderThickness = node.ShowBorder ? new Thickness(1) : default,
        };
        AddToneClasses(border, "plugin-ui-surface", node.Tone);
        return border;
    }

    private static Control BuildText(PluginUiTextNode node)
    {
        var text = new TextBlock
        {
            Text = node.Text,
            TextWrapping = node.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            HorizontalAlignment = MapAlignment(node.HorizontalAlignment),
        };
        text.Classes.Add("plugin-ui-text");
        text.Classes.Add(node.Role switch
        {
            PluginUiTextRole.Title => "role-title",
            PluginUiTextRole.Metric => "role-metric",
            PluginUiTextRole.Caption => "role-caption",
            _ => "role-body",
        });
        text.Classes.Add(ToneClass(node.Tone));
        return text;
    }

    private static Control BuildIcon(PluginUiIconNode node)
    {
        var icon = new PathIcon
        {
            Data = Geometry.Parse(node.Data),
            Width = node.Size,
            Height = node.Size,
            HorizontalAlignment = MapAlignment(node.HorizontalAlignment),
        };
        AddToneClasses(icon, "plugin-ui-icon", node.Tone);
        return icon;
    }

    private static Control BuildDivider(PluginUiDividerNode node)
    {
        var divider = new Border
        {
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AddToneClasses(divider, "plugin-ui-divider", node.Tone);
        return divider;
    }

    private static void AddToneClasses(StyledElement element, string baseClass, PluginUiTone tone)
    {
        element.Classes.Add(baseClass);
        element.Classes.Add(ToneClass(tone));
    }

    private static string ToneClass(PluginUiTone tone) => tone switch
    {
        PluginUiTone.Accent => "tone-accent",
        PluginUiTone.Success => "tone-success",
        PluginUiTone.Warning => "tone-warning",
        PluginUiTone.Danger => "tone-danger",
        _ => "tone-default",
    };

    private static HorizontalAlignment MapAlignment(PluginUiAlignment alignment) => alignment switch
    {
        PluginUiAlignment.Start => HorizontalAlignment.Left,
        PluginUiAlignment.Center => HorizontalAlignment.Center,
        PluginUiAlignment.End => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Stretch,
    };

    private static VerticalAlignment MapVerticalAlignment(PluginUiAlignment alignment) => alignment switch
    {
        PluginUiAlignment.Start => VerticalAlignment.Top,
        PluginUiAlignment.Center => VerticalAlignment.Center,
        PluginUiAlignment.End => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Stretch,
    };
}
