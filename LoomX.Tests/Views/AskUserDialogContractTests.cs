using LoomX.Assistant.UserDecisions;
using LoomX.ViewModels;
using Xunit;

namespace LoomX.Tests.Views;

public sealed class AskUserDialogContractTests
{
    [Fact]
    public void TryBuildResult_投影四类字段默认值()
    {
        var pending = CreatePending(
            new UserDecisionField(
                "mode",
                "模式",
                UserDecisionFieldType.SingleSelect,
                isRequired: true,
                options: [new("safe", "安全"), new("fast", "快速")],
                defaultOptionId: "safe"),
            new UserDecisionField(
                "targets",
                "目标",
                UserDecisionFieldType.MultiSelect,
                options: [new("a", "A"), new("b", "B")],
                defaultOptionIds: ["a"]),
            new UserDecisionField(
                "count",
                "数量",
                UserDecisionFieldType.Number,
                defaultNumber: 4,
                minNumber: 0,
                maxNumber: 10,
                step: 2),
            new UserDecisionField(
                "note",
                "备注",
                UserDecisionFieldType.Text,
                defaultText: "默认说明",
                maxLength: 20));
        var viewModel = new AskUserDialogViewModel(pending);

        Assert.Collection(
            viewModel.Fields,
            field => Assert.IsType<AskUserSingleSelectFieldViewModel>(field),
            field => Assert.IsType<AskUserMultiSelectFieldViewModel>(field),
            field => Assert.IsType<AskUserNumberFieldViewModel>(field),
            field => Assert.IsType<AskUserTextFieldViewModel>(field));

        Assert.True(viewModel.TryBuildResult(out var values));
        Assert.Equal("safe", values["mode"]);
        Assert.Equal(["a"], Assert.IsAssignableFrom<IEnumerable<string>>(values["targets"]));
        Assert.Equal(4m, values["count"]);
        Assert.Equal("默认说明", values["note"]);
    }

    [Fact]
    public void TryBuildResult_必填字段为空时即时显示安全错误()
    {
        var pending = CreatePending(new UserDecisionField(
            "note",
            "API Key Secret Authorization 问题正文",
            UserDecisionFieldType.Text,
            isRequired: true,
            defaultText: "初始值"));
        var viewModel = new AskUserDialogViewModel(pending);
        var field = Assert.IsType<AskUserTextFieldViewModel>(Assert.Single(viewModel.Fields));

        field.TextValue = "";

        Assert.True(field.HasError);
        Assert.False(viewModel.TryBuildResult(out var values));
        Assert.Empty(values);
        Assert.DoesNotContain("API Key", field.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", field.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", field.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("初始值", field.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuildResult_校验多选最少与最多数量()
    {
        var pending = CreatePending(new UserDecisionField(
            "targets",
            "目标",
            UserDecisionFieldType.MultiSelect,
            options: [new("a", "A"), new("b", "B"), new("c", "C")],
            defaultOptionIds: ["a"],
            minSelections: 2,
            maxSelections: 2));
        var viewModel = new AskUserDialogViewModel(pending);
        var field = Assert.IsType<AskUserMultiSelectFieldViewModel>(Assert.Single(viewModel.Fields));

        Assert.False(viewModel.TryBuildResult(out _));
        Assert.True(field.HasError);

        field.Options[1].IsSelected = true;
        Assert.True(viewModel.TryBuildResult(out _));

        field.Options[2].IsSelected = true;
        Assert.True(field.HasError);
        Assert.False(viewModel.TryBuildResult(out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(3)]
    public void TryBuildResult_校验数字范围与步长(int value)
    {
        var pending = CreatePending(new UserDecisionField(
            "count",
            "数量",
            UserDecisionFieldType.Number,
            isRequired: true,
            defaultNumber: 2,
            minNumber: 0,
            maxNumber: 10,
            step: 2));
        var viewModel = new AskUserDialogViewModel(pending);
        var field = Assert.IsType<AskUserNumberFieldViewModel>(Assert.Single(viewModel.Fields));

        field.NumberValue = value;

        Assert.True(field.HasError);
        Assert.False(viewModel.TryBuildResult(out _));
    }

    [Fact]
    public void TryBuildResult_校验文本最大长度且不回显原值()
    {
        const string input = "用户自由文本Secret";
        var pending = CreatePending(new UserDecisionField(
            "note",
            "备注",
            UserDecisionFieldType.Text,
            defaultText: "",
            maxLength: 4));
        var viewModel = new AskUserDialogViewModel(pending);
        var field = Assert.IsType<AskUserTextFieldViewModel>(Assert.Single(viewModel.Fields));

        field.TextValue = input;

        Assert.True(field.HasError);
        Assert.False(viewModel.TryBuildResult(out _));
        Assert.DoesNotContain(input, field.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", field.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DialogXaml_使用动态玻璃资源滚动字段模板与安全错误区()
    {
        var source = ReadDesktopFile("Views", "AskUserDialog.axaml");
        var code = ReadDesktopFile("Views", "AskUserDialog.axaml.cs");

        Assert.Contains("Background=\"Transparent\"", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DialogBackgroundBrush}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource BorderStrongBrush}", source, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"10\"", source, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer", source, StringComparison.Ordinal);
        Assert.Contains("AskUserSingleSelectFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("AskUserMultiSelectFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("AskUserNumberFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("AskUserTextFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Title}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Question}\"", source, StringComparison.Ordinal);
        Assert.Contains("Description", source, StringComparison.Ordinal);
        Assert.Contains("ImpactSummary", source, StringComparison.Ordinal);
        Assert.Contains("ErrorSummary", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"{l:Locale assistant.cancel}\"", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"{l:Locale assistant.approval.approve}\"", source, StringComparison.Ordinal);
        Assert.Contains("SubmitButton_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("TryBuildResult", code, StringComparison.Ordinal);
        Assert.Contains("Close(false)", code, StringComparison.Ordinal);
    }

    private static PendingUserDecision CreatePending(params UserDecisionField[] fields) => new(
        "request-1",
        "owner-1",
        new UserDecisionRequest(
            "需要确认",
            "请选择后续操作",
            fields,
            description: "可选原因",
            impactSummary: "可选影响"));

    private static string ReadDesktopFile(params string[] segments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "LoomX", .. segments]);
        return File.ReadAllText(path);
    }
}
