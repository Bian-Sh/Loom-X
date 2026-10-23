using System.Collections.ObjectModel;
using LoomX.Localization;
using LoomX.Services;

namespace LoomX.ViewModels;

public enum SplashStageState
{
    Pending,
    Active,
    Completed,
    Failed
}

public sealed class SplashStageViewModel : NotifyViewModel
{
    private SplashStageState state;

    public SplashStageViewModel(StartupPhase phase, string title)
    {
        Phase = phase;
        Title = title;
    }

    public StartupPhase Phase { get; }
    public string Title { get; }
    public SplashStageState State
    {
        get => state;
        private set
        {
            if (!SetProperty(ref state, value)) return;
            OnPropertyChanged(nameof(Marker));
            OnPropertyChanged(nameof(IsCurrent));
        }
    }

    public bool IsCurrent => State == SplashStageState.Active;
    public string Marker => State switch
    {
        SplashStageState.Completed => "✓",
        SplashStageState.Failed => "!",
        SplashStageState.Active => "●",
        _ => "○"
    };

    internal void Update(StartupPhase currentPhase, bool failed)
    {
        if (failed && Phase == currentPhase)
        {
            State = SplashStageState.Failed;
            return;
        }

        var currentIndex = Array.IndexOf(SplashViewModel.DisplayPhases, currentPhase);
        var phaseIndex = Array.IndexOf(SplashViewModel.DisplayPhases, Phase);
        State = currentPhase == StartupPhase.Completed || phaseIndex < currentIndex
            ? SplashStageState.Completed
            : phaseIndex == currentIndex
                ? SplashStageState.Active
                : SplashStageState.Pending;
    }
}

public sealed class SplashViewModel : NotifyViewModel, IDisposable
{
    internal static readonly StartupPhase[] DisplayPhases =
    [
        StartupPhase.Database,
        StartupPhase.PluginCompile,
        StartupPhase.PluginLoad,
        StartupPhase.Gateway,
        StartupPhase.Shell
    ];

    private readonly StartupProgress progress;
    private bool disposed;

    public SplashViewModel(StartupProgress progress)
    {
        this.progress = progress;
        StageItems = new(DisplayPhases.Select(phase => new SplashStageViewModel(phase, TitleFor(phase))));
        progress.PropertyChanged += OnProgressChanged;
        UpdateFromProgress();
    }

    public ObservableCollection<SplashStageViewModel> StageItems { get; }
    public int ProgressValue => progress.ProgressValue;
    public string CurrentStageText => progress.IsFailed
        ? ResourceLookup.Resolve("app.splash.failed")
        : progress.IsCompleted
            ? ResourceLookup.Resolve("app.splash.completed")
            : TitleFor(progress.CurrentPhase);
    public bool IsFailed => progress.IsFailed;
    public string? ErrorMessage => progress.ErrorMessage;
    public string CloseText => ResourceLookup.Resolve("app.splash.close");
    public string VersionLabel => AppVersion.Label;

    private void OnProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        UpdateFromProgress();
    }

    private void UpdateFromProgress()
    {
        foreach (var stage in StageItems)
            stage.Update(progress.CurrentPhase, progress.IsFailed);

        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(CurrentStageText));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private static string TitleFor(StartupPhase phase) => phase switch
    {
        StartupPhase.Database => ResourceLookup.Resolve("app.splash.stage.database"),
        StartupPhase.PluginCompile => ResourceLookup.Resolve("app.splash.stage.plugin.compile"),
        StartupPhase.PluginLoad => ResourceLookup.Resolve("app.splash.stage.plugin.load"),
        StartupPhase.Gateway => ResourceLookup.Resolve("app.splash.stage.gateway"),
        StartupPhase.Shell => ResourceLookup.Resolve("app.splash.stage.shell"),
        StartupPhase.Completed => ResourceLookup.Resolve("app.splash.completed"),
        _ => ResourceLookup.Resolve("app.splash.starting")
    };

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        progress.PropertyChanged -= OnProgressChanged;
    }
}
