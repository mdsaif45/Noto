using Noto.Core.Images;
using Noto.Infrastructure.Storage;
using Noto.Infrastructure.Tests.Folders;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>The image files beside the database (ADR-019 §4), on a real folder in the test's own temporary directory.</summary>
public sealed class AttachmentStoreTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0];
    private static readonly byte[] Gif = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61];
    private static readonly byte[] Bmp = [0x42, 0x4D, 0, 0];
    private static readonly byte[] Webp = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];

    private readonly string _root = Directory.CreateTempSubdirectory("noto-attachments-").FullName;
    private readonly AttachmentStore _store;

    public AttachmentStoreTests() => _store = new AttachmentStore(Path.Join(_root, "attachments"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("webp")]
    public void An_image_is_kept_under_a_name_Noto_made_and_found_again(string type)
    {
        byte[] content = type switch { "png" => Png, "jpg" => Jpeg, "gif" => Gif, "bmp" => Bmp, _ => Webp };

        (ImageKeeping outcome, string? reference) = _store.Keep(content, type);

        Assert.Equal(ImageKeeping.Kept, outcome);
        Assert.True(ImageReference.TryFileName(reference, out _));
        Assert.Equal(content, File.ReadAllBytes(_store.PathOf(reference)!));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("gif")]
    [InlineData("webp")]
    public void Bytes_that_are_not_their_type_are_not_kept(string type)
    {
        // A renamed file is not taken for an image: an executable named .png stays outside the note.
        byte[] notImage = [0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0];

        Assert.Equal((ImageKeeping.NotAnImage, (string?)null), _store.Keep(notImage, type));
        Assert.Empty(Directory.Exists(Path.Join(_root, "attachments")) ? Directory.GetFiles(Path.Join(_root, "attachments")) : []);
    }

    [Fact]
    public void A_type_Noto_does_not_keep_is_refused()
    {
        Assert.Equal(ImageKeeping.NotAnImage, _store.Keep(Png, "svg").Outcome);
    }

    [Fact]
    public void An_image_over_the_limit_is_refused_and_one_at_the_limit_kept()
    {
        byte[] large = new byte[ImageReference.MaxBytes + 1];
        Png.CopyTo(large, 0);
        Assert.Equal(ImageKeeping.TooLarge, _store.Keep(large, "png").Outcome);

        byte[] atLimit = new byte[ImageReference.MaxBytes];
        Png.CopyTo(atLimit, 0);
        Assert.Equal(ImageKeeping.Kept, _store.Keep(atLimit, "png").Outcome);
    }

    [Fact]
    public void A_sweep_over_notes_that_cannot_be_read_removes_nothing_and_does_not_throw()
    {
        // Not a database: the sweep cannot know what is referenced, so every file stays.
        string notADatabase = Path.Join(_root, "noto.db");
        File.WriteAllText(notADatabase, "this file is not a database, and is long enough to have a header");
        string? reference = _store.Keep(Png, "png").Reference;
        File.SetLastWriteTimeUtc(_store.PathOf(reference)!, DateTime.UtcNow - AttachmentStore.SweepGrace - TimeSpan.FromDays(1));

        var database = new NotoDatabase(notADatabase);
        try
        {
            Assert.Equal(0, _store.SweepUnreferenced(database, DateTimeOffset.UtcNow));
            Assert.NotNull(_store.PathOf(reference));
        }
        finally
        {
            database.Release();
        }
    }

    [Fact]
    public void A_dropped_file_is_copied_by_its_type_and_its_bytes()
    {
        string photo = Path.Join(_root, "My photo.PNG");
        File.WriteAllBytes(photo, Png);
        string fake = Path.Join(_root, "setup.png");
        File.WriteAllBytes(fake, [0x4D, 0x5A, 0, 0, 0, 0, 0, 0]);

        (ImageKeeping outcome, string? reference) = _store.KeepCopyOf(photo);

        Assert.Equal(ImageKeeping.Kept, outcome);
        Assert.EndsWith(".png", reference, StringComparison.Ordinal);
        Assert.True(File.Exists(photo));
        Assert.Equal(ImageKeeping.NotAnImage, _store.KeepCopyOf(fake).Outcome);
        Assert.Equal(ImageKeeping.NotAnImage, _store.KeepCopyOf(Path.Join(_root, "notes.txt")).Outcome);
        Assert.Equal(ImageKeeping.Failed, _store.KeepCopyOf(Path.Join(_root, "gone.png")).Outcome);
    }

    [Fact]
    public void Only_a_reference_Noto_made_that_is_there_resolves()
    {
        string reference = _store.Keep(Png, "png").Reference!;

        Assert.NotNull(_store.PathOf(reference));
        Assert.Null(_store.PathOf("attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.png"));
        Assert.Null(_store.PathOf("attachments/../../noto.db"));
        Assert.Null(_store.PathOf(null));
    }

    [Fact]
    public void The_sweep_removes_only_old_unreferenced_files_Noto_made()
    {
        string kept = _store.Keep(Png, "png").Reference!;
        string unreferenced = _store.Keep(Png, "png").Reference!;
        string recent = _store.Keep(Png, "png").Reference!;
        string foreign = Path.Join(_root, "attachments", "holiday.png");
        File.WriteAllBytes(foreign, Png);

        DateTime old = DateTime.UtcNow - AttachmentStore.SweepGrace - TimeSpan.FromHours(1);
        foreach (string path in new[] { _store.PathOf(kept)!, _store.PathOf(unreferenced)!, foreign })
        {
            File.SetLastWriteTimeUtc(path, old);
        }

        ImageReference.TryFileName(kept, out string keptName);
        int removed = _store.Sweep(new HashSet<string> { keptName }, DateTimeOffset.UtcNow);

        Assert.Equal(1, removed);
        Assert.NotNull(_store.PathOf(kept));
        Assert.Null(_store.PathOf(unreferenced));
        Assert.NotNull(_store.PathOf(recent));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void Every_note_counts_for_the_sweep_binned_ones_too()
    {
        using var db = new FolderTestContext();
        var folder = db.SeedFolder();
        db.SeedNote(folder, "a\n![x](attachments/01JABCDEFGHJKMNPQRSTVWXYZ0.png)");
        db.SeedNote(folder, "b\n![y](attachments/01JABCDEFGHJKMNPQRSTVWXYZ1.jpg)", deletedAt: db.Clock.UtcNow);
        db.SeedNote(folder, "c\n[not an image](attachments/01JABCDEFGHJKMNPQRSTVWXYZ2.png)");

        Assert.Equal(
            new HashSet<string> { "01JABCDEFGHJKMNPQRSTVWXYZ0.png", "01JABCDEFGHJKMNPQRSTVWXYZ1.jpg" },
            AttachmentStore.ReferencedBy(db.Database));
    }
}
