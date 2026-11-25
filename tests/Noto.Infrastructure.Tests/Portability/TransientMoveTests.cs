using Noto.Infrastructure.Portability;
using Xunit;

namespace Noto.Infrastructure.Tests.Portability;

/// <summary>
/// A move another process blocks for a moment is tried again; a real failure still fails (#280). The hold is made the way
/// Windows Defender or the indexer holds a just-written file: a handle that shares nothing, let go a moment later.
/// </summary>
public sealed class TransientMoveTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("noto-transient-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_folder_held_open_for_a_moment_moves_once_it_is_let_go()
    {
        string work = Path.Join(_root, "export.partial");
        string final = Path.Join(_root, "export");
        Directory.CreateDirectory(work);
        string file = Path.Join(work, "note.md");
        await File.WriteAllTextAsync(file, "text");

        using (var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Task letGo = Task.Delay(300).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);
            TransientMove.Run(() => Directory.Move(work, final));
            await letGo;
        }

        Assert.True(File.Exists(Path.Join(final, "note.md")));
        Assert.False(Directory.Exists(work));
    }

    [Fact]
    public void A_missing_source_fails_at_once_without_waiting()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<DirectoryNotFoundException>(() => TransientMove.Run(() => Directory.Move(Path.Join(_root, "none"), Path.Join(_root, "to"))));
        Assert.True(watch.ElapsedMilliseconds < 1000, $"waited {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void A_hold_that_never_ends_is_reported_as_the_move_failed()
    {
        string work = Path.Join(_root, "held.partial");
        Directory.CreateDirectory(work);
        string file = Path.Join(work, "note.md");
        File.WriteAllText(file, "text");

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<Exception>(() => TransientMove.Run(() => Directory.Move(work, Path.Join(_root, "never"))));
        }

        Assert.True(Directory.Exists(work));
    }
}
