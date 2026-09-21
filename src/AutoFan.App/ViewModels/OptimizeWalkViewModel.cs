using System.ComponentModel;

namespace AutoFan.App.ViewModels;

public enum OptimizeWalkStep
{
    Consent,
    Watch,
    Fans,
    Hold,
}

public enum OptimizeWalkPhase
{
    Ready,
    Running,
    Done,
}

public sealed record WalkStepMarker(string Title, bool IsCurrent, bool IsDone);

public sealed class OptimizeWalkViewModel : INotifyPropertyChanged
{
    public const string WindowTitle = "Optimize this PC";

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? PrimaryRequested;

    public event EventHandler? CancelRequested;

    public OptimizeWalkStep Step { get; private set; } = OptimizeWalkStep.Consent;

    public OptimizeWalkPhase Phase { get; private set; } = OptimizeWalkPhase.Ready;

    public string Headline { get; private set; } = "Before we start";

    public string Detail { get; private set; } = ConsentDetail;

    public string StatusText { get; private set; } = string.Empty;

    public string PrimaryText { get; private set; } = "Start";

    public bool CanPrimary { get; private set; } = true;

    public bool CanCancel { get; private set; } = true;

    public bool IsBusy => Phase == OptimizeWalkPhase.Running;

    public bool TestsStarted { get; private set; }

    public bool HoldStarted { get; private set; }

    public IReadOnlyList<WalkStepMarker> Steps { get; private set; } = Markers(OptimizeWalkStep.Consent, false);

    public void RequestPrimary()
    {
        if (!CanPrimary)
        {
            return;
        }

        if (Step == OptimizeWalkStep.Consent && Phase == OptimizeWalkPhase.Ready)
        {
            GoTo(OptimizeWalkStep.Watch, OptimizeWalkPhase.Ready);
            return;
        }

        if (Step == OptimizeWalkStep.Watch && Phase == OptimizeWalkPhase.Done)
        {
            GoTo(OptimizeWalkStep.Fans, OptimizeWalkPhase.Ready);
            return;
        }

        if (Step == OptimizeWalkStep.Fans && Phase == OptimizeWalkPhase.Done)
        {
            GoTo(OptimizeWalkStep.Hold, OptimizeWalkPhase.Ready);
            return;
        }

        PrimaryRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCancel()
    {
        if (!CanCancel)
        {
            return;
        }

        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    public void BeginCurrent()
    {
        if (Step == OptimizeWalkStep.Watch)
        {
            TestsStarted = true;
        }

        if (Step == OptimizeWalkStep.Fans)
        {
            TestsStarted = true;
        }

        if (Step == OptimizeWalkStep.Hold)
        {
            HoldStarted = true;
        }

        Phase = OptimizeWalkPhase.Running;
        CanPrimary = false;
        Publish();
    }

    public void ReportStatus(string message)
    {
        StatusText = message;
        OnPropertyChanged(nameof(StatusText));
    }

    public void CompleteCurrent(string result)
    {
        StatusText = result;
        Phase = OptimizeWalkPhase.Done;
        CanPrimary = Step != OptimizeWalkStep.Hold;
        if (Step == OptimizeWalkStep.Hold)
        {
            CanCancel = false;
        }

        Publish();
    }

    public void Fail(string detail)
    {
        StatusText = detail;
        Phase = OptimizeWalkPhase.Ready;
        CanPrimary = Step is not OptimizeWalkStep.Consent;
        Publish();
    }

    private void GoTo(OptimizeWalkStep step, OptimizeWalkPhase phase)
    {
        Step = step;
        Phase = phase;
        StatusText = string.Empty;
        CanPrimary = true;
        Publish();
    }

    private void Publish()
    {
        (Headline, Detail, PrimaryText) = CopyFor(Step, Phase);
        Steps = Markers(Step, Phase == OptimizeWalkPhase.Done);
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(CanPrimary));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(TestsStarted));
        OnPropertyChanged(nameof(HoldStarted));
    }

    private static (string Headline, string Detail, string Primary) CopyFor(
        OptimizeWalkStep step,
        OptimizeWalkPhase phase)
    {
        return (step, phase) switch
        {
            (OptimizeWalkStep.Consent, _) =>
                ("Before we start", ConsentDetail, "Start"),
            (OptimizeWalkStep.Watch, OptimizeWalkPhase.Ready) =>
                ("Watch this PC", WatchDetail, "Start watching"),
            (OptimizeWalkStep.Watch, OptimizeWalkPhase.Running) =>
                ("Watch this PC", WatchDetail, "Start watching"),
            (OptimizeWalkStep.Watch, OptimizeWalkPhase.Done) =>
                ("Watch this PC", WatchDetail, "Continue"),
            (OptimizeWalkStep.Fans, OptimizeWalkPhase.Ready) =>
                ("Test fans", FansDetail, "Start fan tests"),
            (OptimizeWalkStep.Fans, OptimizeWalkPhase.Running) =>
                ("Test fans", FansDetail, "Start fan tests"),
            (OptimizeWalkStep.Fans, OptimizeWalkPhase.Done) =>
                ("Test fans", FansDetail, "Continue"),
            (OptimizeWalkStep.Hold, OptimizeWalkPhase.Ready) =>
                ("Hold this setting", HoldDetail, "Hold this setting"),
            (OptimizeWalkStep.Hold, OptimizeWalkPhase.Running) =>
                ("Hold this setting", HoldDetail, "Hold this setting"),
            (OptimizeWalkStep.Hold, OptimizeWalkPhase.Done) =>
                ("Hold this setting", HoldDetail, "Done"),
            _ => ("Optimize this PC", ConsentDetail, "Start"),
        };
    }

    private static IReadOnlyList<WalkStepMarker> Markers(OptimizeWalkStep current, bool currentDone)
    {
        return
        [
            Marker("Before we start", OptimizeWalkStep.Consent, current, currentDone),
            Marker("Watch this PC", OptimizeWalkStep.Watch, current, currentDone),
            Marker("Test fans", OptimizeWalkStep.Fans, current, currentDone),
            Marker("Hold this setting", OptimizeWalkStep.Hold, current, currentDone),
        ];
    }

    private static WalkStepMarker Marker(
        string title,
        OptimizeWalkStep step,
        OptimizeWalkStep current,
        bool currentDone)
    {
        bool isCurrent = step == current;
        bool isDone = step < current || (isCurrent && currentDone);
        return new WalkStepMarker(title, isCurrent, isDone);
    }

    private const string ConsentDetail =
        "AUTO Fan will watch temperatures, then change motherboard fans and NVIDIA GPU fans to see what cools this PC. That can take a while. Cancel, close, Stop, or a temperature limit puts motherboard fans back on BIOS and NVIDIA fans back on the driver.";

    private const string WatchDetail =
        "This step does not change fans. It heats the CPU and GPU a little so later tests have a baseline.";

    private const string FansDetail =
        "Each connected fan is set to a few speeds. Pair tests run only if those measurements finish. You can cancel; finished measurements still help.";

    private const string HoldDetail =
        "Apply the quiet-versus-cool setting chosen on Home. Motherboard and NVIDIA fans follow it while AUTO Fan stays open.";

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
