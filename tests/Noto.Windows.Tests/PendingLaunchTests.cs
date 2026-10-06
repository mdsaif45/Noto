using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// At most one launch waiting for the UI thread (A17): bounded, and nothing lost.
/// </summary>
public sealed class PendingLaunchTests
{
    [Fact]
    public void The_first_request_asks_for_a_run_and_later_ones_join_it()
    {
        var pending = new PendingLaunch();

        Assert.True(pending.Offer(100));
        Assert.False(pending.Offer(200));
        Assert.False(pending.Offer(300));
    }

    [Fact]
    public void The_run_takes_the_newest_request()
    {
        var pending = new PendingLaunch();
        _ = pending.Offer(100);
        _ = pending.Offer(300);

        Assert.Equal(300, pending.Take());
    }

    [Fact]
    public void A_request_after_the_run_asks_for_another()
    {
        var pending = new PendingLaunch();
        _ = pending.Offer(100);
        _ = pending.Take();

        Assert.True(pending.Offer(200));
        Assert.Equal(200, pending.Take());
    }

    [Fact]
    public void Ten_thousand_requests_while_the_ui_is_busy_ask_for_one_run()
    {
        var pending = new PendingLaunch();

        int runs = Enumerable.Range(0, 10_000).Count(pending.Offer);

        Assert.Equal(1, runs);
        Assert.Equal(9_999, pending.Take());
    }

    [Fact]
    public async Task Concurrent_requests_are_never_lost()
    {
        // Producers stand in for the pipe; the consumer for the UI thread,
        // running once per scheduled run. Every producer's last request must
        // be seen by a run that started after it was offered.
        var pending = new PendingLaunch();
        int scheduled = 0;
        int taken = 0;
        using var runs = new SemaphoreSlim(0);
        int lastTaken = -1;

        Task consumer = Task.Run(async () =>
        {
            while (await runs.WaitAsync(TimeSpan.FromSeconds(2)))
            {
                lastTaken = pending.Take();
                _ = Interlocked.Increment(ref taken);
            }
        });

        Parallel.For(0, 8, producer =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                if (pending.Offer((producer * 100_000) + i))
                {
                    _ = Interlocked.Increment(ref scheduled);
                    _ = runs.Release();
                }
            }
        });

        // The final request: offered after every other, so it must be taken.
        if (pending.Offer(int.MaxValue))
        {
            _ = Interlocked.Increment(ref scheduled);
            _ = runs.Release();
        }

        await consumer;

        Assert.Equal(scheduled, taken);
        Assert.Equal(int.MaxValue, lastTaken);
        Assert.True(scheduled <= (8 * 5_000) + 1);
    }
}
