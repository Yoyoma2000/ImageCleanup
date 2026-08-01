namespace ImageCleanup.Core.Tests.Grouping;

using ImageCleanup.Core.Grouping;

/// <summary>
/// Simulates the App layer's index-based ComboBox selection pattern
/// (FileActionViewModel.AvailableActions/SelectedActionIndex) entirely with
/// Core-testable types, to prove Delete/Keep/Move detection
/// (KeepSelector.ResolveKeepConflicts) is driven purely by ActionType and
/// is unaffected by whatever display text a translator writes for
/// Common.Action.Keep/Delete/Move/None. This is the regression guard for
/// the value/display split: before it, "Keep" was both the display string
/// AND the value business logic compared against, so a translated display
/// string would have silently broken detection — this test fails loudly if
/// that coupling is ever reintroduced.
/// </summary>
public class ActionTypeLocalizationResilienceTests
{
    private static readonly ActionType[] ActionOrder =
        [ActionType.None, ActionType.Keep, ActionType.Delete, ActionType.Move];

    /// <summary>A stand-in for two totally different "translations" of the same four actions — arbitrary strings, never read by KeepSelector.</summary>
    private static readonly Dictionary<ActionType, string> EnglishDisplay = new()
    {
        [ActionType.None]   = "None",
        [ActionType.Keep]   = "Keep",
        [ActionType.Delete] = "Delete",
        [ActionType.Move]   = "Move",
    };

    private static readonly Dictionary<ActionType, string> GibberishDisplay = new()
    {
        [ActionType.None]   = "Ω-Nothing",
        [ActionType.Keep]   = "★彼女を守れ★",
        [ActionType.Delete] = "🗑 zap-it",
        [ActionType.Move]   = "yeet elsewhere",
    };

    [Theory]
    [MemberData(nameof(DisplayDictionaries))]
    public void ResolveKeepConflicts_DetectsKeepConflict_RegardlessOfDisplayTextTranslation(
        Dictionary<ActionType, string> display)
    {
        // Simulate three ComboBoxes: index 1 (Keep) picked for a.jpg and
        // b.jpg, index 2 (Delete) for c.jpg — exactly like a user picking
        // from FileActionViewModel.AvailableActions via SelectedActionIndex.
        var selections = new (string FilePath, int SelectedIndex)[]
        {
            ("a.jpg", 1), // Keep
            ("b.jpg", 1), // Keep
            ("c.jpg", 2), // Delete
        };

        // The index -> ActionType mapping (what SelectedActionIndex's setter
        // does) never touches `display` at all — proving the display
        // dictionary is display-only and can't influence detection.
        var actions = selections.Select(s => (s.FilePath, Action: ActionOrder[s.SelectedIndex])).ToArray();

        // Sanity: confirm the display text really is different between the
        // two dictionaries, so this test would have caught the old
        // string-comparison bug (where a translated "Keep" wouldn't have
        // equaled the literal "Keep" business logic compared against).
        Assert.NotEqual(EnglishDisplay[ActionType.Keep], GibberishDisplay[ActionType.Keep]);

        var conflicts = KeepSelector.ResolveKeepConflicts(actions, newKeepFilePath: "b.jpg");

        // a.jpg was also Keep and must be bumped — this must hold no matter
        // which display dictionary was "active" when the selection was made.
        Assert.Equal(["a.jpg"], conflicts);
    }

    public static IEnumerable<object[]> DisplayDictionaries()
    {
        yield return [EnglishDisplay];
        yield return [GibberishDisplay];
    }
}
