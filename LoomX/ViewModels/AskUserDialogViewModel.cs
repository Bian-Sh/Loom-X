using System.Collections.ObjectModel;
using LoomX.Assistant.UserDecisions;

namespace LoomX.ViewModels;

/// <summary>把挂起的用户决策请求投影为可绑定表单，并在提交前执行安全校验。</summary>
public sealed class AskUserDialogViewModel : NotifyViewModel
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    private readonly UserDecisionRequest request;
    private string errorSummary = string.Empty;

    public AskUserDialogViewModel(PendingUserDecision pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        RequestId = pending.RequestId;
        request = pending.Request;
        Fields = request.Fields.Select(CreateField).ToArray();
        ValidateFields();
    }

    public string RequestId { get; }

    public string Title => request.Title;

    public string Question => request.Question;

    public string? Description => request.Description;

    public string? ImpactSummary => request.ImpactSummary;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasImpactSummary => !string.IsNullOrWhiteSpace(ImpactSummary);

    public bool AllowCancel => request.AllowCancel;

    public IReadOnlyList<AskUserFieldViewModel> Fields { get; }

    public string ErrorSummary
    {
        get => errorSummary;
        private set
        {
            if (SetProperty(ref errorSummary, value))
            {
                OnPropertyChanged(nameof(HasErrors));
            }
        }
    }

    public bool HasErrors => ErrorSummary.Length > 0;

    public bool TryBuildResult(out IReadOnlyDictionary<string, object?> values)
    {
        var projected = BuildValues();
        if (!ValidateFields(projected))
        {
            values = EmptyValues;
            return false;
        }

        values = new ReadOnlyDictionary<string, object?>(projected);
        return true;
    }

    private AskUserFieldViewModel CreateField(UserDecisionField field) => field.Type switch
    {
        UserDecisionFieldType.SingleSelect => new AskUserSingleSelectFieldViewModel(field, ValidateFields),
        UserDecisionFieldType.MultiSelect => new AskUserMultiSelectFieldViewModel(field, ValidateFields),
        UserDecisionFieldType.Number => new AskUserNumberFieldViewModel(field, ValidateFields),
        UserDecisionFieldType.Text => new AskUserTextFieldViewModel(field, ValidateFields),
        _ => throw new ArgumentOutOfRangeException(nameof(field), "不支持的用户决策字段类型。"),
    };

    private Dictionary<string, object?> BuildValues()
    {
        var values = new Dictionary<string, object?>(Fields.Count, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            values.Add(field.Id, field.GetValue());
        }

        return values;
    }

    private void ValidateFields() => ValidateFields(BuildValues());

    private bool ValidateFields(IReadOnlyDictionary<string, object?> values)
    {
        var errors = UserDecisionValidator.ValidateSubmission(request, values);
        foreach (var field in Fields)
        {
            field.ErrorMessage = errors.FirstOrDefault(error =>
                string.Equals(error.FieldId, field.Id, StringComparison.Ordinal))?.Message ?? string.Empty;
        }

        ErrorSummary = errors.Count == 0 ? string.Empty : "请检查决策字段后重试。";
        return errors.Count == 0;
    }
}

public abstract class AskUserFieldViewModel : NotifyViewModel
{
    private string errorMessage = string.Empty;

    protected AskUserFieldViewModel(UserDecisionField field, Action changed)
    {
        Field = field;
        Changed = changed;
    }

    protected UserDecisionField Field { get; }

    protected Action Changed { get; }

    public string Id => Field.Id;

    public string Label => Field.Label;

    public bool IsRequired => Field.IsRequired;

    public string RequiredSuffix => IsRequired ? " *" : string.Empty;

    public string ErrorMessage
    {
        get => errorMessage;
        internal set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => ErrorMessage.Length > 0;

    internal abstract object? GetValue();
}

public sealed class AskUserSingleSelectFieldViewModel : AskUserFieldViewModel
{
    private bool updatingSelection;

    public AskUserSingleSelectFieldViewModel(UserDecisionField field, Action changed)
        : base(field, changed)
    {
        Options = field.Options
            .Select(option => new AskUserOptionViewModel(
                option,
                string.Equals(option.Id, field.DefaultOptionId, StringComparison.Ordinal),
                OnOptionChanged))
            .ToArray();
    }

    public IReadOnlyList<AskUserOptionViewModel> Options { get; }

    public string? SelectedOptionId
    {
        get => Options.FirstOrDefault(option => option.IsSelected)?.Id;
        set
        {
            updatingSelection = true;
            try
            {
                foreach (var option in Options)
                {
                    option.IsSelected = string.Equals(option.Id, value, StringComparison.Ordinal);
                }
            }
            finally
            {
                updatingSelection = false;
            }

            OnPropertyChanged();
            Changed();
        }
    }

    internal override object? GetValue() => SelectedOptionId;

    private void OnOptionChanged(AskUserOptionViewModel selected)
    {
        if (updatingSelection)
        {
            return;
        }

        if (selected.IsSelected)
        {
            updatingSelection = true;
            try
            {
                foreach (var option in Options)
                {
                    if (!ReferenceEquals(option, selected))
                    {
                        option.IsSelected = false;
                    }
                }
            }
            finally
            {
                updatingSelection = false;
            }
        }

        OnPropertyChanged(nameof(SelectedOptionId));
        Changed();
    }
}

public sealed class AskUserMultiSelectFieldViewModel : AskUserFieldViewModel
{
    public AskUserMultiSelectFieldViewModel(UserDecisionField field, Action changed)
        : base(field, changed)
    {
        var defaults = field.DefaultOptionIds.ToHashSet(StringComparer.Ordinal);
        Options = field.Options
            .Select(option => new AskUserOptionViewModel(option, defaults.Contains(option.Id), _ => Changed()))
            .ToArray();
    }

    public IReadOnlyList<AskUserOptionViewModel> Options { get; }

    internal override object GetValue() =>
        Array.AsReadOnly(Options.Where(option => option.IsSelected).Select(option => option.Id).ToArray());
}

public sealed class AskUserNumberFieldViewModel : AskUserFieldViewModel
{
    private decimal? numberValue;

    public AskUserNumberFieldViewModel(UserDecisionField field, Action changed)
        : base(field, changed)
    {
        numberValue = field.DefaultNumber;
    }

    public decimal? NumberValue
    {
        get => numberValue;
        set
        {
            if (SetProperty(ref numberValue, value))
            {
                Changed();
            }
        }
    }

    public decimal Minimum => Field.MinNumber ?? decimal.MinValue;

    public decimal Maximum => Field.MaxNumber ?? decimal.MaxValue;

    public decimal Increment => Field.Step ?? 1;

    internal override object? GetValue() => NumberValue;
}

public sealed class AskUserTextFieldViewModel : AskUserFieldViewModel
{
    private string textValue;

    public AskUserTextFieldViewModel(UserDecisionField field, Action changed)
        : base(field, changed)
    {
        textValue = field.DefaultText ?? string.Empty;
    }

    public string TextValue
    {
        get => textValue;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref textValue, value))
            {
                Changed();
            }
        }
    }

    public bool IsMultiline => Field.IsMultiline;

    public int MaxLength => Field.MaxLength ?? 1000;

    internal override object GetValue() => TextValue;
}

public sealed class AskUserOptionViewModel : NotifyViewModel
{
    private readonly Action<AskUserOptionViewModel> changed;
    private bool isSelected;

    public AskUserOptionViewModel(
        UserDecisionOption option,
        bool isSelected,
        Action<AskUserOptionViewModel> changed)
    {
        Id = option.Id;
        Label = option.Label;
        Description = option.Description;
        this.isSelected = isSelected;
        this.changed = changed;
    }

    public string Id { get; }

    public string Label { get; }

    public string? Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                changed(this);
            }
        }
    }
}
