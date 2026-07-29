namespace ImageCleanup.App.ViewModels;

public sealed class StagingEntryViewModel
{
    public int StagingId { get; }
    public string DisplayText { get; }
    public string Action { get; }

    public StagingEntryViewModel(int stagingId, string filePath, string action)
    {
        StagingId   = stagingId;
        DisplayText = Path.GetFileName(filePath);
        Action      = action;
    }
}
