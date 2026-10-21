using TweakPilot.Core;

namespace TweakPilot.Engine.Executors;

/// <summary>Jede Aktion kennt Capture, Apply, Verify, Revert (Restore) und Describe (Abschnitt 5.2).</summary>
public interface IActionExecutor
{
    Type ActionType { get; }

    Type CaptureType { get; }

    /// <summary>Eindeutiger Schluessel des Ziels zur Konflikterkennung zwischen Tweaks.</summary>
    string TargetKey(TweakAction action);

    /// <summary>Liest den Ist-Zustand, veraendert nichts.</summary>
    ActionCapture Capture(TweakAction action);

    /// <summary>Fuehrt die Aktion aus und liefert die Sicherung inklusive geschriebenem Zustand.</summary>
    ActionCapture Apply(TweakAction action, ActionCapture before);

    /// <summary>Liest den Zustand erneut und vergleicht mit dem Soll.</summary>
    bool Verify(TweakAction action);

    /// <summary>Stellt den Zustand vor dem Anwenden wieder her. Idempotent, schreibt nur bei Abweichung.</summary>
    void Restore(ActionCapture capture);

    bool IsAtWrittenState(ActionCapture capture);

    bool IsAtOriginalState(ActionCapture capture);

    /// <summary>Text fuer die Vorschau, veraendert nichts.</summary>
    PlanItem Describe(string tweakId, TweakAction action);

    CaptureView View(ActionCapture capture);
}

public abstract class ActionExecutor<TAction, TCapture> : IActionExecutor
    where TAction : TweakAction
    where TCapture : ActionCapture
{
    public Type ActionType => typeof(TAction);

    public Type CaptureType => typeof(TCapture);

    public string TargetKey(TweakAction action) => TargetKey((TAction)action);

    public ActionCapture Capture(TweakAction action) => Capture((TAction)action);

    public ActionCapture Apply(TweakAction action, ActionCapture before) => Apply((TAction)action, (TCapture)before);

    public bool Verify(TweakAction action) => Verify((TAction)action);

    public void Restore(ActionCapture capture) => Restore((TCapture)capture);

    public bool IsAtWrittenState(ActionCapture capture) => IsAtWrittenState((TCapture)capture);

    public bool IsAtOriginalState(ActionCapture capture) => IsAtOriginalState((TCapture)capture);

    public PlanItem Describe(string tweakId, TweakAction action) => Describe(tweakId, (TAction)action);

    public CaptureView View(ActionCapture capture) => View((TCapture)capture);

    protected static string NameOf(TAction action) => ActionTypeNames.NameOf(action);

    protected abstract string TargetKey(TAction action);

    protected abstract TCapture Capture(TAction action);

    protected abstract TCapture Apply(TAction action, TCapture before);

    protected abstract bool Verify(TAction action);

    protected abstract void Restore(TCapture capture);

    protected abstract bool IsAtWrittenState(TCapture capture);

    protected abstract bool IsAtOriginalState(TCapture capture);

    protected abstract PlanItem Describe(string tweakId, TAction action);

    protected abstract CaptureView View(TCapture capture);
}
