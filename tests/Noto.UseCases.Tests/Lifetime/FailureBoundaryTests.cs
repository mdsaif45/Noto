using System.Runtime.CompilerServices;
using Noto.Core.Diagnostics;
using Noto.Core.Storage;
using Noto.UseCases.Lifetime;
using Xunit;

namespace Noto.UseCases.Tests.Lifetime;

/// <summary>
/// #10: a failure that reaches a last-chance boundary is recorded once, the open note saved once, and the
/// application ended once — whichever boundary sees it first, however many see it, and whatever the save does.
/// </summary>
public sealed class FailureBoundaryTests
{
    private const string Secret = "SECRET-NOTE-TEXT-4711";

    private readonly RecordingAppLog _log = new();
    private int _saves;
    private int _shutdowns;

    [Fact]
    public void The_first_failure_is_recorded_the_note_saved_and_the_application_ended_once_each()
    {
        var boundary = Boundary(() => true);
        Assert.False(boundary.Handled);

        Assert.True(boundary.Fail(UnhandledSource.UiThread, new InvalidOperationException(Secret)));

        var (source, failure) = Assert.Single(_log.Unhandled);
        Assert.Equal(UnhandledSource.UiThread, source);
        Assert.Equal(["System.InvalidOperationException"], failure.Types);
        Assert.Equal([true], _log.Saves);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
        Assert.True(boundary.Handled);
    }

    [Fact]
    public void A_later_failure_from_any_boundary_changes_nothing()
    {
        var boundary = Boundary(() => true);
        boundary.Fail(UnhandledSource.UiThread, new InvalidOperationException());

        Assert.False(boundary.Fail(UnhandledSource.OtherThread, new InvalidOperationException()));
        Assert.False(boundary.Fail(UnhandledSource.UiThread, new TimeoutException()));

        Assert.Single(_log.Unhandled);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void A_save_that_cannot_keep_the_note_is_recorded_and_the_application_still_ends_once()
    {
        var boundary = Boundary(() => false);

        boundary.Fail(UnhandledSource.OtherThread, new InvalidOperationException());

        Assert.Equal([false], _log.Saves);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void A_save_that_throws_is_recorded_never_rethrown_never_retried_and_the_application_still_ends()
    {
        var boundary = Boundary(() => throw new IOException(@"C:\Users\SecretUser\noto.db is locked"));

        var error = Record.Exception(() => boundary.Fail(UnhandledSource.UiThread, new InvalidOperationException()));

        Assert.Null(error);
        Assert.Equal(["System.IO.IOException"], Assert.Single(_log.SaveFailures).Types);
        Assert.Empty(_log.Saves);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void A_failure_raised_while_saving_is_not_handled_again()
    {
        FailureBoundary? boundary = null;
        boundary = Boundary(() =>
        {
            // The save fails in a way that reaches a boundary itself.
            Assert.False(boundary!.Fail(UnhandledSource.UiThread, new InvalidOperationException()));
            return false;
        });

        boundary.Fail(UnhandledSource.OtherThread, new InvalidOperationException());

        Assert.Single(_log.Unhandled);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void A_log_that_throws_while_recording_the_failure_stops_neither_the_save_nor_the_end()
    {
        _log.ThrowOn = nameof(IAppLog.UnhandledException);
        var boundary = Boundary(() => true);

        bool handled = false;
        var error = Record.Exception(() => handled = boundary.Fail(UnhandledSource.UiThread, new InvalidOperationException()));

        Assert.Null(error);
        Assert.True(handled);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
        Assert.Equal([true], _log.Saves);
        Assert.False(boundary.Fail(UnhandledSource.OtherThread, new InvalidOperationException()));
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void A_log_that_throws_while_recording_the_save_is_not_mistaken_for_a_failed_save()
    {
        _log.ThrowOn = nameof(IAppLog.EmergencySave);
        var boundary = Boundary(() => true);

        Assert.Null(Record.Exception(() => boundary.Fail(UnhandledSource.UiThread, new InvalidOperationException())));

        Assert.Empty(_log.SaveFailures);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void A_save_that_throws_and_a_log_that_throws_recording_it_still_end_the_application_once()
    {
        _log.ThrowOn = nameof(IAppLog.EmergencySaveFailed);
        var boundary = Boundary(() => throw new IOException());

        Assert.Null(Record.Exception(() => boundary.Fail(UnhandledSource.OtherThread, new InvalidOperationException())));

        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void Running_out_of_memory_while_saving_is_never_swallowed_and_the_application_still_ends_once()
    {
        // A real one: a string longer than the runtime allows (the type itself may not be constructed, CA2201).
        var boundary = Boundary(() => new string('x', int.MaxValue).Length > 0);

        Assert.Throws<OutOfMemoryException>(() => boundary.Fail(UnhandledSource.UiThread, new InvalidOperationException()));

        Assert.Empty(_log.SaveFailures);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
        Assert.True(boundary.Handled);
    }

    [Fact]
    public void Failures_arriving_together_on_many_threads_are_handled_exactly_once()
    {
        var boundary = Boundary(() => true);
        using var start = new ManualResetEventSlim();

        var threads = Enumerable.Range(0, 16).Select(_ => new Thread(() =>
        {
            start.Wait();
            boundary.Fail(UnhandledSource.OtherThread, new InvalidOperationException());
        })).ToList();

        threads.ForEach(t => t.Start());
        start.Set();
        threads.ForEach(t => t.Join());

        Assert.Single(_log.Unhandled);
        Assert.Equal(1, _saves);
        Assert.Equal(1, _shutdowns);
    }

    [Fact]
    public void What_is_recorded_holds_neither_the_message_nor_the_stack_trace()
    {
        var boundary = Boundary(() => true);
        Exception thrown;

        try
        {
            ThrowFromAFrameNamedLeakyFrame4711();
            return;
        }
        catch (InvalidOperationException ex)
        {
            thrown = new AggregateException(Secret, ex);
        }

        boundary.Fail(UnhandledSource.UiThread, thrown);

        var (_, failure) = Assert.Single(_log.Unhandled);
        string recorded = string.Join('|', failure.Types) + failure.HResult;
        Assert.DoesNotContain(Secret, recorded, StringComparison.Ordinal);
        Assert.DoesNotContain("LeakyFrame4711", recorded, StringComparison.Ordinal);
        Assert.Equal(["System.AggregateException", "System.InvalidOperationException"], failure.Types);
    }

    [Theory]
    [InlineData(StorageFailure.CannotOpen, "could not open")]
    [InlineData(StorageFailure.Corrupt, "damaged")]
    [InlineData(StorageFailure.MigrationFailed, "could not update")]
    [InlineData(StorageFailure.SchemaTooNew, "newer version")]
    [InlineData(StorageFailure.WriteFailed, "could not start")]
    [InlineData(StorageFailure.Unknown, "could not start")]
    public void Each_startup_failure_tells_the_user_what_happened_and_which_file(StorageFailure reason, string says)
    {
        const string database = @"C:\Users\Someone\AppData\Local\Noto\noto.db";

        string message = StartupFailureMessage.For(reason, database);

        Assert.Contains(says, message, StringComparison.Ordinal);
        Assert.EndsWith(database, message, StringComparison.Ordinal);
    }

    private FailureBoundary Boundary(Func<bool> save) => new(_log, () =>
    {
        Interlocked.Increment(ref _saves);
        return save();
    }, () => Interlocked.Increment(ref _shutdowns));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromAFrameNamedLeakyFrame4711() => throw new InvalidOperationException(Secret);

    private sealed class RecordingAppLog : IAppLog
    {
        private readonly object _gate = new();

        /// <summary>The event that throws instead of recording, to stand for a log that fails.</summary>
        public string? ThrowOn { get; set; }

        public List<(UnhandledSource Source, ExceptionSummary Failure)> Unhandled { get; } = [];

        public List<bool> Saves { get; } = [];

        public List<ExceptionSummary> SaveFailures { get; } = [];

        public void StartupFailed(StorageFailure reason, ExceptionSummary failure)
        {
        }

        public void UnhandledException(UnhandledSource source, ExceptionSummary failure)
        {
            ThrowIf(nameof(UnhandledException));

            lock (_gate)
            {
                Unhandled.Add((source, failure));
            }
        }

        public void EmergencySave(bool saved)
        {
            ThrowIf(nameof(EmergencySave));

            lock (_gate)
            {
                Saves.Add(saved);
            }
        }

        public void EmergencySaveFailed(ExceptionSummary failure)
        {
            ThrowIf(nameof(EmergencySaveFailed));

            lock (_gate)
            {
                SaveFailures.Add(failure);
            }
        }

        public void UnobservedTaskException(ExceptionSummary failure)
        {
        }

        public void EventSubscriberFailed(ExceptionSummary failure)
        {
        }

        private void ThrowIf(string name)
        {
            if (ThrowOn == name)
            {
                throw new InvalidOperationException("the log failed");
            }
        }
    }
}
