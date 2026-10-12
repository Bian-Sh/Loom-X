using System.ComponentModel;

namespace LoomX.Services;

/// <summary>桌面启动阶段，用于驱动启动闪屏和启动编排器。</summary>
public enum StartupPhase
{
    NotStarted,
    Database,
    PluginCompile,
    PluginLoad,
    Gateway,
    Shell,
    Completed,
    Failed
}

/// <summary>
/// 启动进度状态机。只允许阶段向前推进，失败后进入终态。
/// </summary>
public sealed class StartupProgress : INotifyPropertyChanged
{
    private static readonly StartupPhase[] OrderedPhases =
    [
        StartupPhase.NotStarted,
        StartupPhase.Database,
        StartupPhase.PluginCompile,
        StartupPhase.PluginLoad,
        StartupPhase.Gateway,
        StartupPhase.Shell,
        StartupPhase.Completed
    ];

    private StartupPhase currentPhase = StartupPhase.NotStarted;
    private int progressValue;
    private string? errorMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public StartupPhase CurrentPhase
    {
        get => currentPhase;
        private set
        {
            if (currentPhase == value) return;
            currentPhase = value;
            OnPropertyChanged(nameof(CurrentPhase));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsCompleted));
        }
    }

    public int ProgressValue
    {
        get => progressValue;
        private set
        {
            if (progressValue == value) return;
            progressValue = value;
            OnPropertyChanged(nameof(ProgressValue));
        }
    }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (string.Equals(errorMessage, value, StringComparison.Ordinal)) return;
            errorMessage = value;
            OnPropertyChanged(nameof(ErrorMessage));
        }
    }

    public bool IsFailed => CurrentPhase == StartupPhase.Failed;

    public bool IsCompleted => CurrentPhase == StartupPhase.Completed;

    public bool AdvanceTo(StartupPhase phase)
    {
        if (phase is StartupPhase.Completed or StartupPhase.Failed)
            return false;

        var targetIndex = Array.IndexOf(OrderedPhases, phase);
        var currentIndex = Array.IndexOf(OrderedPhases, CurrentPhase);
        if (targetIndex < 0 || targetIndex <= currentIndex)
            return false;

        CurrentPhase = phase;
        RefreshProgress();
        return true;
    }

    public void Complete()
    {
        if (CurrentPhase is StartupPhase.Completed or StartupPhase.Failed)
            return;

        CurrentPhase = StartupPhase.Completed;
        ProgressValue = 100;
        ErrorMessage = null;
    }

    public void Fail(string message)
    {
        CurrentPhase = StartupPhase.Failed;
        ErrorMessage = message;
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsCompleted));
    }

    private void RefreshProgress()
    {
        var index = Array.IndexOf(OrderedPhases, CurrentPhase);
        ProgressValue = index <= 0 ? 0 : (int)Math.Round(index * 100.0 / (OrderedPhases.Length - 1), MidpointRounding.AwayFromZero);
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
