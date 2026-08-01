using System.ComponentModel;
using System.Runtime.CompilerServices;
using ImageCleanup.Core.Grouping;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ImageCleanup.App.ViewModels;

public sealed class FileActionViewModel : INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>Index order backing both AvailableActions and SelectedActionIndex — None/Keep/Delete/Move.</summary>
    private static readonly ActionType[] ActionOrder = [ActionType.None, ActionType.Keep, ActionType.Delete, ActionType.Move];

    /// <summary>
    /// Localized display text for the ComboBox, in ActionOrder — resolved
    /// once per instance (a fresh scan/rebuild creates fresh instances, so
    /// this reflects whatever language was active at that time; see
    /// LocalizationService's remarks on restart-required for already-
    /// rendered content). Never compared in logic — see SelectedActionType.
    /// </summary>
    public IReadOnlyList<string> AvailableActions { get; } = ActionOrder.Select(ActionDisplay.GetDisplayText).ToList();

    public int FileRecordId { get; init; }
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Null unless the caller (e.g. Quality) supplied one — unused by Duplicates.</summary>
    public double? BlurScore { get; init; }

    /// <summary>Display-friendly BlurScore for XAML binding without a converter.</summary>
    public string BlurScoreDisplay => BlurScore.HasValue ? BlurScore.Value.ToString("F1") : "—";

    /// <summary>True when this file is the group's current keep choice (SelectedActionType == Keep).</summary>
    public bool IsSuggested => _selectedActionType == ActionType.Keep;

    private ImageSource? _thumbnail;
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set { _thumbnail = value; Notify(); }
    }

    /// <summary>
    /// Kicks off async thumbnail generation. The bytes are produced by
    /// <paramref name="generateBytes"/> off the UI thread (e.g. via ThumbnailCache);
    /// the resulting <see cref="Thumbnail"/> populates once decoding completes.
    /// </summary>
    public void RequestThumbnail(Func<byte[]?> generateBytes) =>
        ThumbnailLoader.RequestThumbnail(_dispatcher, generateBytes, bitmap => Thumbnail = bitmap);

    private ImageSource? _detailThumbnail;
    /// <summary>Larger preview shown in the group detail dialog — a separate cache entry/size from <see cref="Thumbnail"/>.</summary>
    public ImageSource? DetailThumbnail
    {
        get => _detailThumbnail;
        private set { _detailThumbnail = value; Notify(); }
    }

    /// <summary>Same as <see cref="RequestThumbnail"/> but populates <see cref="DetailThumbnail"/> instead.</summary>
    public void RequestDetailThumbnail(Func<byte[]?> generateBytes) =>
        ThumbnailLoader.RequestThumbnail(_dispatcher, generateBytes, bitmap => DetailThumbnail = bitmap);

    // XAML visibility helpers — no converter needed. "Keep" is a real, always-selectable
    // action now (see AvailableActions), so the ComboBox itself is always shown — only
    // the small star badge/border track whether this file is the current keep choice.
    public Visibility SuggestedBadgeVisibility => IsSuggested ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MoveTargetVisibility     => _selectedActionType == ActionType.Move ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Highlights the current keep-choice file with a visible border in the detail view.</summary>
    public Thickness KeepBorderThickness => IsSuggested ? new Thickness(3) : new Thickness(0);

    private ActionType _selectedActionType;

    /// <summary>
    /// The stable, language-independent action — this is what business
    /// logic (OnFileActionChanged, KeepSelector, CommitService via
    /// ActionDisplay.ToStagingValue) reads/compares. Never compare display
    /// text against a literal like "Delete" — that text is localized and
    /// will not match once a non-Dev language is active.
    /// </summary>
    public ActionType SelectedActionType
    {
        get => _selectedActionType;
        set
        {
            if (_selectedActionType == value) return;
            _selectedActionType = value;
            Notify();
            Notify(nameof(SelectedActionIndex));
            Notify(nameof(MoveTargetVisibility));
            Notify(nameof(IsSuggested));
            Notify(nameof(SuggestedBadgeVisibility));
            Notify(nameof(KeepBorderThickness));
            if (!_suppress) ActionChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// Index-based mirror of <see cref="SelectedActionType"/> for binding to
    /// ComboBox.SelectedIndex — every action ComboBox in the app binds this,
    /// not SelectedItem/display text, for two reasons: a virtualizing
    /// ListView/GridView reusing a container for a different
    /// FileActionViewModel can leave a SelectedItem-bound ComboBox showing
    /// blank on reuse (see Quality's original ComboBox-blanking bug fix),
    /// and — now that AvailableActions holds localized display strings
    /// rather than stable values — SelectedItem would have to reverse-map a
    /// translated string back to an ActionType, which SelectedIndex avoids
    /// needing entirely.
    /// </summary>
    public int SelectedActionIndex
    {
        get => Array.IndexOf(ActionOrder, _selectedActionType);
        set
        {
            if (value < 0 || value >= ActionOrder.Length) return;
            SelectedActionType = ActionOrder[value];
        }
    }

    private string? _targetPath;
    public string? TargetPath
    {
        get => _targetPath;
        set { if (_targetPath == value) return; _targetPath = value; Notify(); }
    }

    public int? StagingId { get; set; }

    /// <summary>Called when the user changes the action. Set by DuplicatesViewModel after construction.</summary>
    public Action<FileActionViewModel>? ActionChanged { get; set; }

    /// <summary>Duplicates: every group member defaults to Delete except the suggested keep file.</summary>
    public FileActionViewModel(int fileRecordId, string filePath, bool isSuggested)
        : this(fileRecordId, filePath, isSuggested ? ActionType.Keep : ActionType.Delete)
    {
    }

    /// <summary>Quality (and anything else needing an explicit default action, e.g. None): no per-group Keep conflict resolution applies here — that logic lives in DuplicatesViewModel, not this class.</summary>
    public FileActionViewModel(int fileRecordId, string filePath, ActionType initialAction, double? blurScore = null)
    {
        FileRecordId      = fileRecordId;
        FilePath          = filePath;
        BlurScore         = blurScore;
        _selectedActionType = initialAction;
    }

    /// <summary>Silently resets to None (e.g. when user removes the staging entry from the panel).</summary>
    internal void ResetToNone()
    {
        _suppress            = true;
        _selectedActionType  = ActionType.None;
        StagingId            = null;
        _suppress            = false;
        Notify(nameof(SelectedActionType));
        Notify(nameof(SelectedActionIndex));
        Notify(nameof(MoveTargetVisibility));
        Notify(nameof(IsSuggested));
        Notify(nameof(SuggestedBadgeVisibility));
        Notify(nameof(KeepBorderThickness));
    }

    private bool _suppress;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
