namespace ImageCleanup.Core.IO;

/// <summary>
/// Recursively enumerates files under a root directory whose extension is in
/// <c>extensions</c>, skipping hidden/system/reparse-point (junction/symlink)
/// subdirectories and any subdirectory that can't be accessed (permission
/// denied, broken junction/symlink, etc.) rather than aborting the whole scan.
/// </summary>
/// <remarks>
/// Deliberately manual stack-based recursion rather than
/// <c>Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)</c>:
/// that single-call form has no way to skip a specific subdirectory, and one
/// inaccessible folder anywhere in the tree throws and aborts the entire
/// enumeration. Walking directory-by-directory lets each one fail
/// independently, and files stream out (via <c>yield return</c>) as each
/// directory is visited rather than only after the whole tree is walked.
/// </remarks>
public static class ImageFileEnumerator
{
    /// <param name="rootPath">Directory to scan — recursed into but not itself subject to the hidden/system check.</param>
    /// <param name="extensions">File extensions to include, e.g. ".jpg" (case-insensitive).</param>
    /// <param name="skippedPaths">Optional collection to record directories that were skipped (hidden/system/inaccessible).</param>
    public static IEnumerable<string> EnumerateFiles(
        string rootPath,
        IReadOnlyCollection<string> extensions,
        ICollection<string>? skippedPaths = null)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        // Cheap guard against the exact same literal directory path being
        // pushed onto the stack twice (harmless in practice today since each
        // parent only enumerates its own subdirectories once, but costs
        // nothing to have). The real defense against a reparse-point cycle
        // (a junction/symlink resolving back to an ancestor) is skipping
        // FileAttributes.ReparsePoint directories entirely below — this set
        // does NOT resolve junction/symlink targets, so it can't by itself
        // detect "same physical directory, different logical path".
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Guards against the same physical file being yielded twice under two
        // different logical paths (e.g. a file-level symlink/hardlink) —
        // defense in depth alongside the reparse-point directory skip below.
        var yieldedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            var fullDir = Path.GetFullPath(dir);
            if (!visitedDirectories.Add(fullDir))
                continue;

            List<string> files;
            List<string> subdirs;
            try
            {
                files   = Directory.EnumerateFiles(dir).ToList();
                subdirs = Directory.EnumerateDirectories(dir).ToList();
            }
            catch (Exception ex) when (IsAccessError(ex))
            {
                skippedPaths?.Add(dir);
                continue;
            }

            foreach (var file in files)
            {
                if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;
                if (yieldedFiles.Add(Path.GetFullPath(file)))
                    yield return file;
            }

            foreach (var subdir in subdirs)
            {
                if (IsSkippableSubdirectory(subdir, skippedPaths))
                    continue;
                pending.Push(subdir);
            }
        }
    }

    private static bool IsSkippableSubdirectory(string path, ICollection<string>? skippedPaths)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Hidden) != 0
                || (attrs & FileAttributes.System) != 0
                || (attrs & FileAttributes.ReparsePoint) != 0)
            {
                skippedPaths?.Add(path);
                return true;
            }
            return false;
        }
        catch (Exception ex) when (IsAccessError(ex))
        {
            skippedPaths?.Add(path);
            return true;
        }
    }

    private static bool IsAccessError(Exception ex) =>
        ex is UnauthorizedAccessException or IOException or System.Security.SecurityException;
}
