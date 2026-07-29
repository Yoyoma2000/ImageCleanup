using ImageCleanup.Core.Grouping;

namespace ImageCleanup.Core.Tests.Grouping;

public sealed class SuggestionEngineTests
{
    // ── Exact-hash grouping ───────────────────────────────────────────────

    [Fact]
    public void ExactDuplicates_SameHash_FormOneGroup()
    {
        var records = new[]
        {
            Rec("/a.jpg", hash: "H1"),
            Rec("/b.jpg", hash: "H1"),
            Rec("/c.jpg", hash: "H1"),
        };

        var groups = SuggestionEngine.GroupDuplicates(records);

        var g = Assert.Single(groups);
        Assert.True(g.IsExactMatch);
        Assert.Equal(3, g.Files.Count);
    }

    [Fact]
    public void ExactDuplicates_TwoDifferentHashes_TwoGroups()
    {
        var records = new[]
        {
            Rec("/a.jpg", hash: "H1"),
            Rec("/b.jpg", hash: "H1"),
            Rec("/c.jpg", hash: "H2"),
            Rec("/d.jpg", hash: "H2"),
        };

        var groups = SuggestionEngine.GroupDuplicates(records);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.True(g.IsExactMatch));
    }

    [Fact]
    public void ExactDuplicates_UniqueHashes_NoGroups()
    {
        var records = new[]
        {
            Rec("/a.jpg", hash: "H1"),
            Rec("/b.jpg", hash: "H2"),
            Rec("/c.jpg", hash: "H3"),
        };

        Assert.Empty(SuggestionEngine.GroupDuplicates(records));
    }

    // ── Near-duplicate (perceptual hash) grouping ─────────────────────────

    [Fact]
    public void NearDups_WithinThreshold_FormOneGroup()
    {
        // Hamming distance 0UL↔1UL = 1, 0UL↔3UL = 2 — all ≤ 5
        var records = new[]
        {
            Rec("/a.jpg", hash: "H1", phash: 0UL),
            Rec("/b.jpg", hash: "H2", phash: 1UL),  // dist 1 from /a
            Rec("/c.jpg", hash: "H3", phash: 3UL),  // dist 2 from /a, 1 from /b
        };

        var groups = SuggestionEngine.GroupDuplicates(records, hammingThreshold: 5);

        var g = Assert.Single(groups);
        Assert.False(g.IsExactMatch);
        Assert.Equal(3, g.Files.Count);
    }

    [Fact]
    public void NearDups_BeyondThreshold_NoGroup()
    {
        // 0UL vs (1UL<<6)-1 = 0b111111 = 6 bits set → dist 6 > threshold 5
        ulong farHash = (1UL << 6) - 1; // 0b111111 — Hamming distance 6 from 0UL

        var records = new[]
        {
            Rec("/a.jpg", hash: "H1", phash: 0UL),
            Rec("/b.jpg", hash: "H2", phash: farHash),
        };

        Assert.Empty(SuggestionEngine.GroupDuplicates(records, hammingThreshold: 5));
    }

    [Fact]
    public void NearDups_ExactlyAtThreshold_AreGrouped()
    {
        ulong fiveBits = (1UL << 5) - 1; // 0b11111 — Hamming distance 5 from 0UL

        var records = new[]
        {
            Rec("/a.jpg", hash: "H1", phash: 0UL),
            Rec("/b.jpg", hash: "H2", phash: fiveBits),
        };

        var groups = SuggestionEngine.GroupDuplicates(records, hammingThreshold: 5);

        Assert.Single(groups);
    }

    [Fact]
    public void NearDups_NullPerceptualHash_ExcludedFromNearDupPhase()
    {
        // /a and /b have the same exact hash → exact group
        // /c has no perceptual hash → excluded from near-dup phase
        var records = new[]
        {
            Rec("/a.jpg", hash: "H1", phash: 0UL),
            Rec("/b.jpg", hash: "H1", phash: 0UL),   // exact dup of /a
            Rec("/c.jpg", hash: "H2", phash: null),   // no perceptual hash
            Rec("/d.jpg", hash: "H3", phash: 0UL),   // near-dup candidate alone — no group
        };

        var groups = SuggestionEngine.GroupDuplicates(records, hammingThreshold: 5);

        // Only the exact-dup group for H1
        var g = Assert.Single(groups);
        Assert.True(g.IsExactMatch);
        Assert.All(g.Files, f => Assert.Contains(f.FilePath, new[] { "/a.jpg", "/b.jpg" }));
    }

    // ── Suggested selection — tiebreaker order ────────────────────────────

    [Fact]
    public void Suggested_HighestResolutionWins()
    {
        var records = new[]
        {
            Rec("/small.jpg",  hash: "H1", width: 800,  height: 600),
            Rec("/large.jpg",  hash: "H1", width: 1920, height: 1080),
            Rec("/medium.jpg", hash: "H1", width: 1280, height: 720),
        };

        var g = Assert.Single(SuggestionEngine.GroupDuplicates(records));
        Assert.Equal("/large.jpg", g.Suggested.FilePath);
    }

    [Fact]
    public void Suggested_SameResolution_HighestBlurScoreWins()
    {
        var records = new[]
        {
            Rec("/blurry.jpg", hash: "H1", width: 1920, height: 1080, blur: 10.0),
            Rec("/sharp.jpg",  hash: "H1", width: 1920, height: 1080, blur: 80.0),
            Rec("/medium.jpg", hash: "H1", width: 1920, height: 1080, blur: 45.0),
        };

        var g = Assert.Single(SuggestionEngine.GroupDuplicates(records));
        Assert.Equal("/sharp.jpg", g.Suggested.FilePath);
    }

    [Fact]
    public void Suggested_SameResolutionAndBlur_MostRecentModifiedWins()
    {
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var records = new[]
        {
            Rec("/old.jpg",    hash: "H1", lastModified: t0),
            Rec("/newer.jpg",  hash: "H1", lastModified: t0.AddDays(10)),
            Rec("/newest.jpg", hash: "H1", lastModified: t0.AddDays(20)),
        };

        var g = Assert.Single(SuggestionEngine.GroupDuplicates(records));
        Assert.Equal("/newest.jpg", g.Suggested.FilePath);
    }

    [Fact]
    public void Suggested_NullDimensions_TreatedAsZeroArea()
    {
        var records = new[]
        {
            Rec("/no-dims.jpg", hash: "H1", width: null, height: null),
            Rec("/has-dims.jpg", hash: "H1", width: 100, height: 100),
        };

        var g = Assert.Single(SuggestionEngine.GroupDuplicates(records));
        Assert.Equal("/has-dims.jpg", g.Suggested.FilePath);
    }

    // ── Single-file records produce no groups ─────────────────────────────

    [Fact]
    public void SingleRecord_ProducesNoGroups()
    {
        Assert.Empty(SuggestionEngine.GroupDuplicates([Rec("/only.jpg", "H1")]));
    }

    [Fact]
    public void EmptyInput_ProducesNoGroups()
    {
        Assert.Empty(SuggestionEngine.GroupDuplicates([]));
    }

    // ── Exact-dup records are excluded from near-dup phase ────────────────

    [Fact]
    public void ExactDupRecordsNotDoubleCountedInNearDupPhase()
    {
        // /a and /b are exact dups. /c is near-dup of /a by phash but should
        // NOT pull /a or /b into a near-dup group because they're already assigned.
        var records = new[]
        {
            Rec("/a.jpg", hash: "H1", phash: 0UL),
            Rec("/b.jpg", hash: "H1", phash: 0UL),  // exact dup of /a
            Rec("/c.jpg", hash: "H2", phash: 1UL),  // 1-bit away from /a — near-dup candidate
            Rec("/d.jpg", hash: "H3", phash: 1UL),  // same phash as /c → forms near-dup group
        };

        var groups = SuggestionEngine.GroupDuplicates(records, hammingThreshold: 5);

        Assert.Equal(2, groups.Count);
        Assert.Single(groups, g => g.IsExactMatch);
        Assert.Single(groups, g => !g.IsExactMatch && g.Files.All(f => f.FilePath is "/c.jpg" or "/d.jpg"));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static ImageRecord Rec(
        string path,
        string hash            = "DEFAULT",
        ulong? phash           = null,
        int?   width           = null,
        int?   height          = null,
        double? blur           = null,
        DateTime? lastModified = null) => new()
    {
        FilePath       = path,
        FileHash       = hash,
        PerceptualHash = phash,
        Width          = width,
        Height         = height,
        BlurScore      = blur,
        LastModified   = lastModified ?? DateTime.UtcNow,
    };
}
