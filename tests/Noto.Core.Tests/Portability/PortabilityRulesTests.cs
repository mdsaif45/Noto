using Noto.Core.Portability;
using Xunit;

namespace Noto.Core.Tests.Portability;

/// <summary>The pure rules of ADR-020: export names, the backup manifest's checks, image sources and the export index.</summary>
public sealed class PortabilityRulesTests
{
    private static readonly string Hash = new('a', 64);

    [Theory]
    [InlineData("Weekly sync", "Weekly sync")]
    [InlineData("a<b>c:d\"e/f\\g|h?i*j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("tab\there", "tab_here")]
    [InlineData("  padded  ", "padded")]
    [InlineData("ends with dots...", "ends with dots")]
    [InlineData("CON", "CON_")]
    [InlineData("com1.txt", "com1.txt_")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("", PortableNames.Untitled)]
    [InlineData("...", PortableNames.Untitled)]
    [InlineData(null, PortableNames.Untitled)]
    [InlineData("日本語 ✓ Hôme", "日本語 ✓ Hôme")]
    public void A_name_is_made_safe_for_Windows(string? name, string safe)
    {
        Assert.Equal(safe, PortableNames.Safe(name));
    }

    [Fact]
    public void A_long_name_is_cut_never_inside_a_surrogate_pair()
    {
        Assert.Equal(PortableNames.MaxLength, PortableNames.Safe(new string('x', 200)).Length);

        string emoji = new string('x', PortableNames.MaxLength - 1) + "🎉tail";
        string cut = PortableNames.Safe(emoji);
        Assert.Equal(PortableNames.MaxLength - 1, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
    }

    [Fact]
    public void Names_are_unique_within_a_directory_ignoring_case_in_the_order_asked()
    {
        var names = new NameAllocator(["Taken"]);

        Assert.Equal("Notes.md", names.Take("Notes", ".md"));
        Assert.Equal("notes (2).md", names.Take("notes", ".md"));
        Assert.Equal("Notes (3).md", names.Take("Notes", ".md"));
        Assert.Equal("Taken (2)", names.Take("Taken"));
        Assert.Equal("Other", names.Take("Other"));
    }

    [Fact]
    public void A_manifest_round_trips()
    {
        BackupManifest manifest = Manifest();

        (BackupManifest? read, BackupProblem? problem) = BackupManifest.Read(manifest.ToJson(), 4);

        Assert.Null(problem);
        Assert.Equal(manifest.Counts, read!.Counts);
        Assert.Equal(manifest.Entries, read.Entries);
        Assert.Equal(manifest.CreatedAt, read.CreatedAt);
    }

    [Theory]
    [InlineData("{ not json", BackupProblem.NotABackup)]
    [InlineData("null", BackupProblem.NotABackup)]
    [InlineData("{}", BackupProblem.NotABackup)]
    public void Text_that_is_not_a_manifest_is_not_a_backup(string json, BackupProblem problem)
    {
        Assert.Equal((null, problem), BackupManifest.Read(json, 4));
    }

    [Fact]
    public void A_manifest_is_refused_by_kind()
    {
        Assert.Equal(BackupProblem.NotABackup, Problem(Manifest() with { Format = "other" }));
        Assert.Equal(BackupProblem.NotABackup, Problem(Manifest() with { FormatVersion = 0 }));
        Assert.Equal(BackupProblem.NewerFormat, Problem(Manifest() with { FormatVersion = BackupManifest.CurrentFormatVersion + 1 }));
        Assert.Equal(BackupProblem.NewerSchema, Problem(Manifest() with { SchemaVersion = 5 }));
        Assert.Null(Problem(Manifest() with { SchemaVersion = 3 }));
        Assert.Equal(BackupProblem.Incomplete, Problem(Manifest() with { Entries = [new("attachments/01J00000000000000000000A01.png", 1, Hash)] }));
    }

    [Theory]
    [InlineData("../noto.db")]
    [InlineData("/noto.db")]
    [InlineData("C:/noto.db")]
    [InlineData("attachments/../noto.db")]
    [InlineData("attachments/evil.exe")]
    [InlineData("attachments/01J00000000000000000000A01.svg")]
    [InlineData("other.db")]
    [InlineData("")]
    public void An_entry_outside_the_backups_own_paths_is_refused(string path)
    {
        Assert.False(BackupManifest.IsEntryPath(path));
        Assert.Equal(BackupProblem.Corrupted, Problem(Manifest() with { Entries = [new(BackupManifest.DatabaseEntry, 1, Hash), new(path, 1, Hash)] }));
    }

    [Fact]
    public void An_entry_listed_twice_or_without_a_proper_hash_or_size_is_refused()
    {
        Assert.Equal(BackupProblem.Corrupted, Problem(Manifest() with { Entries = [new(BackupManifest.DatabaseEntry, 1, Hash), new(BackupManifest.DatabaseEntry, 1, Hash)] }));
        Assert.Equal(BackupProblem.Corrupted, Problem(Manifest() with { Entries = [new(BackupManifest.DatabaseEntry, 1, Hash.ToUpperInvariant())] }));
        Assert.Equal(BackupProblem.Corrupted, Problem(Manifest() with { Entries = [new(BackupManifest.DatabaseEntry, 1, Hash[1..])] }));
        Assert.Equal(BackupProblem.Corrupted, Problem(Manifest() with { Entries = [new(BackupManifest.DatabaseEntry, -1, Hash)] }));
        Assert.Null(Problem(Manifest() with { Entries = [new(BackupManifest.DatabaseEntry, 0, Hash)] }));
    }

    [Fact]
    public void An_images_source_is_its_own_not_a_link_in_its_name()
    {
        // A link may sit in an image's name (CommonMark); the image's source is the last destination inside it.
        Assert.Equal(["a.png"], ImageSources.In("![see [x](y.png)](a.png)").Select(s => s.Source));
    }

    [Fact]
    public void Image_sources_are_found_and_only_those_mapped_are_rewritten()
    {
        const string text = "a ![one](img/a.png) b\r\n![two](attachments/01J00000000000000000000A01.png)\n[link](img/a.png) ![three](https://x.y/z.png)";

        Assert.Equal(["img/a.png", "attachments/01J00000000000000000000A01.png", "https://x.y/z.png"], ImageSources.In(text).Select(s => s.Source));
        Assert.Equal(
            "a ![one](attachments/NEW.png) b\r\n![two](attachments/01J00000000000000000000A01.png)\n[link](img/a.png) ![three](https://x.y/z.png)",
            ImageSources.Rewrite(text, s => s == "img/a.png" ? "attachments/NEW.png" : null));
        Assert.Equal("no images\r\n", ImageSources.Rewrite("no images\r\n", _ => "x"));
    }

    [Fact]
    public void An_export_index_is_read_only_when_it_is_one_this_build_knows()
    {
        var index = new ExportIndex { ExportedAt = DateTimeOffset.UnixEpoch, Folders = [new ExportFolder("Work", "Work", true, null, [new ExportNote("a.md", false, true, null)])] };

        Assert.Equal("a.md", ExportIndex.Read(index.ToJson())!.Folders[0].Notes[0].File);
        Assert.Null(ExportIndex.Read((index with { Version = ExportIndex.CurrentVersion + 1 }).ToJson()));
        Assert.Null(ExportIndex.Read((index with { Format = "other" }).ToJson()));
        Assert.Null(ExportIndex.Read("{ broken"));
        Assert.Null(ExportIndex.Read("""{"format":"noto-export","version":1,"exportedAt":"2026-01-01T00:00:00Z","folders":[{"directory":"W","name":"W","pinned":false,"notes":[{"pinned":false}]}]}"""));
    }

    private static BackupManifest Manifest() => new()
    {
        Format = BackupManifest.FormatName,
        FormatVersion = BackupManifest.CurrentFormatVersion,
        SchemaVersion = 4,
        CreatedAt = new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.Zero),
        AppVersion = "test",
        Counts = new BackupCounts(1, 2, 0, 1),
        Entries = [new(BackupManifest.DatabaseEntry, 4096, Hash), new("attachments/01J00000000000000000000A01.png", 10, Hash)],
    };

    private static BackupProblem? Problem(BackupManifest manifest) => BackupManifest.Read(manifest.ToJson(), 4).Problem;
}
