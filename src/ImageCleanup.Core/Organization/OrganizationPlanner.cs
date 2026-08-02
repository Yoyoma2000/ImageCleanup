using System.Globalization;
using ImageCleanup.Core.Grouping;
using ImageCleanup.Core.Metadata;

namespace ImageCleanup.Core.Organization;

/// <summary>
/// Builds a proposed Year/Month/MetadataCategory folder hierarchy for a set
/// of scanned files. Pure planning logic — never touches the filesystem;
/// callers decide separately whether/how to actually move files per the
/// returned plan.
/// </summary>
public static class OrganizationPlanner
{
    /// <summary>
    /// Builds the plan. <paramref name="categoryFolderName"/> resolves a
    /// MetadataCategory to the folder name used for BOTH the real on-disk
    /// path (PlannedFile.TargetFolder) and the display label
    /// (CategoryGroup.Label) — omit it (or pass null) to get the original
    /// behavior, Category.ToString() ("Photo"/"NoMetadata"), which is what
    /// every existing caller/test still gets automatically. A caller can
    /// supply a localized name (e.g. from LocalizationService) instead;
    /// see the App layer's OrganizationViewModel for that wiring, and the
    /// localization-infrastructure session notes in CLAUDE.md for the
    /// Windows-folder-name-safety considerations that come with actually
    /// translating these — CJK text itself is fine as a folder name, but
    /// any translated value must still avoid characters Windows forbids in
    /// folder names.
    /// </summary>
    public static OrganizationPlan BuildHierarchy(
        IEnumerable<ImageRecord> records,
        Func<MetadataCategory, string>? categoryFolderName = null,
        Func<int, string>? monthName = null)
    {
        var resolveCategoryName = categoryFolderName ?? (category => category.ToString());
        var resolveMonthName = monthName ?? (month => new DateTime(1, month, 1).ToString("MMMM", CultureInfo.CurrentCulture));

        var classified = records
            .Select(r => (
                Record: r,
                Date: r.DateTaken ?? r.LastModified,
                Category: MetadataClassifier.ClassifyMetadata(new ExifMetadata { HasExif = r.HasExif })))
            .ToList();

        var years = classified
            .GroupBy(x => x.Date.Year)
            .OrderBy(g => g.Key)
            .Select(yearGroup => new YearGroup
            {
                Year = yearGroup.Key,
                Months = yearGroup
                    .GroupBy(x => x.Date.Month)
                    .OrderBy(g => g.Key)
                    .Select(monthGroup => new MonthGroup
                    {
                        Month = monthGroup.Key,
                        Categories = monthGroup
                            .GroupBy(x => x.Category)
                            .OrderBy(g => g.Key)
                            .Select(categoryGroup =>
                            {
                                var categoryName = resolveCategoryName(categoryGroup.Key);
                                var targetFolder = $"{yearGroup.Key:D4}/{FormatMonthFolder(monthGroup.Key, resolveMonthName)}/{categoryName}";
                                var files = ResolveFileNames(
                                    categoryGroup.Select(x => x.Record),
                                    targetFolder);
                                return new CategoryGroup
                                {
                                    Category = categoryGroup.Key,
                                    Label    = categoryName,
                                    Files    = files,
                                };
                            })
                            .ToList(),
                    })
                    .ToList(),
            })
            .ToList();

        return new OrganizationPlan { Years = years };
    }

    /// <summary>
    /// Computes each file's target filename within a single destination
    /// folder, resolving conflicts: first claim of a name wins as-is; a
    /// later file with the same name gets " (from &lt;parent folder&gt;)"
    /// appended; if that's still taken (same name AND same parent folder
    /// name), falls back to " (from &lt;parent folder&gt;) (2)", "(3)", etc.
    /// </summary>
    private static List<PlannedFile> ResolveFileNames(IEnumerable<ImageRecord> records, string targetFolder)
    {
        var result    = new List<PlannedFile>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            var originalFileName = Path.GetFileName(record.FilePath);
            var candidate         = originalFileName;

            if (!usedNames.Add(candidate))
            {
                var parentFolderName = GetParentFolderName(record.FilePath);
                candidate = WithSuffix(originalFileName, $"(from {parentFolderName})");

                if (!usedNames.Add(candidate))
                {
                    var attempt = 2;
                    string numbered;
                    do
                    {
                        numbered = WithSuffix(originalFileName, $"(from {parentFolderName}) ({attempt})");
                        attempt++;
                    } while (!usedNames.Add(numbered));
                    candidate = numbered;
                }
            }

            result.Add(new PlannedFile
            {
                SourcePath     = record.FilePath,
                TargetFileName = candidate,
                TargetFolder   = targetFolder,
            });
        }

        return result;
    }

    /// <summary>
    /// Hybrid month folder name for the real, on-disk destination path —
    /// e.g. "03 - March". Two-digit zero-padded number sorts correctly in
    /// File Explorer (pure word names sort alphabetically, not
    /// chronologically) regardless of what language the month name itself
    /// is in — the leading "NN - " prefix is what carries the sort order,
    /// so a localized name here doesn't affect chronological sort any
    /// differently than the English default did. The month name keeps it
    /// readable (pure numbers aren't). This is distinct from the TreeView
    /// preview's word-only month label (OrganizationTreeBuilder) — that's
    /// an in-app list where chronological filesystem sort isn't a
    /// concern, so it stays as-is (also localized, via its own monthName
    /// parameter, but independently of this method).
    /// </summary>
    private static string FormatMonthFolder(int month, Func<int, string> monthName) =>
        $"{month:D2} - {monthName(month)}";

    private static string GetParentFolderName(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        return string.IsNullOrEmpty(dir) ? string.Empty : Path.GetFileName(dir);
    }

    private static string WithSuffix(string fileName, string suffix)
    {
        var nameOnly  = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return $"{nameOnly} {suffix}{extension}";
    }
}
