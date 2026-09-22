using System.Text.Json;
using LoomX.Assistant.UserDecisions;
using Xunit;

namespace LoomX.Tests.Assistant;

public sealed class UserDecisionModelsTests
{
    [Fact]
    public void 请求校验_拒绝重复字段标识()
    {
        var request = CreateRequest(
            CreateTextField("note"),
            CreateTextField("note"));

        var error = Assert.Single(UserDecisionValidator.ValidateRequest(request));

        Assert.Equal("note", error.FieldId);
        Assert.Contains("重复", error.Message);
    }

    [Fact]
    public void 请求校验_拒绝空标题并返回中文安全消息()
    {
        var request = new UserDecisionRequest(
            title: "   ",
            question: "请选择后续操作。",
            fields: [CreateTextField("note")]);

        var error = Assert.Single(UserDecisionValidator.ValidateRequest(request));

        Assert.Equal(UserDecisionValidationError.RequestFieldId, error.FieldId);
        Assert.Contains("标题", error.Message);
        Assert.DoesNotContain("请选择后续操作", error.Message);
    }

    [Fact]
    public void 单选字段校验_拒绝未知默认选项()
    {
        var field = new UserDecisionField(
            id: "mode",
            label: "运行模式",
            type: UserDecisionFieldType.SingleSelect,
            options:
            [
                new UserDecisionOption("safe", "安全模式"),
                new UserDecisionOption("fast", "快速模式"),
            ],
            defaultOptionId: "missing");

        var error = Assert.Single(UserDecisionValidator.ValidateRequest(CreateRequest(field)));

        Assert.Equal("mode", error.FieldId);
        Assert.Contains("默认选项", error.Message);
    }

    [Fact]
    public void 多选字段校验_拒绝最小值大于最大值()
    {
        var field = new UserDecisionField(
            id: "features",
            label: "启用能力",
            type: UserDecisionFieldType.MultiSelect,
            options:
            [
                new UserDecisionOption("search", "搜索"),
                new UserDecisionOption("browser", "浏览器"),
            ],
            minSelections: 2,
            maxSelections: 1);

        var error = Assert.Single(UserDecisionValidator.ValidateRequest(CreateRequest(field)));

        Assert.Equal("features", error.FieldId);
        Assert.Contains("最少选择数", error.Message);
    }

    [Fact]
    public void 数值字段校验_拒绝反向范围与不符合步长的默认值()
    {
        var invalidRange = new UserDecisionField(
            id: "range",
            label: "范围",
            type: UserDecisionFieldType.Number,
            minNumber: 10,
            maxNumber: 1);
        var invalidStep = new UserDecisionField(
            id: "threads",
            label: "线程数",
            type: UserDecisionFieldType.Number,
            defaultNumber: 4,
            minNumber: 1,
            maxNumber: 10,
            step: 2);

        var errors = UserDecisionValidator.ValidateRequest(CreateRequest(invalidRange, invalidStep));

        Assert.Contains(errors, error => error.FieldId == "range" && error.Message.Contains("最小值"));
        Assert.Contains(errors, error => error.FieldId == "threads" && error.Message.Contains("步长"));
    }

    [Fact]
    public void 提交校验_拒绝必填空文本()
    {
        var request = CreateRequest(CreateTextField("note", isRequired: true, maxLength: 20));

        var error = Assert.Single(UserDecisionValidator.ValidateSubmission(
            request,
            new Dictionary<string, object?> { ["note"] = "   " }));

        Assert.Equal("note", error.FieldId);
        Assert.Contains("不能为空", error.Message);
    }

    [Fact]
    public void 提交校验_拒绝超过字段上限的文本()
    {
        var request = CreateRequest(CreateTextField("note", maxLength: 5));

        var error = Assert.Single(UserDecisionValidator.ValidateSubmission(
            request,
            new Dictionary<string, object?> { ["note"] = "六个字符文本" }));

        Assert.Equal("note", error.FieldId);
        Assert.Contains("长度", error.Message);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("option")]
    [InlineData("impact")]
    public void 请求校验_拒绝展示内容中的敏感模式(string location)
    {
        var optionDescription = location == "option" ? "请粘贴 api_key" : "普通说明";
        var field = new UserDecisionField(
            id: "mode",
            label: "运行模式",
            type: UserDecisionFieldType.SingleSelect,
            options: [new UserDecisionOption("safe", "安全模式", optionDescription)]);
        var request = new UserDecisionRequest(
            title: "确认设置",
            question: location == "question" ? "请提供 Authorization" : "请选择运行模式。",
            fields: [field],
            impactSummary: location == "impact" ? "将更新 database_password" : "仅影响本次运行。");

        var errors = UserDecisionValidator.ValidateRequest(request);

        Assert.Contains(errors, error =>
            error.Message.Contains("敏感", StringComparison.Ordinal)
            && (error.FieldId == UserDecisionValidationError.RequestFieldId || error.FieldId == "mode"));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("question")]
    [InlineData("description")]
    [InlineData("impact")]
    [InlineData("field_label")]
    [InlineData("option_label")]
    [InlineData("option_description")]
    [InlineData("default_text")]
    public void 请求校验_所有展示文本复用内容级敏感检测(string location)
    {
        const string sensitive = "clientSecrets=sk-proj-abcdefghijklmnopqrstuvwxyz123456";
        var option = new UserDecisionOption(
            "safe",
            location == "option_label" ? sensitive : "安全模式",
            location == "option_description" ? sensitive : "普通说明");
        var field = location == "default_text"
            ? CreateTextField("note", defaultText: sensitive)
            : new UserDecisionField(
                id: "mode",
                label: location == "field_label" ? sensitive : "运行模式",
                type: UserDecisionFieldType.SingleSelect,
                options: [option]);
        var request = new UserDecisionRequest(
            title: location == "title" ? sensitive : "确认设置",
            question: location == "question" ? sensitive : "请选择运行模式。",
            fields: [field],
            description: location == "description" ? sensitive : "普通说明",
            impactSummary: location == "impact" ? sensitive : "仅影响本次运行。");

        var errors = UserDecisionValidator.ValidateRequest(request);

        Assert.Contains(errors, error => error.Message.Contains("敏感", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Message.Contains(sensitive, StringComparison.Ordinal));
    }

    [Fact]
    public void 提交校验_拒绝敏感文本且错误不回显原值()
    {
        const string sensitive = "Authorization: Bearer abcdefghijklmnopqrstuvwxyz123456";
        var request = CreateRequest(CreateTextField("note"));

        var error = Assert.Single(UserDecisionValidator.ValidateSubmission(
            request,
            new Dictionary<string, object?> { ["note"] = sensitive }));

        Assert.Equal("note", error.FieldId);
        Assert.Contains("敏感", error.Message);
        Assert.DoesNotContain(sensitive, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 请求校验_限制问题选项说明与影响摘要长度()
    {
        var longText = new string('长', 5000);
        var field = new UserDecisionField(
            id: "mode",
            label: "运行模式",
            type: UserDecisionFieldType.SingleSelect,
            options: [new UserDecisionOption("safe", "安全模式", longText)]);
        var request = new UserDecisionRequest(
            title: "确认设置",
            question: longText,
            fields: [field],
            impactSummary: longText);

        var errors = UserDecisionValidator.ValidateRequest(request);

        Assert.Contains(errors, error =>
            error.FieldId == UserDecisionValidationError.RequestFieldId
            && error.Message.Contains("问题")
            && error.Message.Contains("长度"));
        Assert.Contains(errors, error =>
            error.FieldId == "mode"
            && error.Message.Contains("选项说明")
            && error.Message.Contains("长度"));
        Assert.Contains(errors, error =>
            error.FieldId == UserDecisionValidationError.RequestFieldId
            && error.Message.Contains("影响摘要")
            && error.Message.Contains("长度"));
    }
    [Fact]
    public void 字段类型_JSON往返使用稳定蛇形判别值()
    {
        var cases = new[]
        {
            (CreateValidField(UserDecisionFieldType.SingleSelect), "single_select"),
            (CreateValidField(UserDecisionFieldType.MultiSelect), "multi_select"),
            (CreateValidField(UserDecisionFieldType.Number), "number"),
            (CreateValidField(UserDecisionFieldType.Text), "text"),
        };

        foreach (var (field, discriminator) in cases)
        {
            var json = JsonSerializer.Serialize(field);
            var roundTrip = JsonSerializer.Deserialize<UserDecisionField>(json);

            Assert.Contains($"\"Type\":\"{discriminator}\"", json, StringComparison.Ordinal);
            Assert.NotNull(roundTrip);
            Assert.Equal(field.Type, roundTrip.Type);
            Assert.Empty(UserDecisionValidator.ValidateRequest(CreateRequest(roundTrip)));
            Assert.Equal(json, JsonSerializer.Serialize(roundTrip));
        }
    }

    [Fact]
    public void 字段校验_逐类型拒绝典型跨类型属性()
    {
        var invalidFields = new[]
        {
            new UserDecisionField(
                "single",
                "单选",
                UserDecisionFieldType.SingleSelect,
                options: [new UserDecisionOption("a", "A")],
                defaultText: "不应存在"),
            new UserDecisionField(
                "multi",
                "多选",
                UserDecisionFieldType.MultiSelect,
                options: [new UserDecisionOption("a", "A")],
                defaultNumber: 1),
            new UserDecisionField(
                "number",
                "数值",
                UserDecisionFieldType.Number,
                options: [new UserDecisionOption("a", "A")]),
            new UserDecisionField(
                "text",
                "文本",
                UserDecisionFieldType.Text,
                minSelections: 1),
        };

        foreach (var field in invalidFields)
        {
            var errors = UserDecisionValidator.ValidateRequest(CreateRequest(field));

            Assert.Contains(errors, error =>
                error.FieldId == field.Id
                && error.Message.Contains("不适用", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void 取消结果_不携带请求默认字段值()
    {
        var field = CreateTextField("note", defaultText: "默认说明");
        var request = CreateRequest(field);

        Assert.Empty(UserDecisionValidator.ValidateRequest(request));

        var result = UserDecisionResult.Cancel("用户取消");

        Assert.True(result.Cancelled);
        Assert.Equal("用户取消", result.CancellationReason);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void 模型集合_复制调用方数据以保持不可变()
    {
        var options = new List<UserDecisionOption> { new("safe", "安全模式") };
        var fields = new List<UserDecisionField>
        {
            new(
                id: "mode",
                label: "运行模式",
                type: UserDecisionFieldType.SingleSelect,
                options: options),
        };
        var request = new UserDecisionRequest("确认设置", "请选择运行模式。", fields);
        var submitted = new List<string> { "safe" };
        var values = new Dictionary<string, object?> { ["mode"] = submitted };
        var result = UserDecisionResult.Submit(values);

        options.Add(new UserDecisionOption("fast", "快速模式"));
        fields.Clear();
        submitted.Add("fast");
        values.Clear();

        Assert.Single(request.Fields);
        Assert.Single(request.Fields[0].Options);
        Assert.Equal(["safe"], Assert.IsAssignableFrom<IReadOnlyList<string>>(result.Values["mode"]));
    }

    private static UserDecisionField CreateValidField(UserDecisionFieldType type) => type switch
    {
        UserDecisionFieldType.SingleSelect => new UserDecisionField(
            "single",
            "单选",
            type,
            options: [new UserDecisionOption("safe", "安全")],
            defaultOptionId: "safe"),
        UserDecisionFieldType.MultiSelect => new UserDecisionField(
            "multi",
            "多选",
            type,
            options: [new UserDecisionOption("safe", "安全")],
            defaultOptionIds: ["safe"],
            minSelections: 1,
            maxSelections: 1),
        UserDecisionFieldType.Number => new UserDecisionField(
            "number",
            "数值",
            type,
            defaultNumber: 2,
            minNumber: 0,
            maxNumber: 10,
            step: 2),
        UserDecisionFieldType.Text => new UserDecisionField(
            "text",
            "文本",
            type,
            defaultText: "普通文本",
            isMultiline: true,
            maxLength: 100),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static UserDecisionRequest CreateRequest(params UserDecisionField[] fields) =>
        new("确认设置", "请选择后续操作。", fields);

    private static UserDecisionField CreateTextField(
        string id,
        bool isRequired = false,
        int? maxLength = null,
        string? defaultText = null) =>
        new(
            id: id,
            label: "补充说明",
            type: UserDecisionFieldType.Text,
            isRequired: isRequired,
            defaultText: defaultText,
            maxLength: maxLength);
}
