using ImageCleanup.Core.Hashing;

namespace ImageCleanup.Core.Grouping;

public static class SuggestionEngine
{
    /// <summary>
    /// Groups records into duplicate sets using two passes:
    /// <list type="number">
    ///   <item>Exact duplicates — grouped by identical FileHash (SHA-256).</item>
    ///   <item>Near-duplicates — remaining records whose PerceptualHash values
    ///         are within <paramref name="hammingThreshold"/> bits of each other,
    ///         using union-find to collect connected components.</item>
    /// </list>
    /// Records with a null PerceptualHash participate in exact-hash grouping but
    /// are excluded from near-duplicate grouping.
    /// Each group's <see cref="DuplicateGroup.Suggested"/> is the file to keep:
    /// highest pixel area, then highest BlurScore, then most recent LastModified.
    /// </summary>
    public static List<DuplicateGroup> GroupDuplicates(
        IEnumerable<ImageRecord> records,
        int hammingThreshold = 5)
    {
        var list = records.ToList();
        var groups = new List<DuplicateGroup>();
        var assignedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Phase 1: exact duplicates ─────────────────────────────────────
        foreach (var bucket in list.GroupBy(r => r.FileHash, StringComparer.OrdinalIgnoreCase))
        {
            if (bucket.Count() < 2) continue;

            var files = bucket.ToList();
            foreach (var f in files) assignedPaths.Add(f.FilePath);

            groups.Add(new DuplicateGroup
            {
                Files       = files,
                Suggested   = PickBest(files),
                IsExactMatch = true,
            });
        }

        // ── Phase 2: near-duplicates by perceptual hash ───────────────────
        // Low-detail images (near-uniform colour) are excluded: their DHash
        // values collapse to near-zero regardless of actual content, producing
        // false-positive near-dup groups. Exact-hash grouping above still
        // catches them if they are truly identical byte-for-byte.
        var pool = list
            .Where(r => !assignedPaths.Contains(r.FilePath)
                     && r.PerceptualHash.HasValue
                     && r.LowDetail != true)
            .ToList();

        if (pool.Count < 2)
            return groups;

        // Union-Find with path compression
        var parent = Enumerable.Range(0, pool.Count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }

        void Union(int a, int b)
        {
            int pa = Find(a), pb = Find(b);
            if (pa != pb) parent[pa] = pb;
        }

        for (int i = 0; i < pool.Count; i++)
        for (int j = i + 1; j < pool.Count; j++)
        {
            if (DHasher.HammingDistance(pool[i].PerceptualHash!.Value,
                                        pool[j].PerceptualHash!.Value) <= hammingThreshold)
                Union(i, j);
        }

        foreach (var component in pool
            .Select((r, idx) => (Record: r, Root: Find(idx)))
            .GroupBy(x => x.Root)
            .Where(g => g.Count() >= 2))
        {
            var files = component.Select(x => x.Record).ToList();
            groups.Add(new DuplicateGroup
            {
                Files        = files,
                Suggested    = PickBest(files),
                IsExactMatch = false,
            });
        }

        return groups;
    }

    private static ImageRecord PickBest(List<ImageRecord> files) =>
        files
            .OrderByDescending(f => (long)(f.Width ?? 0) * (f.Height ?? 0))
            .ThenByDescending(f => f.BlurScore ?? 0.0)
            .ThenByDescending(f => f.LastModified)
            .First();
}
