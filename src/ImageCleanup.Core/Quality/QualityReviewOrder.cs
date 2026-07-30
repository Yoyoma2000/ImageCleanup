namespace ImageCleanup.Core.Quality;

/// <summary>
/// Pure sort/filter logic behind the Quality feature's review list, extracted
/// from the App-layer ViewModel so it stays unit-testable without WinUI.
/// </summary>
public static class QualityReviewOrder
{
    /// <summary>
    /// Returns <paramref name="files"/> sorted blurriest-first (ascending
    /// BlurScore — lower Laplacian variance means more blur), excluding any
    /// file with a null/missing BlurScore. No threshold or auto-flagging:
    /// this only orders the review list, the user decides what to do with it.
    /// </summary>
    public static IReadOnlyList<T> SortBlurriestFirst<T>(
        IEnumerable<T> files,
        Func<T, double?> blurScoreSelector)
    {
        return files
            .Where(f => blurScoreSelector(f).HasValue)
            .OrderBy(f => blurScoreSelector(f)!.Value)
            .ToList();
    }
}
