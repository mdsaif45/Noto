using Noto.Core.Diagnostics;
using Noto.Infrastructure.Diagnostics;
using Xunit;

namespace Noto.Infrastructure.Tests.Diagnostics;

/// <summary>F16 (#10): the record of recent failures, on disk. Best effort: it never stops a start or an end.</summary>
public sealed class FailureHistoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("noto-failures-").FullName;
    private readonly DateTimeOffset _now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private string File_ => Path.Join(_directory, "failures.txt");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void No_record_is_no_failures()
    {
        Assert.Empty(FailureHistory.Read(File_));
    }

    [Fact]
    public void Failures_are_appended_and_only_the_newest_are_kept()
    {
        for (int i = 0; i < CrashLoop.Kept + 3; i++)
        {
            FailureHistory.Append(File_, new Failure(_now.AddMinutes(i), TimeSpan.FromSeconds(i)));
        }

        IReadOnlyList<Failure> kept = FailureHistory.Read(File_);

        Assert.Equal(CrashLoop.Kept, kept.Count);
        Assert.Equal(_now.AddMinutes(3), kept[0].At);
        Assert.Equal(_now.AddMinutes(CrashLoop.Kept + 2), kept[^1].At);
    }

    [Fact]
    public void Clearing_forgets_every_failure_and_a_damaged_record_reads_what_it_can()
    {
        File.WriteAllLines(File_, ["not a failure", CrashLoop.Line(new Failure(_now, TimeSpan.FromSeconds(4)))]);
        Assert.Single(FailureHistory.Read(File_));

        FailureHistory.Clear(File_);

        Assert.False(File.Exists(File_));
        Assert.Empty(FailureHistory.Read(File_));
    }

    [Fact]
    public void A_record_held_open_elsewhere_is_read_as_none_and_never_throws()
    {
        File.WriteAllText(File_, CrashLoop.Line(new Failure(_now, TimeSpan.FromSeconds(4))));

        using (new FileStream(File_, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Empty(FailureHistory.Read(File_));
            FailureHistory.Append(File_, new Failure(_now, TimeSpan.FromSeconds(1)));
            FailureHistory.Clear(File_);
        }
    }
}
