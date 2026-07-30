using ImageCleanup.Core.IO;

namespace ImageCleanup.Core.Tests.IO;

public class ImageFileEnumeratorTests
{
    private static readonly string[] JpgOnly = [".jpg"];

    [Fact]
    public void EnumerateFiles_FindsFilesAtEveryNestingLevel()
    {
        var root = CreateTempRoot();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "top.jpg"), [1]);
            var sub1 = Directory.CreateDirectory(Path.Combine(root, "sub1")).FullName;
            File.WriteAllBytes(Path.Combine(sub1, "mid.jpg"), [2]);
            var sub2 = Directory.CreateDirectory(Path.Combine(sub1, "sub2")).FullName;
            File.WriteAllBytes(Path.Combine(sub2, "deep.jpg"), [3]);

            var found = ImageFileEnumerator.EnumerateFiles(root, JpgOnly).ToList();

            Assert.Equal(3, found.Count);
            Assert.Contains(Path.Combine(root, "top.jpg"), found);
            Assert.Contains(Path.Combine(sub1, "mid.jpg"), found);
            Assert.Contains(Path.Combine(sub2, "deep.jpg"), found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_FiltersByExtension_CaseInsensitive()
    {
        var root = CreateTempRoot();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "photo.JPG"), [1]);
            File.WriteAllBytes(Path.Combine(root, "notes.txt"), [2]);

            var found = ImageFileEnumerator.EnumerateFiles(root, JpgOnly).ToList();

            Assert.Single(found);
            Assert.Equal(Path.Combine(root, "photo.JPG"), found[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_SkipsHiddenDirectory_ButFindsSiblingFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var visible = Directory.CreateDirectory(Path.Combine(root, "visible")).FullName;
            File.WriteAllBytes(Path.Combine(visible, "a.jpg"), [1]);

            var hidden = Directory.CreateDirectory(Path.Combine(root, "hidden")).FullName;
            File.WriteAllBytes(Path.Combine(hidden, "b.jpg"), [2]);
            new DirectoryInfo(hidden).Attributes |= FileAttributes.Hidden;

            var skipped = new List<string>();
            var found = ImageFileEnumerator.EnumerateFiles(root, JpgOnly, skipped).ToList();

            Assert.Single(found);
            Assert.Equal(Path.Combine(visible, "a.jpg"), found[0]);
            Assert.Contains(hidden, skipped);
        }
        finally
        {
            new DirectoryInfo(Path.Combine(root, "hidden")).Attributes &= ~FileAttributes.Hidden;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_SkipsSystemDirectory_ButFindsSiblingFiles()
    {
        var root = CreateTempRoot();
        try
        {
            var visible = Directory.CreateDirectory(Path.Combine(root, "visible")).FullName;
            File.WriteAllBytes(Path.Combine(visible, "a.jpg"), [1]);

            var systemDir = Directory.CreateDirectory(Path.Combine(root, "systemish")).FullName;
            File.WriteAllBytes(Path.Combine(systemDir, "b.jpg"), [2]);
            new DirectoryInfo(systemDir).Attributes |= FileAttributes.System;

            var skipped = new List<string>();
            var found = ImageFileEnumerator.EnumerateFiles(root, JpgOnly, skipped).ToList();

            Assert.Single(found);
            Assert.Equal(Path.Combine(visible, "a.jpg"), found[0]);
            Assert.Contains(systemDir, skipped);
        }
        finally
        {
            new DirectoryInfo(Path.Combine(root, "systemish")).Attributes &= ~FileAttributes.System;
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_SubdirectoryBecomesInaccessibleMidWalk_SkipsItAndContinues()
    {
        // Simulates a directory disappearing between being discovered and being
        // read (broken junction/symlink, concurrent delete, permission revoked
        // mid-scan) — this must be skipped, not abort the rest of the walk.
        var root = CreateTempRoot();
        try
        {
            // A matching file directly in root gives us a guaranteed pause
            // point after root's own subdirectories have been discovered
            // (and pushed for later processing) but before either has been
            // visited yet.
            File.WriteAllBytes(Path.Combine(root, "root.jpg"), [1]);

            var subA = Directory.CreateDirectory(Path.Combine(root, "subA")).FullName;
            File.WriteAllBytes(Path.Combine(subA, "a.jpg"), [2]);
            var subB = Directory.CreateDirectory(Path.Combine(root, "subB")).FullName;

            var skipped = new List<string>();
            var found = new List<string>();

            using var e = ImageFileEnumerator.EnumerateFiles(root, JpgOnly, skipped).GetEnumerator();

            Assert.True(e.MoveNext());
            Assert.Equal(Path.Combine(root, "root.jpg"), e.Current);

            Directory.Delete(subB);

            while (e.MoveNext())
                found.Add(e.Current);

            Assert.Single(found);
            Assert.Equal(Path.Combine(subA, "a.jpg"), found[0]);
            Assert.Contains(subB, skipped);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_RootDoesNotExist_ReturnsEmptyRatherThanThrowing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}");

        var found = ImageFileEnumerator.EnumerateFiles(missingRoot, JpgOnly).ToList();

        Assert.Empty(found);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ImageFileEnumeratorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
