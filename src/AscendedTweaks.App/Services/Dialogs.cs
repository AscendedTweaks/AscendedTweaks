using System.Collections.ObjectModel;
using System.Windows;
using AscendedTweaks.Engine;
using AscendedTweaks.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AscendedTweaks.App.Services;

/// <summary>Fuehrt Dialoge aus einem Hintergrund-Thread synchron im UI-Thread aus (Rueckfragen der Engine).</summary>
public interface IUiInvoker
{
    T Invoke<T>(Func<Task<T>> action);
}

public sealed class WpfUiInvoker : IUiInvoker
{
    public T Invoke<T>(Func<Task<T>> action)
    {
        return Application.Current.Dispatcher.Invoke(action).GetAwaiter().GetResult();
    }
}

/// <summary>Dialog mit Ergebnis. Die Oberflaeche zeigt Dialoge als Ebene ueber dem Fenster (kein zweites Fenster).</summary>
public abstract class DialogViewModel : ObservableObject
{
    public abstract void Cancel();
}

public abstract partial class DialogViewModel<T> : DialogViewModel
{
    private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<T> Completion => _completion.Task;

    public void Close(T result) => _completion.TrySetResult(result);
}

public interface IDialogService
{
    Task<T> ShowAsync<T>(DialogViewModel<T> dialog);

    /// <summary>Zeigt einen Dialog ohne Warten (Fortschritt); Schliessen ueber <see cref="Dismiss"/>.</summary>
    void Show(DialogViewModel dialog);

    void Dismiss(DialogViewModel dialog);
}

/// <summary>Dialog-Stapel; der oberste Dialog liegt vorn, die Seite darunter ist abgedunkelt und nicht bedienbar.</summary>
public sealed class OverlayDialogService : IDialogService
{
    public ObservableCollection<DialogViewModel> Stack { get; } = [];

    public async Task<T> ShowAsync<T>(DialogViewModel<T> dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        Stack.Add(dialog);
        try
        {
            return await dialog.Completion.ConfigureAwait(true);
        }
        finally
        {
            Stack.Remove(dialog);
        }
    }

    public void Show(DialogViewModel dialog) => Stack.Add(dialog);

    public void Dismiss(DialogViewModel dialog) => Stack.Remove(dialog);
}

/// <summary>Einfache Rueckfrage mit Bestaetigen und Abbrechen.</summary>
public sealed partial class ConfirmDialogViewModel : DialogViewModel<bool>
{
    public ConfirmDialogViewModel(string title, string body, string confirmText, string cancelText, bool danger = false)
    {
        Title = title;
        Body = body;
        ConfirmText = confirmText;
        CancelText = cancelText;
        IsDanger = danger;
    }

    public string Title { get; }

    public string Body { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public bool IsDanger { get; }

    public override void Cancel() => Close(false);

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Decline() => Close(false);
}

/// <summary>Texteingabe (zum Beispiel Profilname); null bei Abbruch. Bestaetigen nur mit gueltiger Eingabe.</summary>
public sealed partial class TextInputDialogViewModel : DialogViewModel<string?>
{
    private readonly Func<string, bool> _isValid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _text;

    public TextInputDialogViewModel(string title, string label, string initial, string confirmText, string cancelText, Func<string, bool> isValid, string invalidText)
    {
        Title = title;
        Label = label;
        _text = initial;
        ConfirmText = confirmText;
        CancelText = cancelText;
        _isValid = isValid;
        InvalidText = invalidText;
    }

    public string Title { get; }

    public string Label { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    public string InvalidText { get; }

    public string? Error => Text.Length == 0 || IsValid ? null : InvalidText;

    private bool IsValid => _isValid(Text.Trim());

    public override void Cancel() => Close(null);

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm() => Close(Text.Trim());

    [RelayCommand]
    private void Decline() => Close(null);
}

/// <summary>V2: Gruppe in der Klartext-Vorschau, zum Beispiel "Windows reagiert schneller (4)".</summary>
public sealed record PreviewGroup(string Title, IReadOnlyList<string> Items);

public sealed record PreviewRow(string Tweak, string Target, string Current, string New);

public sealed record NoteRow(string Tweak, string Text);

/// <summary>Vorschau (Dry-Run, Regel S3): Es wird noch nichts geaendert.</summary>
public sealed partial class PreviewDialogViewModel : DialogViewModel<bool>
{
    public PreviewDialogViewModel(IReadOnlyList<PreviewRow> rows, IReadOnlyList<NoteRow> excluded, IReadOnlyList<string> reverts, bool rebootRequired, bool adminRequired, bool previewOnly, bool userOnly = false)
    {
        UserOnly = userOnly;
        Rows = rows;
        Excluded = excluded;
        Reverts = reverts;
        RebootRequired = rebootRequired;
        AdminRequired = adminRequired;
        PreviewOnly = previewOnly;
    }

    public IReadOnlyList<PreviewRow> Rows { get; }

    public IReadOnlyList<NoteRow> Excluded { get; }

    public IReadOnlyList<string> Reverts { get; }

    public bool RebootRequired { get; }

    public bool AdminRequired { get; }

    /// <summary>Nur ansehen ("Vorschau" in der Aenderungsleiste), ohne Anwenden-Schaltflaeche.</summary>
    public bool PreviewOnly { get; }

    /// <summary>Nur Benutzer-Tweaks: kein Wiederherstellungspunkt (braucht Admin); der Hinweis steht in der Vorschau.</summary>
    public bool UserOnly { get; }

    public bool HasExcluded => Excluded.Count > 0;

    public bool HasReverts => Reverts.Count > 0;

    /// <summary>V2: "Das wird geaendert" in Klartext, gruppiert; die technische Tabelle ist dann eingeklappt.</summary>
    public IReadOnlyList<PreviewGroup> Groups { get; init; } = [];

    public bool HasGroups => Groups.Count > 0;

    [ObservableProperty]
    private bool _showTechnical;

    public bool TableVisible => !HasGroups || ShowTechnical;

    partial void OnShowTechnicalChanged(bool value) => OnPropertyChanged(nameof(TableVisible));

    [RelayCommand]
    private void ToggleTechnical() => ShowTechnical = !ShowTechnical;

    public override void Cancel() => Close(false);

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Decline() => Close(false);
}

/// <summary>Experten-Warnung in drei Stufen: Erklaerung, Kontrollkaestchen, Eingabe des Bestaetigungsworts.</summary>
public sealed partial class ExpertWarningViewModel : DialogViewModel<bool>
{
    private readonly Localizer _localizer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    private int _stage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    private bool _understood;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    private string _typed = string.Empty;

    public ExpertWarningViewModel(Localizer localizer, IReadOnlyList<string> tweaks)
    {
        _localizer = localizer;
        Tweaks = tweaks;
    }

    public IReadOnlyList<string> Tweaks { get; }

    /// <summary>"VERSTANDEN" bzw. "UNDERSTOOD" je nach Sprache.</summary>
    public string Keyword => _localizer["expert.keyword"];

    public bool CanContinue => Stage switch
    {
        1 => true,
        2 => Understood,
        _ => string.Equals(Typed.Trim(), Keyword, StringComparison.Ordinal),
    };

    public override void Cancel() => Close(false);

    [RelayCommand]
    private void Continue()
    {
        if (!CanContinue)
        {
            return;
        }

        if (Stage < 3)
        {
            Stage++;
        }
        else
        {
            Close(true);
        }
    }

    [RelayCommand]
    private void Decline() => Close(false);
}

/// <summary>Echter Fortschritt pro Tweak; Abbrechen wirkt zwischen zwei Tweaks.</summary>
public sealed partial class ProgressDialogViewModel : DialogViewModel, IProgress<BatchProgress>
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly Func<string, string> _name;

    [ObservableProperty]
    private int _current;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private string _currentTweak = string.Empty;

    [ObservableProperty]
    private bool _isCancelling;

    public ProgressDialogViewModel(Func<string, string> name) => _name = name;

    public CancellationToken Token => _cancel.Token;

    public bool IsIndeterminate => Total == 0;

    public override void Cancel()
    {
        IsCancelling = true;
        _cancel.Cancel();
    }

    public void Report(BatchProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Total = value.Total;
        Current = value.Index;
        CurrentTweak = _name(value.TweakId);
        OnPropertyChanged(nameof(IsIndeterminate));
    }

    [RelayCommand]
    private void RequestCancel() => Cancel();
}

public sealed record ResultRow(string Tweak, bool Ok, string Message);

public sealed partial class ResultDialogViewModel : DialogViewModel<bool>
{
    public ResultDialogViewModel(string title, string summary, IReadOnlyList<ResultRow> rows, bool rebootRequired)
    {
        Title = title;
        Summary = summary;
        Rows = rows;
        RebootRequired = rebootRequired;
    }

    public string Title { get; }

    public string Summary { get; }

    public IReadOnlyList<ResultRow> Rows { get; }

    public bool RebootRequired { get; }

    /// <summary>V2: grosse Schaltflaeche "Alles wieder rueckgaengig" nach einem Preset; null ohne.</summary>
    public string? UndoText { get; init; }

    public bool HasUndo => UndoText is not null;

    public bool UndoRequested { get; private set; }

    public override void Cancel() => Close(true);

    [RelayCommand]
    private void Done() => Close(true);

    [RelayCommand]
    private void Undo()
    {
        UndoRequested = true;
        Close(true);
    }
}

/// <summary>Konflikt beim Zuruecksetzen (Abschnitt 7.4).</summary>
public sealed partial class ConflictDialogViewModel : DialogViewModel<ConflictDecision>
{
    public ConflictDialogViewModel(string tweak, RevertConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        Tweak = tweak;
        Target = conflict.View.Target;
        Current = conflict.View.Current;
        Original = conflict.View.Original;
    }

    public string Tweak { get; }

    public string Target { get; }

    public string Current { get; }

    public string Original { get; }

    public override void Cancel() => Close(ConflictDecision.Cancel);

    [RelayCommand]
    private void RestoreOriginal() => Close(ConflictDecision.RestoreOriginal);

    [RelayCommand]
    private void KeepCurrent() => Close(ConflictDecision.KeepCurrent);

    [RelayCommand]
    private void Abort() => Close(ConflictDecision.Cancel);
}
