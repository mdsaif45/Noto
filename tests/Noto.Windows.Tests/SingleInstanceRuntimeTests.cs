using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Noto.Platform.Windows;
using Xunit;
using Xunit.Abstractions;

namespace Noto.Windows.Tests;

/// <summary>
/// Ownership, the activation pipe and the claim (A17, ADR-013), against real
/// kernel objects: named mutexes and named pipes.
/// </summary>
/// <remarks>
/// <para>
/// Every test uses a key of its own — the real user's SID and a made-up
/// root — so nothing here can meet a running Noto or a real data root. No
/// interactive desktop is needed.
/// </para>
/// <para>
/// A mutex belongs to a thread, so "another process holds the root" is a
/// <see cref="Holder"/> thread here. The validation harness repeats the
/// important cases with real processes, a killed one included.
/// </para>
/// </remarks>
public sealed class SingleInstanceRuntimeTests(ITestOutputHelper output)
{
    private static readonly ClaimTimings Quick = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(50));

    private static InstanceKey NewKey() =>
        InstanceKey.From(ProcessIdentity.CurrentUserSid(), $@"C:\noto-instance-tests\{Guid.NewGuid():N}");

    // ------------------------------------------------------------ ownership

    [Fact]
    public void A_free_root_is_owned_by_the_first_launch()
    {
        InstanceKey key = NewKey();

        Assert.False(InstanceOwnership.IsHeld(key));

        using InstanceOwnership? owned = InstanceOwnership.TryAcquire(key);

        Assert.NotNull(owned);
        Assert.False(owned.Recovered);
        Assert.True(InstanceOwnership.IsHeld(key));
    }

    [Fact]
    public void A_held_root_cannot_be_owned_by_anyone_else()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);

        Assert.True(holder.Owned);
        Assert.Null(OnOtherThread(() => InstanceOwnership.TryAcquire(key)));
        Assert.True(InstanceOwnership.IsHeld(key));
    }

    [Fact]
    public void Released_ownership_can_be_taken_at_once()
    {
        InstanceKey key = NewKey();
        using (new Holder(key))
        {
        }

        using InstanceOwnership? next = InstanceOwnership.TryAcquire(key);

        Assert.NotNull(next);
        Assert.False(next.Recovered);
    }

    [Fact]
    public void Letting_go_frees_the_root_at_once_while_the_owner_is_still_running()
    {
        // The owner closes its window and lets go, but its process (here, its
        // thread) lives on for a while. The next launch must own straight
        // away — not wait for the old process to end and find it abandoned.
        // A waiting launch holds a handle of its own, which keeps the mutex
        // alive across the owner's dispose; the observer stands in for it.
        InstanceKey key = NewKey();
        using var acquired = new ManualResetEventSlim();
        using var letGo = new ManualResetEventSlim();
        using var released = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            InstanceOwnership? owned = InstanceOwnership.TryAcquire(key);
            acquired.Set();
            letGo.Wait();
            owned?.Dispose();
            released.Set();
            finish.Wait();
        });
        owner.Start();
        acquired.Wait();
        using var observer = Mutex.OpenExisting(key.MutexName);
        letGo.Set();
        released.Wait();

        try
        {
            using InstanceOwnership? next = InstanceOwnership.TryAcquire(key);

            Assert.NotNull(next);
            Assert.False(next.Recovered);
        }
        finally
        {
            finish.Set();
            owner.Join();
        }
    }

    [Fact]
    public void An_owner_that_dies_holding_the_root_leaves_it_recoverable_not_locked()
    {
        InstanceKey key = NewKey();
        using (new Holder(key, abandon: true))
        {
        }

        using InstanceOwnership? next = InstanceOwnership.TryAcquire(key);

        Assert.NotNull(next);
        Assert.True(next.Recovered);
    }

    [Fact]
    public void Simultaneous_launches_produce_exactly_one_owner()
    {
        for (int round = 0; round < 20; round++)
        {
            InstanceKey key = NewKey();
            const int Launches = 16;
            using var start = new Barrier(Launches);
            using var done = new CountdownEvent(Launches);
            using var finish = new ManualResetEventSlim();
            int owners = 0;

            var threads = Enumerable.Range(0, Launches).Select(_ => new Thread(() =>
            {
                start.SignalAndWait();
                using InstanceOwnership? owned = InstanceOwnership.TryAcquire(key);

                if (owned is not null)
                {
                    _ = Interlocked.Increment(ref owners);
                }

                done.Signal();
                finish.Wait();
            })).ToList();

            threads.ForEach(t => t.Start());
            done.Wait();
            finish.Set();
            threads.ForEach(t => t.Join());

            Assert.Equal(1, owners);
        }
    }

    [Fact]
    public void A_name_taken_by_another_kind_of_object_is_refused_not_ignored()
    {
        InstanceKey key = NewKey();
        using var squatter = new EventWaitHandle(false, EventResetMode.ManualReset, key.MutexName);

        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, Assert.Null);

        Assert.Equal(ClaimOutcome.Refused, claim.Outcome);
        Assert.Equal(ClaimRefusal.Denied, claim.Refusal);
    }

    // ---------------------------------------------------------------- claim

    [Fact]
    public void A_launch_with_no_owner_becomes_the_owner()
    {
        InstanceKey key = NewKey();

        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, owned => Assert.NotNull(owned));

        Assert.Equal(ClaimOutcome.Owner, claim.Outcome);
        Assert.False(claim.TookOver);
    }

    [Fact]
    public void A_second_launch_hands_off_and_does_not_own()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);
        var received = new List<ActivationRequest>();
        using ActivationPipeServer server = StartServer(key, r => { lock (received) { received.Add(r); } return ActivationReply.Accepted; });

        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, Assert.Null);

        Assert.Equal(ClaimOutcome.HandedOff, claim.Outcome);
        Assert.Equal([ActivationRequest.Activate], received);
        Assert.True(InstanceOwnership.IsHeld(key));
    }

    [Fact]
    public void A_rejected_handoff_is_refused()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);
        using ActivationPipeServer server = StartServer(key, request => ActivationReply.Rejected);

        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, Assert.Null);

        Assert.Equal((ClaimOutcome.Refused, ClaimRefusal.Rejected), (claim.Outcome, claim.Refusal));
    }

    [Fact]
    public async Task A_launch_during_the_owners_shutdown_takes_over_once_it_has_gone()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);
        using ActivationPipeServer server = StartServer(key, request => ActivationReply.Accepted);
        server.BeginShutdown();

        // The closing owner lets go 300 ms later: pipe first, then the mutex.
        Task closing = Task.Run(async () =>
        {
            await Task.Delay(300);
            server.Dispose();
            holder.Dispose();
        });

        var clock = Stopwatch.StartNew();
        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, owned =>
        {
            clock.Stop();
            Assert.NotNull(owned);

            // The successor can serve the next launch: the name came free.
            using ActivationPipeServer successor = StartServer(key, request => ActivationReply.Accepted);
        });
        await closing;

        output.WriteLine($"took over after {clock.ElapsedMilliseconds} ms");
        Assert.Equal(ClaimOutcome.Owner, claim.Outcome);
        Assert.True(claim.TookOver);
    }

    [Fact]
    public void A_closing_owner_that_never_lets_go_is_unreachable_not_waited_on_forever()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);
        using ActivationPipeServer server = StartServer(key, request => ActivationReply.Accepted);
        server.BeginShutdown();

        var clock = Stopwatch.StartNew();
        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, Assert.Null);

        output.WriteLine($"gave up after {clock.ElapsedMilliseconds} ms (takeover limit {Quick.Takeover.TotalMilliseconds} ms)");
        Assert.Equal(ClaimOutcome.Unreachable, claim.Outcome);
        Assert.InRange(clock.Elapsed, Quick.Takeover, Quick.Takeover + TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void An_owner_with_no_pipe_is_unreachable_within_the_limit()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);

        var clock = Stopwatch.StartNew();
        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, Assert.Null);

        output.WriteLine($"gave up after {clock.ElapsedMilliseconds} ms (limit {Quick.Unreachable.TotalMilliseconds} ms)");
        Assert.Equal(ClaimOutcome.Unreachable, claim.Outcome);
        Assert.InRange(clock.Elapsed, Quick.Unreachable, Quick.Unreachable + TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void The_real_unreachable_limit_is_about_five_seconds()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);

        var clock = Stopwatch.StartNew();
        InstanceClaim claim = ClaimWhile(key, elevated: false, ClaimTimings.Default, Assert.Null);

        output.WriteLine($"gave up after {clock.ElapsedMilliseconds} ms");
        Assert.Equal(ClaimOutcome.Unreachable, claim.Outcome);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task An_owner_that_dies_while_a_launch_waits_is_replaced_by_that_launch()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);
        Task dying = Task.Run(async () =>
        {
            await Task.Delay(300);
            holder.Dispose();
        });

        InstanceClaim claim = ClaimWhile(key, elevated: false, Quick, owned => Assert.NotNull(owned));
        await dying;

        Assert.Equal(ClaimOutcome.Owner, claim.Outcome);
    }

    // ------------------------------------------------------------ elevation

    [Fact]
    public void An_elevated_launch_with_no_owner_is_refused_and_takes_nothing()
    {
        InstanceKey key = NewKey();

        InstanceClaim claim = ClaimWhile(key, elevated: true, Quick, Assert.Null);

        Assert.Equal((ClaimOutcome.Refused, ClaimRefusal.Elevated), (claim.Outcome, claim.Refusal));
        Assert.False(InstanceOwnership.IsHeld(key));
    }

    [Fact]
    public void An_elevated_launch_hands_off_to_a_running_owner()
    {
        InstanceKey key = NewKey();
        using var holder = new Holder(key);
        using ActivationPipeServer server = StartServer(key, request => ActivationReply.Accepted);

        InstanceClaim claim = ClaimWhile(key, elevated: true, Quick, Assert.Null);

        Assert.Equal(ClaimOutcome.HandedOff, claim.Outcome);
    }

    [Fact]
    public void An_elevated_launch_never_takes_over_from_a_closing_owner()
    {
        InstanceKey key = NewKey();

        using (var holder = new Holder(key))
        using (ActivationPipeServer server = StartServer(key, request => ActivationReply.Accepted))
        {
            server.BeginShutdown();

            InstanceClaim claim = ClaimWhile(key, elevated: true, Quick, Assert.Null);

            Assert.Equal((ClaimOutcome.Refused, ClaimRefusal.Elevated), (claim.Outcome, claim.Refusal));
        }

        Assert.False(InstanceOwnership.IsHeld(key));
    }

    // ----------------------------------------------------------------- pipe

    [Fact]
    public void A_second_server_for_the_same_root_is_refused_the_name()
    {
        InstanceKey key = NewKey();
        using ActivationPipeServer first = StartServer(key, request => ActivationReply.Accepted);

        PipeStartStatus status = ActivationPipeServer.Start(key, request => ActivationReply.Accepted, out ActivationPipeServer? second, out int error);

        using (second)
        {
            output.WriteLine($"second start: {status} ({error})");
            Assert.Equal(PipeStartStatus.NameTaken, status);
            Assert.Null(second);
        }
    }

    [Fact]
    public void A_squatter_holding_the_name_is_detected_and_not_joined()
    {
        InstanceKey key = NewKey();

        // Another process created the name first, allowing any number of
        // instances. Joining it would split launches between the two.
        using var squatter = new NamedPipeServerStream(
            key.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);

        PipeStartStatus status = ActivationPipeServer.Start(key, request => ActivationReply.Accepted, out ActivationPipeServer? server, out int error);

        using (server)
        {
            output.WriteLine($"start beside a squatter: {status} ({error})");
            Assert.Equal(PipeStartStatus.NameTaken, status);
            Assert.Null(server);
        }
    }

    [Fact]
    public void Closing_the_server_frees_the_name_for_a_successor()
    {
        InstanceKey key = NewKey();

        using (StartServer(key, request => ActivationReply.Accepted))
        {
        }

        using ActivationPipeServer successor = StartServer(key, request => ActivationReply.Accepted);

        Assert.Equal(HandoffStatus.Accepted, ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void A_closed_server_answers_no_one()
    {
        InstanceKey key = NewKey();

        using (StartServer(key, request => ActivationReply.Accepted))
        {
        }

        Assert.Equal(HandoffStatus.Unavailable, ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromMilliseconds(300)));
    }

    public static TheoryData<string, byte[]> Malformed => new()
    {
        { "empty-ish", [0] },
        { "truncated", "NOTO\u0001"u8.ToArray() },
        { "bad magic", ActivationEnvelope.Frame(1, 1, []).Select((b, i) => i == 0 ? (byte)'X' : b).ToArray() },
        { "version 2", ActivationEnvelope.Frame(2, 1, []) },
        { "version 0", ActivationEnvelope.Frame(0, 1, []) },
        { "unknown kind 9", ActivationEnvelope.Frame(1, 9, []) },
        { "reserved Uri", ActivationEnvelope.Frame(1, 2, "noto://x"u8) },
        { "reserved Cli", ActivationEnvelope.Frame(1, 3, "--x"u8) },
        { "length too long", ActivationEnvelope.Frame(1, 1, [], declaredLength: 10) },
        { "trailing bytes", ActivationEnvelope.Frame(1, 1, "abc"u8, declaredLength: 0) },
        { "activate with payload", ActivationEnvelope.Frame(1, 1, "x"u8) },
        { "8 KiB exactly", ActivationEnvelope.Frame(1, 1, new byte[ActivationEnvelope.MaxSize - ActivationEnvelope.HeaderSize]) },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void A_malformed_request_is_rejected_and_the_owner_stays_healthy(string name, byte[] message)
    {
        InstanceKey key = NewKey();
        int handled = 0;
        using ActivationPipeServer server = StartServer(key, request => { _ = Interlocked.Increment(ref handled); return ActivationReply.Accepted; });

        HandoffStatus status = ActivationPipeClient.SendRaw(key.PipeName, message, TimeSpan.FromSeconds(2), key.UserSid);

        output.WriteLine($"{name}: {status}");
        Assert.Equal(HandoffStatus.Rejected, status);
        Assert.Equal(0, handled);

        Assert.Equal(HandoffStatus.Accepted, ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, handled);
    }

    [Theory]
    [InlineData(ActivationEnvelope.MaxSize + 1)]
    [InlineData(64 * 1024)]
    public void An_oversized_request_is_never_accepted_and_the_owner_stays_healthy(int size)
    {
        InstanceKey key = NewKey();
        int handled = 0;
        using ActivationPipeServer server = StartServer(key, request => { _ = Interlocked.Increment(ref handled); return ActivationReply.Accepted; });

        // A valid header followed by far too much.
        byte[] message = ActivationEnvelope.Frame(1, 1, new byte[size - ActivationEnvelope.HeaderSize]);
        HandoffStatus status = ActivationPipeClient.SendRaw(key.PipeName, message, TimeSpan.FromSeconds(2), key.UserSid);

        output.WriteLine($"{size} bytes: {status}");
        Assert.Contains(status, new[] { HandoffStatus.Rejected, HandoffStatus.Unavailable });
        Assert.Equal(0, handled);

        Assert.Equal(HandoffStatus.Accepted, ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void After_shutdown_begins_every_request_is_told_so_and_none_is_handed_on()
    {
        InstanceKey key = NewKey();
        int handled = 0;
        using ActivationPipeServer server = StartServer(key, request => { _ = Interlocked.Increment(ref handled); return ActivationReply.Accepted; });

        server.BeginShutdown();

        Assert.Equal(HandoffStatus.ShuttingDown, ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, handled);
    }

    [Fact]
    public void A_client_that_connects_and_says_nothing_is_cut_off_and_the_next_is_served()
    {
        InstanceKey key = NewKey();
        using ActivationPipeServer server = StartServer(key, request => ActivationReply.Accepted);
        using var silent = new NamedPipeClientStream(".", key.PipeName, PipeDirection.InOut);
        silent.Connect(2000);

        var clock = Stopwatch.StartNew();
        HandoffStatus next = ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromSeconds(6));

        output.WriteLine($"next client served after {clock.ElapsedMilliseconds} ms");
        Assert.Equal(HandoffStatus.Accepted, next);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void A_client_that_never_hangs_up_is_cut_off_and_the_next_is_served()
    {
        InstanceKey key = NewKey();
        using ActivationPipeServer server = StartServer(key, request => ActivationReply.Accepted);
        using var lingering = new NamedPipeClientStream(".", key.PipeName, PipeDirection.InOut);
        lingering.Connect(2000);
        lingering.Write(ActivationEnvelope.Encode(ActivationRequest.Activate));
        Assert.Equal((int)ActivationReply.Accepted, lingering.ReadByte());

        var clock = Stopwatch.StartNew();
        HandoffStatus next = ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromSeconds(6));

        output.WriteLine($"next client served after {clock.ElapsedMilliseconds} ms");
        Assert.Equal(HandoffStatus.Accepted, next);
        Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Many_launches_in_a_burst_are_each_answered()
    {
        InstanceKey key = NewKey();
        int handled = 0;
        using ActivationPipeServer server = StartServer(key, request => { _ = Interlocked.Increment(ref handled); return ActivationReply.Accepted; });

        HandoffStatus[] statuses = Enumerable.Range(0, 8)
            .AsParallel()
            .WithDegreeOfParallelism(8)
            .Select(_ => ActivationPipeClient.Send(key, ActivationRequest.Activate, TimeSpan.FromSeconds(2)))
            .ToArray();

        Assert.All(statuses, s => Assert.Equal(HandoffStatus.Accepted, s));
        Assert.Equal(8, handled);
    }

    // ------------------------------------------------------------- security

    [Fact]
    public void The_pipe_grants_the_current_user_and_no_one_else()
    {
        InstanceKey key = NewKey();
        using ActivationPipeServer server = StartServer(key, request => ActivationReply.Accepted);
        using var client = new NamedPipeClientStream(".", key.PipeName, PipeDirection.InOut);
        client.Connect(2000);

        PipeSecurity security = client.GetAccessControl();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<PipeAccessRule>()
            .ToList();

        foreach (PipeAccessRule rule in rules)
        {
            output.WriteLine($"{rule.AccessControlType} {rule.IdentityReference} {rule.PipeAccessRights}");
        }

        Assert.True(security.AreAccessRulesProtected);
        PipeAccessRule only = Assert.Single(rules);
        Assert.Equal(AccessControlType.Allow, only.AccessControlType);
        Assert.Equal(ProcessIdentity.CurrentUserSid(), only.IdentityReference.Value);
    }

    [Fact]
    public void A_client_reaching_the_pipe_over_the_network_is_refused()
    {
        InstanceKey key = NewKey();
        int handled = 0;
        using ActivationPipeServer server = StartServer(key, request => { _ = Interlocked.Increment(ref handled); return ActivationReply.Accepted; });

        // \\localhost\pipe\… goes through the SMB redirector: Windows treats
        // it as a remote client, which PIPE_REJECT_REMOTE_CLIENTS refuses.
        using var remote = new NamedPipeClientStream("localhost", key.PipeName, PipeDirection.InOut);
        Exception? refused = Record.Exception(() => remote.Connect(3000));

        output.WriteLine($"network connection: {refused?.GetType().Name ?? "CONNECTED"} {refused?.Message}");
        Assert.NotNull(refused);
        Assert.False(remote.IsConnected);
        Assert.Equal(0, handled);
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1001", 1, 1, null)]
    [InlineData("S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1002", 1, 1, HandoffStatus.WrongUser)]
    [InlineData("S-1-5-21-1-2-3-1001", null, 1, 1, HandoffStatus.WrongUser)]
    [InlineData("S-1-5-21-1-2-3-1001", "S-1-5-21-1-2-3-1001", 1, 2, HandoffStatus.OtherSession)]
    [InlineData("S-1-5-21-1-2-3-1001", "S-1-5-18", 1, 0, HandoffStatus.WrongUser)]
    public void The_server_must_be_the_same_user_in_the_same_session(
        string expectedSid, string? serverSid, int session, int serverSession, HandoffStatus? expected)
    {
        Assert.Equal(expected, ActivationPipeClient.JudgeServer(expectedSid, serverSid, session, serverSession));
    }

    [Fact]
    public void A_pipe_served_by_another_user_is_refused_before_anything_is_sent()
    {
        // Windows' own pipes are served by system accounts, in session 0 —
        // a real server that is not this user.
        string[] candidates = ["epmapper", "ntsvcs", "srvsvc", "wkssvc", "lsass"];
        string mySid = ProcessIdentity.CurrentUserSid();
        int mySession = ProcessIdentity.CurrentSession();

        foreach (string name in candidates)
        {
            if (ActivationPipeClient.ServerOf(name, TimeSpan.FromSeconds(1)) is not var (pid, sid, session) || sid == mySid)
            {
                continue;
            }

            output.WriteLine($"{name}: pid {pid}, user {sid ?? "(unreadable)"}, session {session}; this process: session {mySession}");

            HandoffStatus status = ActivationPipeClient.SendRaw(name, ActivationEnvelope.Encode(ActivationRequest.Activate), TimeSpan.FromSeconds(1), mySid);

            Assert.Equal(HandoffStatus.WrongUser, status);

            if (session != mySession && sid is not null)
            {
                // The same real server, judged as if it were this user's: only
                // the session differs.
                Assert.Equal(HandoffStatus.OtherSession, ActivationPipeClient.JudgeServer(sid, sid, mySession, session));
            }

            return;
        }

        Assert.Fail("No system pipe could be opened to stand in for a foreign server.");
    }

    [Fact]
    public void This_process_is_not_elevated_when_the_suite_runs_normally()
    {
        // Evidence for the record, not a contract: CI may run elevated.
        output.WriteLine($"elevated: {ProcessIdentity.IsElevated()}, session: {ProcessIdentity.CurrentSession()}");
    }

    // -------------------------------------------------------------- helpers

    private static ActivationPipeServer StartServer(InstanceKey key, Func<ActivationRequest, ActivationReply> onRequest)
    {
        PipeStartStatus status = ActivationPipeServer.Start(key, onRequest, out ActivationPipeServer? server, out int error);

        Assert.True(status == PipeStartStatus.Started, $"The activation pipe did not start: {status} ({error}).");
        return server!;
    }

    /// <summary>Claims, lets the test look at the ownership, and always lets it go.</summary>
    private static InstanceClaim ClaimWhile(InstanceKey key, bool elevated, ClaimTimings timings, Action<InstanceOwnership?> whileHeld)
    {
        InstanceOwnership? owned = null;

        try
        {
            InstanceClaim claim = InstanceOwnership.Claim(key, elevated, timings, out owned);
            whileHeld(owned);
            return claim;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static T OnOtherThread<T>(Func<T> work)
    {
        T result = default!;
        var thread = new Thread(() =>
        {
            result = work();

            if (result is IDisposable disposable)
            {
                disposable.Dispose();
            }
        });
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>
    /// Holds a root on a thread of its own, as another process would. With
    /// <c>abandon</c>, the thread ends without letting go — a crashed owner.
    /// </summary>
    private sealed class Holder : IDisposable
    {
        private readonly ManualResetEventSlim _acquired = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        private int _disposed;

        public Holder(InstanceKey key, bool abandon = false)
        {
            _thread = new Thread(() =>
            {
                InstanceOwnership? owned = InstanceOwnership.TryAcquire(key);
                Owned = owned is not null;
                _acquired.Set();
                _release.Wait();

                if (!abandon)
                {
                    owned?.Dispose();
                }
            });
            _thread.Start();
            _acquired.Wait();
        }

        public bool Owned { get; private set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _release.Set();
            _thread.Join();
            _acquired.Dispose();
            _release.Dispose();
        }
    }
}
