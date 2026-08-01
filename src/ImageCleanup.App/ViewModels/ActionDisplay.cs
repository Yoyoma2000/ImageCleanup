using ImageCleanup.Core.Grouping;
using ImageCleanup.Data.Services;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Maps the stable Core.Grouping.ActionType enum to (a) the localized text
/// shown in the UI and (b) the string persisted in the staging tables — one
/// shared place so FileActionViewModel, StagingEntryViewModel, and every
/// ViewModel that stages an action agree on both, rather than each computing
/// them independently.
/// </summary>
public static class ActionDisplay
{
    /// <summary>
    /// The value persisted via IStagingRepository.StageAction and compared
    /// by CommitService's switch — deliberately just ActionType.ToString()
    /// ("None"/"Keep"/"Delete"/"Move"), so the DB contract stays in sync
    /// automatically. This is an internal/storage value, never shown to the
    /// user directly — see GetDisplayText for that. Do not rename ActionType's
    /// members without also migrating any already-staged rows.
    /// </summary>
    public static string ToStagingValue(this ActionType action) => action.ToString();

    /// <summary>Localized display text for a single action — resolves through the currently active language.</summary>
    public static string GetDisplayText(ActionType action) => action switch
    {
        ActionType.None   => LocalizationService.Current.GetString("Common.Action.None"),
        ActionType.Keep   => LocalizationService.Current.GetString("Common.Action.Keep"),
        ActionType.Delete => LocalizationService.Current.GetString("Common.Action.Delete"),
        ActionType.Move   => LocalizationService.Current.GetString("Common.Action.Move"),
        _                 => action.ToString(),
    };
}
