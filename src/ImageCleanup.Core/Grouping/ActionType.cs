namespace ImageCleanup.Core.Grouping;

/// <summary>
/// The stable, language-independent action a user can pick for a file
/// (Duplicates/Quality review). Business logic (KeepSelector, staging,
/// CommitService's persisted string) must compare/store THIS, never a
/// display string — display text is a separate, localizable concern (see
/// the App layer's FileActionViewModel.AvailableActions /
/// ActionDisplay.GetDisplayText).
/// </summary>
public enum ActionType
{
    None,
    Keep,
    Delete,
    Move,
}
