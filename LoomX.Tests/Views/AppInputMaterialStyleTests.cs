using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace LoomX.Tests.Views;

[Collection("Avalonia UI")]
public sealed class AppInputMaterialStyleTests
{
    private static readonly Color ExpectedColor = Color.FromArgb(0x71, 0x23, 0x56, 0x78);

    [Fact]
    public void FocusedTextBoxTemplateUsesControlBackground()
    {
        AssertFocusedTemplateUsesControlBackground(new TextBox());
    }

    [Fact]
    public void FocusedComboBoxTemplateUsesControlBackground()
    {
        AssertFocusedTemplateUsesControlBackground(new ComboBox
        {
            ItemsSource = new[] { "alpha", "beta" },
            SelectedIndex = 0
        });
    }

    [Fact]
    public void FocusedNumericUpDownTemplateUsesControlBackground()
    {
        AssertFocusedTemplateUsesControlBackground(new NumericUpDown { Value = 12 });
    }

    [Fact]
    public void TransparentInputSemanticKeepsTextBoxTemplateTransparent()
    {
        var textBox = new TextBox { BorderThickness = default };
        textBox.Classes.Add("input-transparent");

        AssertFocusedTemplateUsesExpectedBackground(textBox, Colors.Transparent);
    }

    [Fact]
    public void EmbeddedInputSemanticRemovesTextBoxSurface()
    {
        var textBox = new TextBox();
        textBox.Classes.Add("input-embedded");

        AssertFocusedTemplateUsesExpectedBackground(textBox, Colors.Transparent);
    }

    private static void AssertFocusedTemplateUsesControlBackground(TemplatedControl control)
    {
        AssertFocusedTemplateUsesExpectedBackground(control, ExpectedColor);
    }

    private static void AssertFocusedTemplateUsesExpectedBackground(
        TemplatedControl control,
        Color expectedColor)
    {
        AvaloniaTestBootstrap.Ensure();

        if (expectedColor != Colors.Transparent)
        {
            control.Background = new SolidColorBrush(expectedColor);
        }
        var host = new Window
        {
            Width = 420,
            Height = 180,
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = control
            }
        };

        host.Show();
        try
        {
            control.ApplyTemplate();
            host.UpdateLayout();

            var focusTarget = control is NumericUpDown
                ? Assert.Single(control.GetVisualDescendants().OfType<TextBox>())
                : control;
            Assert.True(focusTarget.Focus());
            host.UpdateLayout();

            var unexpectedBackgrounds = control.GetVisualDescendants()
                .Select(visual => visual switch
                {
                    Border border => (
                        Type: (string?)visual.GetType().Name,
                        Name: border.Name,
                        Background: border.Background),
                    ContentPresenter presenter => (
                        Type: (string?)visual.GetType().Name,
                        Name: presenter.Name,
                        Background: presenter.Background),
                    _ => (Type: null, Name: null, Background: (IBrush?)null)
                })
                .Where(item => item.Type is not null)
                .Where(item => item.Background is ISolidColorBrush brush
                    && brush.Color.A > 0
                    && brush.Color != expectedColor
                    && item.Name != "HighlightBackground")
                .Select(item => $"{item.Type}#{item.Name ?? "<unnamed>"}: {item.Background}")
                .ToArray();

            Assert.True(control.IsKeyboardFocusWithin);
            Assert.Equal(expectedColor, Assert.IsAssignableFrom<ISolidColorBrush>(control.Background).Color);
            Assert.Empty(unexpectedBackgrounds);
        }
        finally
        {
            host.Close();
        }
    }
}
