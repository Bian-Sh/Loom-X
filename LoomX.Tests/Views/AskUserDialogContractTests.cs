using LoomX.Assistant.UserDecisions;
using LoomX.Localization;
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
    public void 单选自由输入_展示默认提示并与预设选项互斥()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(new UserDecisionField(
            "mode",
            "模式",
            UserDecisionFieldType.SingleSelect,
            isRequired: true,
            options: [new("safe", "安全"), new("fast", "快速")],
            defaultOptionId: "safe",
            allowCustomInput: true,
            maxLength: 120)));
        var field = Assert.IsType<AskUserSingleSelectFieldViewModel>(viewModel.CurrentField);

        Assert.True(field.AllowsCustomInput);
        Assert.Equal("我有其他想法...", field.CustomInputPlaceholder);
        Assert.Equal(120, field.CustomInputMaxLength);

        field.CustomInput = "我想逐步确认";
        Assert.Null(field.SelectedOptionId);

        field.Options.Single(option => option.Id == "safe").IsSelected = true;
        Assert.Equal(string.Empty, field.CustomInput);
    }

    [Fact]
    public void 单选自由输入_使用请求提供的提示与长度()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(new UserDecisionField(
            "mode",
            "模式",
            UserDecisionFieldType.SingleSelect,
            options: [new("safe", "安全")],
            allowCustomInput: true,
            customInputPlaceholder: "描述你的模式",
            maxLength: 64)));
        var field = Assert.IsType<AskUserSingleSelectFieldViewModel>(viewModel.CurrentField);

        Assert.Equal("描述你的模式", field.CustomInputPlaceholder);
        Assert.Equal(64, field.CustomInputMaxLength);
    }

    [Fact]
    public void 多选自由输入_与预设项互斥并投影到独立映射()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(new UserDecisionField(
            "features",
            "功能",
            UserDecisionFieldType.MultiSelect,
            isRequired: true,
            options: [new("cloud", "云同步"), new("local", "本地索引")],
            defaultOptionIds: ["cloud", "local"],
            minSelections: 2,
            maxSelections: 2,
            allowCustomInput: true,
            maxLength: 100)));
        var field = Assert.IsType<AskUserMultiSelectFieldViewModel>(viewModel.CurrentField);

        field.CustomInput = "只启用本地索引";
        Assert.All(field.Options, option => Assert.False(option.IsSelected));

        field.Options[0].IsSelected = true;
        Assert.Equal(string.Empty, field.CustomInput);

        field.CustomInput = "只启用本地索引";
        Assert.True(viewModel.TryBuildResult(out var values, out var customInputs));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<string>>(values["features"]));
        Assert.Equal("只启用本地索引", customInputs["features"]);
    }

    [Fact]
    public void 多页自由输入_前后切换保留且跳过时同时清空()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField(
                "mode",
                "模式",
                UserDecisionFieldType.SingleSelect,
                options: [new("safe", "安全")],
                allowCustomInput: true),
            new UserDecisionField("note", "备注", UserDecisionFieldType.Text)));
        var field = Assert.IsType<AskUserSingleSelectFieldViewModel>(viewModel.CurrentField);
        field.CustomInput = "保留这条想法";

        viewModel.MoveNextWithoutValidation();
        viewModel.MovePrevious();

        Assert.Equal("保留这条想法", field.CustomInput);
        Assert.True(viewModel.TrySkipCurrentField(out var shouldSubmit));
        Assert.False(shouldSubmit);
        Assert.Equal(string.Empty, field.CustomInput);
        Assert.Null(field.SelectedOptionId);
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
    public void 分页状态_初始显示第一字段且不预先显示错误()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("first", "第一项", UserDecisionFieldType.Text, isRequired: true),
            new UserDecisionField("second", "第二项", UserDecisionFieldType.Text, isRequired: true)));

        Assert.Equal(0, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
        Assert.Same(viewModel.Fields[0], ReadProperty<AskUserFieldViewModel>(viewModel, "CurrentField"));
        Assert.Equal("1 / 2", ReadProperty<string>(viewModel, "StepText"));
        Assert.False(ReadProperty<bool>(viewModel, "HasPreviousField"));
        Assert.True(ReadProperty<bool>(viewModel, "HasNextField"));
        Assert.False(ReadProperty<bool>(viewModel, "IsLastField"));
        Assert.False(ReadProperty<bool>(viewModel, "CanSkipCurrentField"));
        Assert.False(viewModel.HasErrors);
        Assert.All(viewModel.Fields, field => Assert.False(field.HasError));
    }

    [Fact]
    public void 分页导航_前后切换会保留已输入值()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("first", "第一项", UserDecisionFieldType.Text),
            new UserDecisionField("second", "第二项", UserDecisionFieldType.Number)));
        var first = Assert.IsType<AskUserTextFieldViewModel>(viewModel.Fields[0]);
        first.TextValue = "保留此值";

        Invoke(viewModel, "MoveNextWithoutValidation");

        Assert.Equal(1, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
        Assert.Same(viewModel.Fields[1], ReadProperty<AskUserFieldViewModel>(viewModel, "CurrentField"));

        Invoke(viewModel, "MovePrevious");

        Assert.Equal(0, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
        Assert.Equal("保留此值", first.TextValue);
    }

    [Fact]
    public void TryAdvanceCurrentField_当前字段无效时停留并显示安全错误()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("required", "必填项", UserDecisionFieldType.Text, isRequired: true),
            new UserDecisionField("later", "后续项", UserDecisionFieldType.Text, isRequired: true)));

        var advanced = Invoke<bool>(viewModel, "TryAdvanceCurrentField");

        Assert.False(advanced);
        Assert.Equal(0, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
        Assert.True(viewModel.Fields[0].HasError);
        Assert.False(viewModel.Fields[1].HasError);
        Assert.True(viewModel.HasErrors);
    }

    [Fact]
    public void TryAdvanceCurrentField_有效字段前进且末页通过后可构造结果()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("note", "说明", UserDecisionFieldType.Text, isRequired: true),
            new UserDecisionField(
                "mode",
                "模式",
                UserDecisionFieldType.SingleSelect,
                isRequired: true,
                options: [new("safe", "安全"), new("fast", "快速")])));
        Assert.IsType<AskUserTextFieldViewModel>(viewModel.Fields[0]).TextValue = "已确认";

        Assert.True(Invoke<bool>(viewModel, "TryAdvanceCurrentField"));
        Assert.Equal(1, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
        Assert.True(ReadProperty<bool>(viewModel, "IsLastField"));

        Assert.IsType<AskUserSingleSelectFieldViewModel>(viewModel.Fields[1]).SelectedOptionId = "fast";
        Assert.True(Invoke<bool>(viewModel, "TryAdvanceCurrentField"));
        Assert.Equal(1, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
        Assert.True(viewModel.TryBuildResult(out var values));
        Assert.Equal("已确认", values["note"]);
        Assert.Equal("fast", values["mode"]);
    }

    [Fact]
    public void TrySkipCurrentField_仅可选字段可跳过并清空当前值()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("optional", "可选项", UserDecisionFieldType.Text, defaultText: "默认值"),
            new UserDecisionField("required", "必填项", UserDecisionFieldType.Text, isRequired: true)));
        var optional = Assert.IsType<AskUserTextFieldViewModel>(viewModel.Fields[0]);

        var skipped = InvokeSkip(viewModel, out var shouldSubmit);

        Assert.True(skipped);
        Assert.False(shouldSubmit);
        Assert.Equal(string.Empty, optional.TextValue);
        Assert.Equal(1, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
        Assert.False(ReadProperty<bool>(viewModel, "CanSkipCurrentField"));
        Assert.False(InvokeSkip(viewModel, out shouldSubmit));
        Assert.False(shouldSubmit);
        Assert.Equal(1, ReadProperty<int>(viewModel, "CurrentFieldIndex"));
    }

    [Fact]
    public void TrySkipCurrentField_末页可选字段请求直接提交空值()
    {
        var viewModel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("optional", "可选项", UserDecisionFieldType.Number, defaultNumber: 3)));

        Assert.True(InvokeSkip(viewModel, out var shouldSubmit));
        Assert.True(shouldSubmit);
        Assert.True(viewModel.TryBuildResult(out var values));
        Assert.Null(values["optional"]);
    }

    [Fact]
    public void ErrorSummary_使用当前Locale资源()
    {
        var previousCulture = LocaleService.CurrentCulture.Name;
        try
        {
            LocaleService.SetCulture("en-US");
            var viewModel = new AskUserDialogViewModel(CreatePending(new UserDecisionField(
                "note",
                "备注",
                UserDecisionFieldType.Text,
                isRequired: true)));

            Assert.Equal(string.Empty, viewModel.ErrorSummary);
            Assert.False(Invoke<bool>(viewModel, "TryAdvanceCurrentField"));
            Assert.Equal(ResourceLookup.Resolve("assistant.decision.validation_failed"), viewModel.ErrorSummary);
            Assert.DoesNotContain("请检查", viewModel.ErrorSummary, StringComparison.Ordinal);
        }
        finally
        {
            LocaleService.SetCulture(previousCulture);
        }
    }

    [Fact]
    public void CardXaml_右上角取消输入且操作按钮自适应文字()
    {
        var source = ReadDesktopFile("Views", "AskUserCard.axaml");

        Assert.Contains("<UserControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Window", source, StringComparison.Ordinal);
        Assert.Contains("Width=\"520\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"520\"", source, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource DialogBackgroundBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource BorderStrongBrush}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SurfaceSubtleBrush}", source, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource AccentBrush}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content=\"{Binding CurrentField}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding Fields}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding CurrentField.Label}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding StepText}\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PreviousButton\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NextButton\"", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"SkipButton_OnClick\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding CanSkipCurrentField}\"", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding PrimaryActionText}\"", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"PrimaryButton_OnClick\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CancelInputButton\"", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"×\"", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{l:Locale assistant.decision.cancel_input}\"", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"CancelInputButton_OnClick\"", source, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding AllowCancel}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"{l:Locale assistant.decision.cancel}\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"card-secondary card-action\"", source, StringComparison.Ordinal);
        Assert.Contains("Classes=\"accent card-action\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalContentAlignment\" Value=\"Center", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"92\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AskUser 验收面板", source, StringComparison.Ordinal);
        Assert.Contains("AskUserSingleSelectFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("AskUserMultiSelectFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("AskUserNumberFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("AskUserTextFieldViewModel", source, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"TextInput_OnKeyDown\"", source, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"NumberInput_OnKeyDown\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CustomInput, Mode=TwoWay}\"", source, StringComparison.Ordinal);         Assert.Contains("Watermark=\"{Binding CustomInputPlaceholder}\"", source, StringComparison.Ordinal);         Assert.Contains("MaxLength=\"{Binding CustomInputMaxLength}\"", source, StringComparison.Ordinal);         Assert.Contains("IsVisible=\"{Binding AllowsCustomInput}\"", source, StringComparison.Ordinal);         Assert.Contains("KeyDown=\"SelectionCustomInput_OnKeyDown\"", source, StringComparison.Ordinal);         Assert.DoesNotContain("其他（可选）", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Question}\"", source, StringComparison.Ordinal);
        Assert.Contains("Description", source, StringComparison.Ordinal);
        Assert.Contains("ImpactSummary", source, StringComparison.Ordinal);
        Assert.Contains("ErrorSummary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding Title}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding CurrentField.Label}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding CurrentField.RequiredSuffix}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CardXaml_使用紧凑间距并限制选项最多两行()
    {
        var source = ReadDesktopFile("Views", "AskUserCard.axaml");

        Assert.Contains("<StackPanel Spacing=\"8\">", source, StringComparison.Ordinal);
        Assert.Contains("Margin=\"16,14,16,0\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"440\"", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"30\"", source, StringComparison.Ordinal);
        Assert.Contains("Padding=\"8,4\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxLines=\"2\"", source, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CardXaml_底部按钮组进一步压缩()
    {
        var source = ReadDesktopFile("Views", "AskUserCard.axaml");

        Assert.Contains("MinHeight\" Value=\"28\"", source, StringComparison.Ordinal);
        Assert.Contains("Padding\" Value=\"8,2\"", source, StringComparison.Ordinal);
        Assert.Contains("Margin=\"16,6,16,8\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CardCodeBehind_接通右上角取消输入与其余交互()
    {
        var code = ReadDesktopFile("Views", "AskUserCard.axaml.cs");

        Assert.Contains("PreviousButton_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("NextButton_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("SkipButton_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("PrimaryButton_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("CancelInputButton_OnClick", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CancelButton_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("SingleChoice_OnClick", code, StringComparison.Ordinal);
        Assert.Contains("Task.Delay", code, StringComparison.Ordinal);
        Assert.Contains("TryAdvanceCurrentField", code, StringComparison.Ordinal);
        Assert.Contains("TrySkipCurrentField", code, StringComparison.Ordinal);
        Assert.Contains("TryCompleteSubmission", code, StringComparison.Ordinal);
        Assert.Contains("TryCancel", code, StringComparison.Ordinal);
        Assert.Contains("Key.Enter", code, StringComparison.Ordinal);
        Assert.Contains("KeyModifiers.Control", code, StringComparison.Ordinal);
        Assert.Contains("SelectionCustomInput_OnKeyDown", code, StringComparison.Ordinal);
        Assert.Contains("AskUserTextFieldViewModel { IsMultiline: true }", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Close(true)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Close(false)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("protected override void OnKeyDown", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CardViewModel_提交取消只能完成一次()
    {
        var submit = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("answer", "回答", UserDecisionFieldType.Text, isRequired: true)));
        Assert.IsType<AskUserTextFieldViewModel>(submit.CurrentField).TextValue = "确认";

        Assert.True(submit.TryCompleteSubmission());
        Assert.False(submit.TryCancel());
        Assert.True(await submit.Completion);

        var cancel = new AskUserDialogViewModel(CreatePending(
            new UserDecisionField("answer", "回答", UserDecisionFieldType.Text, isRequired: true)));
        Assert.True(cancel.TryCancel());
        Assert.False(cancel.TryCompleteSubmission());
        Assert.False(await cancel.Completion);
    }

    [Theory]
    [InlineData("Strings.resx", "我有其他想法...")]
    [InlineData("Strings.en-US.resx", "I have another idea...")]
    [InlineData("Strings.ja-JP.resx", "ほかの考えがあります...")]
    [InlineData("Strings.zh-TW.resx", "我有其他想法...")]
    public void ApprovalCard操作文案_覆盖全部Locale(string fileName, string customInputPlaceholder)
    {
        var source = ReadDesktopFile("Resources", fileName);

        Assert.Contains("name=\"assistant.decision.skip\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"assistant.decision.continue\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"assistant.decision.submit\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"assistant.decision.previous\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"assistant.decision.next\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"assistant.decision.cancel_input\"", source, StringComparison.Ordinal);
        Assert.Contains($"<data name=\"assistant.decision.custom_input_placeholder\"><value>{customInputPlaceholder}</value></data>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"assistant.decision.cancel\"", source, StringComparison.Ordinal);
    }

    private static T ReadProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<T>(property!.GetValue(target));
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
        Assert.NotNull(method);
        method!.Invoke(target, null);
    }

    private static T Invoke<T>(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, Type.EmptyTypes);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(target, null));
    }

    private static bool InvokeSkip(object target, out bool shouldSubmit)
    {
        var method = target.GetType().GetMethod("TrySkipCurrentField");
        Assert.NotNull(method);
        object?[] arguments = [false];
        var skipped = Assert.IsType<bool>(method!.Invoke(target, arguments));
        shouldSubmit = Assert.IsType<bool>(arguments[0]);
        return skipped;
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
