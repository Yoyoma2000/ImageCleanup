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
    public static OrganizationPlan BuildHierarchy(IEnumerable<ImageRecord> records)
    {
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
                                var targetFolder = $"{yearGroup.Key:D4}/{monthGroup.Key:D2}/{categoryGroup.Key}";
                                var files = ResolveFileNames(
                                    categoryGroup.Select(x => x.Record),
                                    targetFolder);
                                return new CategoryGroup
                                {
                                    Category = categoryGroup.Key,
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
