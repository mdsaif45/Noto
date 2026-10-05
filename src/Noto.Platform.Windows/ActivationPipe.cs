using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace Noto.Platform.Windows;

/// <summary>How starting the activation pipe turned out.</summary>
public enum PipeStartStatus
{
    /// <summary>Listening.</summary>
    Started,

    /// <summary>
    /// Another process already holds the pipe's name — squatting. This Noto
    /// still owns its data root, but no second launch can reach it.
    /// </summary>
    NameTaken,

    /// <summary>Windows refused the pipe for another reason; see the error code.</summary>
    Failed,
}

/// <summary>What happened to one handoff attempt, as the second launch sees it.</summary>
public enum HandoffStatus
{
    /// <summary>The running Noto took the request.</summary>
    Accepted,

    /// <summary>The running Noto is closing.</summary>
    ShuttingDown,

    /// <summary>The running Noto rejected the request.</summary>
    Rejected,

    /// <summary>The pipe's server runs as another user.</summary>
    WrongUser,

    /// <summary>The pipe's server runs in another Windows session.</summary>
    OtherSession,

    /// <summary>No answer: no pipe, busy past the timeout, broken, or silent.</summary>
    Unavailable,
}

/// <summary>
/// The running Noto's end of the activation pipe (A17, ADR-013): one
/// instance, serving one connection at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounded, sequential.</b> Connect → read one request (2 s) → validate →
/// hand it to the callback → write one reply (2 s) → wait for the client to
/// hang up (2 s) → disconnect → accept the next. Every wait is bounded; a
/// stalled client's read or write is cancelled with <c>CancelIoEx</c>, so it
/// cannot hold up the next one. There is no queue: a client that arrives
/// while another is served waits in <c>WaitNamedPipe</c> on its own side.
/// </para>
/// <para>
/// <b>Security.</b> The pipe's DACL grants the current user and no one else;
/// remote clients are refused (<c>PIPE_REJECT_REMOTE_CLIENTS</c>); the name
/// is claimed with <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> and only one instance
/// is allowed, so no other process can add an instance beside it, and a name
/// already taken is reported as <see cref="PipeStartStatus.NameTaken"/>.
/// </para>
/// <para>
/// <b>Threads.</b> Connections are served on the thread pool. The callback
/// runs there too, so it must only hand the request on — never touch a
/// window.
/// </para>
/// </remarks>
public sealed class ActivationPipeServer : IDisposable
{
    /// <summary>How long a client may take to send its request, and to read the reply.</summary>
    internal static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(2);

    private readonly NamedPipeServerStream _pipe;
    private readonly Func<ActivationRequest, ActivationReply> _onRequest;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _shuttingDown;
    private int _disposed;

    private ActivationPipeServer(NamedPipeServerStream pipe, Func<ActivationRequest, ActivationReply> onRequest)
    {
        _pipe = pipe;
        _onRequest = onRequest;
        _loop = Task.Run(RunAsync);
    }

    /// <summary>
    /// Creates the pipe and starts listening.
    /// </summary>
    /// <param name="key">Whose pipe.</param>
    /// <param name="onRequest">
    /// Called with each valid request, on a thread-pool thread, one at a
    /// time; its answer is the reply. Not called once
    /// <see cref="BeginShutdown"/> has run.
    /// </param>
    /// <param name="server">The server, when <see cref="PipeStartStatus.Started"/>.</param>
    /// <param name="errorCode">The Win32 error otherwise.</param>
    public static PipeStartStatus Start(
        InstanceKey key,
        Func<ActivationRequest, ActivationReply> onRequest,
        out ActivationPipeServer? server,
        out int errorCode)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(onRequest);

        server = null;
        SafePipeHandle? handle = Create(key, out errorCode);
        NamedPipeServerStream? pipe = null;

        try
        {
            if (handle is null)
            {
                // ACCESS_DENIED is FIRST_PIPE_INSTANCE's answer when the name
                // exists; PIPE_BUSY is the answer when it exists with its one
                // instance already taken.
                return errorCode is NativeMethods.ERROR_ACCESS_DENIED or NativeMethods.ERROR_PIPE_BUSY
                    ? PipeStartStatus.NameTaken
                    : PipeStartStatus.Failed;
            }

            pipe = new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
            handle = null;
            server = new ActivationPipeServer(pipe, onRequest);
            pipe = null;
            return PipeStartStatus.Started;
        }
        finally
        {
            // Only on failure: ownership passed to the server otherwise.
            pipe?.Dispose();
            handle?.Dispose();
        }
    }

    /// <summary>
    /// The owner is closing: every valid request from now on is answered
    /// <see cref="ActivationReply.ShuttingDown"/> without being handed on.
    /// </summary>
    public void BeginShutdown() => Volatile.Write(ref _shuttingDown, 1);

    /// <summary>
    /// Stops listening and closes the pipe, so its name is free once this
    /// returns. Call before releasing ownership, so a successor can create it.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        BeginShutdown();
        _stop.Cancel();

        // Closing the handle cancels whatever is pending on it.
        _pipe.Dispose();

        try
        {
            _ = _loop.Wait(IoTimeout);
        }
        catch (AggregateException ex)
        {
            Debug.WriteLine($"The activation pipe stopped with: {ex.InnerException?.GetType().Name}");
        }

        _stop.Dispose();
    }

    private static unsafe SafePipeHandle? Create(InstanceKey key, out int errorCode)
    {
        // Protected DACL, one ACE: the current user, full access. No
        // inherited entries, so administrators and SYSTEM are not granted.
        var descriptor = new RawSecurityDescriptor($"D:P(A;;GA;;;{key.UserSid})");
        byte[] binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);

        fixed (byte* sd = binary)
        {
            var attributes = new NativeMethods.SECURITY_ATTRIBUTES
            {
                nLength = (uint)sizeof(NativeMethods.SECURITY_ATTRIBUTES),
                lpSecurityDescriptor = (nint)sd,
                bInheritHandle = 0,
            };

            nint raw = NativeMethods.CreateNamedPipe(
                $@"\\.\pipe\{key.PipeName}",
                NativeMethods.PIPE_ACCESS_DUPLEX | NativeMethods.FILE_FLAG_OVERLAPPED | NativeMethods.FILE_FLAG_FIRST_PIPE_INSTANCE,
                NativeMethods.PIPE_TYPE_MESSAGE | NativeMethods.PIPE_READMODE_MESSAGE | NativeMethods.PIPE_WAIT | NativeMethods.PIPE_REJECT_REMOTE_CLIENTS,
                1,
                64,
                ActivationEnvelope.MaxSize,
                0,
                &attributes);

            if (raw == NativeMethods.INVALID_HANDLE_VALUE)
            {
                errorCode = Marshal.GetLastPInvokeError();
                return null;
            }

            errorCode = 0;
            return new SafePipeHandle(raw, ownsHandle: true);
        }
    }

    private async Task RunAsync()
    {
        byte[] buffer = new byte[ActivationEnvelope.MaxSize + 1];

        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await _pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                await ServeAsync(buffer).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (IOException)
            {
                // The client went away mid-conversation, or connected and left
                // before the server looked; either way, on to the next.
            }

            if (!Disconnect())
            {
                return;
            }
        }
    }

    private async Task ServeAsync(byte[] buffer)
    {
        int read = await WithTimeout(token => _pipe.ReadAsync(buffer, token)).ConfigureAwait(false);

        if (read == 0)
        {
            return;
        }

        ActivationReply reply = Decide(buffer.AsSpan(0, read));

        buffer[0] = (byte)reply;
        await WithTimeout(async token =>
        {
            await _pipe.WriteAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false);
            return 0;
        }).ConfigureAwait(false);

        // The client hangs up first, so the reply is never discarded by a
        // disconnect it has not read yet. A client that never does is cut off.
        _ = await WithTimeout(token => _pipe.ReadAsync(buffer, token)).ConfigureAwait(false);
    }

    private ActivationReply Decide(ReadOnlySpan<byte> message)
    {
        // Read one byte more than the limit: a message that fills the buffer
        // was too large, whatever else it says.
        if (ActivationEnvelope.Decode(message, out ActivationRequest request) != EnvelopeError.None)
        {
            return ActivationReply.Rejected;
        }

        if (Volatile.Read(ref _shuttingDown) != 0)
        {
            return ActivationReply.ShuttingDown;
        }

        return _onRequest(request);
    }

    /// <summary>
    /// Runs one read or write, cancelling it with <c>CancelIoEx</c> if the
    /// client has not completed it within <see cref="IoTimeout"/>.
    /// </summary>
    private async Task<int> WithTimeout(Func<CancellationToken, ValueTask<int>> operation)
    {
        using var timeout = new CancellationTokenSource(IoTimeout);
        using CancellationTokenRegistration cancel = timeout.Token.Register(CancelPendingIo);

        try
        {
            return await operation(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
            && timeout.IsCancellationRequested
            && !_stop.IsCancellationRequested)
        {
            // An I/O aborted by CancelIoEx surfaces as a cancellation; it is
            // this client's failure, not the server stopping.
            throw new IOException("The activation client did not complete in time.", ex);
        }
    }

    private void CancelPendingIo()
    {
        try
        {
            // Every pending operation this process has on the pipe: there is
            // only ever the one.
            _ = NativeMethods.CancelIoEx(_pipe.SafePipeHandle, 0);
        }
        catch (ObjectDisposedException)
        {
            // Already closed; nothing is pending.
        }
    }

    private bool Disconnect()
    {
        try
        {
            _pipe.Disconnect();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Never connected: nothing to disconnect.
            return true;
        }
        catch (IOException)
        {
            return true;
        }
    }
}

/// <summary>
/// The second launch's end of the activation pipe (A17, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// One attempt: connect (bounded) → check who the server is → let it take
/// the foreground → send one request → read one reply → hang up. Every wait
/// is bounded; nothing here retries — <see cref="InstanceOwnership.Claim"/>
/// decides that.
/// </para>
/// <para>
/// <b>The server is checked before anything is sent.</b> It must run as the
/// same user and in the same Windows session as this process; a squatter or
/// a Noto in another session is refused, not talked to.
/// </para>
/// </remarks>
public static class ActivationPipeClient
{
    /// <summary>How long to wait for the pipe to exist and be free.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Hands one request to the running Noto that owns <paramref name="key"/>.</summary>
    public static HandoffStatus Send(InstanceKey key, ActivationRequest request, TimeSpan connectTimeout)
    {
        ArgumentNullException.ThrowIfNull(key);

        return SendRaw(key.PipeName, ActivationEnvelope.Encode(request), connectTimeout, key.UserSid);
    }

    /// <summary>
    /// Sends any bytes, valid or not, to the named pipe — the client's real
    /// path, also used by tests and the validation harness for malformed
    /// requests. Runs off the calling thread, so the caller's
    /// synchronization context is never involved.
    /// </summary>
    internal static HandoffStatus SendRaw(string pipeName, byte[] message, TimeSpan connectTimeout, string expectedUserSid) =>
        Task.Run(() => SendRawAsync(pipeName, message, connectTimeout, expectedUserSid)).GetAwaiter().GetResult();

    private static async Task<HandoffStatus> SendRawAsync(string pipeName, byte[] message, TimeSpan connectTimeout, string expectedUserSid)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            pipe.Connect((int)Math.Max(1, connectTimeout.TotalMilliseconds));
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return HandoffStatus.Unavailable;
        }

        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid)
            || !NativeMethods.GetNamedPipeServerSessionId(pipe.SafePipeHandle, out uint serverSession))
        {
            return HandoffStatus.Unavailable;
        }

        HandoffStatus? identity = JudgeServer(
            expectedUserSid,
            ProcessIdentity.UserSidOf((int)serverPid),
            ProcessIdentity.CurrentSession(),
            (int)serverSession);

        if (identity is HandoffStatus refused)
        {
            return refused;
        }

        // The running Noto is about to be asked to come forward. Windows
        // lets it take the foreground only if this process — which the user
        // just launched, and so may — passes the right on.
        _ = NativeMethods.AllowSetForegroundWindow(serverPid);

        try
        {
            using (var write = new CancellationTokenSource(ActivationPipeServer.IoTimeout))
            {
                await pipe.WriteAsync(message, write.Token).ConfigureAwait(false);
            }

            byte[] reply = new byte[1];
            int read;

            using (var readTimeout = new CancellationTokenSource(ActivationPipeServer.IoTimeout))
            {
                read = await pipe.ReadAsync(reply, readTimeout.Token).ConfigureAwait(false);
            }

            return read == 1 && ActivationEnvelope.DecodeReply(reply[0]) is ActivationReply answer
                ? answer switch
                {
                    ActivationReply.Accepted => HandoffStatus.Accepted,
                    ActivationReply.ShuttingDown => HandoffStatus.ShuttingDown,
                    _ => HandoffStatus.Rejected,
                }
                : HandoffStatus.Unavailable;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            return HandoffStatus.Unavailable;
        }
    }

    /// <summary>
    /// Whether the pipe's server may be talked to: <see langword="null"/> if
    /// so, otherwise the refusal. The user is checked before the session.
    /// </summary>
    internal static HandoffStatus? JudgeServer(string expectedUserSid, string? serverUserSid, int currentSession, int serverSession)
    {
        if (!string.Equals(expectedUserSid, serverUserSid, StringComparison.OrdinalIgnoreCase))
        {
            return HandoffStatus.WrongUser;
        }

        if (currentSession != serverSession)
        {
            return HandoffStatus.OtherSession;
        }

        return null;
    }

    /// <summary>
    /// Who serves a pipe, for the validation harness: its process, user and
    /// session. <see langword="null"/> if the pipe cannot be opened.
    /// </summary>
    internal static (int ProcessId, string? UserSid, int Session)? ServerOf(string pipeName, TimeSpan connectTimeout)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.None);

        try
        {
            pipe.Connect((int)connectTimeout.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint pid)
            || !NativeMethods.GetNamedPipeServerSessionId(pipe.SafePipeHandle, out uint session))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return ((int)pid, ProcessIdentity.UserSidOf((int)pid), (int)session);
    }
}
