using Noto.Core.Diagnostics;
using Xunit;

namespace Noto.Core.Tests.Diagnostics;

/// <summary>F16 (#10): when Noto is failing at every start, and how its failures are recorded.</summary>
public sealed class CrashLoopTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static Failure Early(int minutesAgo) => new(Now.AddMinutes(-minutesAgo), TimeSpan.FromSeconds(5));

    [Fact]
    public void Three_failures_each_soon_after_a_start_within_half_an_hour_are_a_loop()
    {
        Assert.True(CrashLoop.IsLoop([Early(1), Early(3), Early(6)], Now));
        Assert.False(CrashLoop.IsLoop([Early(1), Early(3)], Now));
    }

    [Fact]
    public void A_failure_well_into_a_session_or_long_ago_is_not_part_of_a_loop()
    {
        var late = new Failure(Now.AddMinutes(-2), TimeSpan.FromMinutes(5));
        Assert.False(CrashLoop.IsLoop([Early(1), Early(3), late], Now));
        Assert.False(CrashLoop.IsLoop([Early(1), Early(3), Early(45)], Now));

        // A failure dated after now (the clock moved back) is not counted either.
        Assert.False(CrashLoop.IsLoop([Early(1), Early(3), new Failure(Now.AddMinutes(10), TimeSpan.FromSeconds(1))], Now));
    }

    [Fact]
    public void A_failure_round_trips_through_its_line_and_anything_else_is_skipped()
    {
        Failure failure = new(Now, TimeSpan.FromSeconds(12.5));

        Assert.Equal([failure], CrashLoop.Parse([CrashLoop.Line(failure)]));
        Assert.Equal([failure], CrashLoop.Parse(["", "garbage", "2026-11-11T09:00:00Z -3", "2026-11-11 09:00 5", CrashLoop.Line(failure)]));
    }
}
