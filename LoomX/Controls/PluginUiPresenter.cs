using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LoomX.Plugins;

namespace LoomX.Controls;

/// <summary>把经过 Runtime 校验的插件声明式 UI 映射为宿主主题化控件。</summary>
public sealed class PluginUiPresenter : ContentControl
{
    /// <summary>命令回传通道：宿主把命令交给 Runtime 转给插件，返回值用于 Toast 反馈。</summary>
    public static readonly StyledProperty<Func<PluginUiCommand, Task<PluginUiCommandResult?>>?> CommandSinkProperty =
        AvaloniaProperty.Register<PluginUiPresenter, Func<PluginUiCommand, Task<PluginUiCommandResult?>>?>(
            nameof(CommandSink));

    /// <summary>是否把交互节点渲染为可操作控件；未声明交互的 Contribution 降级为只读展示。</summary>
    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<PluginUiPresenter, bool>(nameof(IsInteractive), defaultValue: false);

    public static readonly StyledProperty<IReadOnlyList<PluginUiContribution>?> ContributionsProperty =
        AvaloniaProperty.Register<PluginUiPresenter, IReadOnlyList<PluginUiContribution>?>(nameof(Contributions));

    private readonly PluginUiFormState form = new();

    public IReadOnlyList<PluginUiContribution>? Contributions
    {
        get => GetValue(ContributionsProperty);
        set => SetValue(ContributionsProperty, value);
    }

    public Func<PluginUiCommand, Task<PluginUiCommandResult?>>? CommandSink
    {
        get => GetValue(CommandSinkProperty);
        set => SetValue(CommandSinkProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContributionsProperty
            || change.Property == CommandSinkProperty
            || change.Property == IsInteractiveProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        form.Reset();
        var interactive = IsInteractive;

        var controls = Contributions?
            .Select(contribution => BuildNode(contribution.Root, contribution.Id, interactive))
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

    internal Control? BuildNode(PluginUiNode node, string contributionId, bool interactive)
    {
        try
        {
            return node switch
            {
                PluginUiStackNode stack => BuildStack(stack, contributionId, interactive),
                PluginUiGridNode grid => BuildGrid(grid, contributionId, interactive),
                PluginUiSurfaceNode surface => BuildSurface(surface, contributionId, interactive),
                PluginUiTextNode text => BuildText(text),
                PluginUiIconNode icon => BuildIcon(icon),
                PluginUiDividerNode divider => BuildDivider(divider),
                PluginUiButtonNode button => BuildButton(button, contributionId, interactive),
                PluginUiTextFieldNode field => BuildTextField(field, interactive),
                PluginUiToggleFieldNode toggle => BuildToggleField(toggle, contributionId, interactive),
                PluginUiChoiceFieldNode choice => BuildChoiceField(choice, contributionId, interactive),
                _ => null,
            };
        }
        catch (Exception) when (node is PluginUiIconNode)
        {
            // Runtime 已做结构与长度校验；Geometry 语法仍由宿主解析并安全丢弃。
            return null;
        }
    }

    private Control BuildStack(PluginUiStackNode node, string contributionId, bool interactive)
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
            if (BuildNode(child, contributionId, interactive) is { } control)
                panel.Children.Add(control);
        }
        return panel;
    }

    private Control BuildGrid(PluginUiGridNode node, string contributionId, bool interactive)
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
            if (BuildNode(item.Content, contributionId, interactive) is not { } control) continue;
            Grid.SetColumn(control, index % node.Columns);
            Grid.SetRow(control, index / node.Columns);
            Grid.SetColumnSpan(control, item.ColumnSpan);
            control.HorizontalAlignment = MapAlignment(item.HorizontalAlignment);
            control.VerticalAlignment = MapVerticalAlignment(item.VerticalAlignment);
            grid.Children.Add(control);
        }
        return grid;
    }

    private Control BuildSurface(PluginUiSurfaceNode node, string contributionId, bool interactive)
    {
        var border = new Border
        {
            Child = BuildNode(node.Content, contributionId, interactive),
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

    private Control BuildButton(PluginUiButtonNode node, string contributionId, bool interactive)
    {
        var button = new Button
        {
            Content = node.Label,
            IsEnabled = interactive,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Classes.Add("plugin-ui-button");
        button.Classes.Add(ToneClass(node.Tone));
        if (node.IsPrimary) button.Classes.Add("is-primary");

        if (!interactive) return button;

        button.Click += async (_, _) =>
            await DispatchCommandAsync(
                new PluginUiCommand(contributionId, node.CommandId, MergeArguments(node.Arguments)));
        return button;
    }

    private Control BuildTextField(PluginUiTextFieldNode node, bool interactive)
    {
        form.SetText(node.FieldId, node.Value ?? string.Empty);
        var box = new TextBox
        {
            Text = node.Value ?? string.Empty,
            Watermark = node.Watermark,
            IsReadOnly = node.IsReadOnly || !interactive,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        box.Classes.Add("plugin-ui-field");
        box.TextChanged += (_, _) => form.SetText(node.FieldId, box.Text ?? string.Empty);

        return string.IsNullOrEmpty(node.Label)
            ? box
            : BuildLabeledField(node.Label, box);
    }

    private Control BuildToggleField(
        PluginUiToggleFieldNode node,
        string contributionId,
        bool interactive)
    {
        form.SetToggle(node.FieldId, node.IsChecked);
        var toggle = new ToggleSwitch
        {
            IsChecked = node.IsChecked,
            IsEnabled = interactive,
        };
        toggle.Classes.Add("plugin-ui-field");
        toggle.IsCheckedChanged += async (_, _) =>
        {
            form.SetToggle(node.FieldId, toggle.IsChecked == true);
            await DispatchFieldCommandAsync(node.CommandId, node.Arguments, contributionId, interactive);
        };

        var content = string.IsNullOrEmpty(node.Caption)
            ? (Control)toggle
            : new StackPanel
            {
                Spacing = 2,
                Children = { toggle, new TextBlock { Text = node.Caption, Classes = { "plugin-ui-text", "role-caption" } } },
            };

        return string.IsNullOrEmpty(node.Label)
            ? content
            : BuildLabeledField(node.Label, content);
    }

    private Control BuildChoiceField(
        PluginUiChoiceFieldNode node,
        string contributionId,
        bool interactive)
    {
        form.SetChoice(node.FieldId, node.SelectedValue ?? string.Empty);
        var options = node.Options ?? [];
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedItem = options.FirstOrDefault(
                item => string.Equals(item, node.SelectedValue, StringComparison.Ordinal)),
            IsEnabled = interactive,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 140,
        };
        combo.Classes.Add("plugin-ui-field");
        combo.SelectionChanged += async (_, _) =>
        {
            form.SetChoice(node.FieldId, combo.SelectedItem as string ?? string.Empty);
            await DispatchFieldCommandAsync(node.CommandId, node.Arguments, contributionId, interactive);
        };

        return string.IsNullOrEmpty(node.Label)
            ? combo
            : BuildLabeledField(node.Label, combo);
    }

    private static Control BuildLabeledField(string label, Control control)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Classes.Add("plugin-ui-field-group");
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Classes = { "plugin-ui-text", "role-caption" },
        });
        panel.Children.Add(control);
        return panel;
    }

    /// <summary>字段值变化时回传即时命令；未声明命令或非交互快照时不产生任何回传。</summary>
    private async Task DispatchFieldCommandAsync(
        string? commandId,
        IReadOnlyDictionary<string, string>? arguments,
        string contributionId,
        bool interactive)
    {
        if (!interactive || string.IsNullOrEmpty(commandId)) return;
        await DispatchCommandAsync(
            new PluginUiCommand(contributionId, commandId!, MergeArguments(arguments)));
    }

    private Dictionary<string, string> MergeArguments(IReadOnlyDictionary<string, string>? arguments)
    {
        var merged = form.Snapshot();
        if (arguments is null) return merged;
        foreach (var (key, value) in arguments)
            merged[key] = value ?? string.Empty;
        return merged;
    }

    private async Task DispatchCommandAsync(PluginUiCommand command)
    {
        var sink = CommandSink;
        if (sink is null) return;
        try
        {
            await sink(command).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 命令失败由 Runtime 侧记录诊断；宿主不把插件异常冒泡到 UI 线程。
        }
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

    /// <summary>本次快照内交互字段的当前值表；命令回传时以 FieldId 为键提交。</summary>
    private sealed class PluginUiFormState
    {
        private readonly Dictionary<string, string> textValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> toggleValues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> choiceValues = new(StringComparer.Ordinal);

        public void Reset()
        {
            textValues.Clear();
            toggleValues.Clear();
            choiceValues.Clear();
        }

        public void SetText(string fieldId, string value) => textValues[fieldId] = value;

        public void SetToggle(string fieldId, bool value) => toggleValues[fieldId] = value;

        public void SetChoice(string fieldId, string value) => choiceValues[fieldId] = value;

        public Dictionary<string, string> Snapshot()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (fieldId, value) in toggleValues)
                snapshot[fieldId] = value ? "true" : "false";
            foreach (var (fieldId, value) in choiceValues)
                snapshot[fieldId] = value;
            foreach (var (fieldId, value) in textValues)
                snapshot[fieldId] = value;
            return snapshot;
        }
    }
}
