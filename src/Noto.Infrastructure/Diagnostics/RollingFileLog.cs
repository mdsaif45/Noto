using System.Buffers;
using System.Globalization;
using System.Security;
using System.Text.Json;
using Noto.Core.Diagnostics;
using Noto.Core.Settings;
using Noto.Core.Storage;

namespace Noto.Infrastructure.Diagnostics;

/// <summary>
/// Noto's log (#7): one JSON object per line, in the data root's <c>logs</c> folder.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it can contain is fixed by the events it implements.</b> Each event's parameters are typed so that note
/// content, setting values, exception messages, stack traces and absolute paths cannot be passed to it
/// (<see cref="IStorageLog"/>); this class writes those parameters and nothing else. There is no method that takes
/// free text.
/// </para>
/// <para>
/// <b>Bounded.</b> A file is closed once the next line would take it past <c>maxFileBytes</c>, and only the newest
/// <c>maxFiles</c> files are kept, the current one included: at most about <c>maxFiles × maxFileBytes</c> on disk
/// (10 MiB by default). Files are named by the moment they were started, so their names sort oldest first.
/// </para>
/// <para>
/// <b>Never the reason Noto fails.</b> A write that cannot happen — a full disk, a locked file, a folder that cannot
/// be created — is dropped. Writes are serialized, so lines from concurrent callers never interleave.
/// </para>
/// </remarks>
public sealed class RollingFileLog : IStorageLog, IAppLog
{
    /// <summary>The size at which a file is closed and a new one started.</summary>
    public const long DefaultMaxFileBytes = 1024 * 1024;

    /// <summary>How many files are kept, the one being written included.</summary>
    public const int DefaultMaxFiles = 10;

    private const string FilePattern = "noto-*.log";

    private readonly string _directory;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private string? _current;
    private long _currentLength;
    private int _sequence;
    private int _level = (int)LogLevel.Information;

    public RollingFileLog(string directory, long maxFileBytes = DefaultMaxFileBytes, int maxFiles = DefaultMaxFiles, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFiles, 1);

        _directory = directory;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The least severe event written. <see cref="LogLevel.Information"/> until settings say otherwise.</summary>
    public LogLevel Level
    {
        get => (LogLevel)Volatile.Read(ref _level);
        set => Volatile.Write(ref _level, (int)value);
    }

    public void SafetyCopyCreated(int fromVersion, DataRootPath copy) =>
        Write(LogLevel.Information, "SafetyCopyCreated", w =>
        {
            w.WriteNumber("fromVersion", fromVersion);
            w.WriteString("copy", copy.Value);
        });

    public void MigrationStarting(int fromVersion, int toVersion, int pendingCount) =>
        Write(LogLevel.Information, "MigrationStarting", w =>
        {
            w.WriteNumber("fromVersion", fromVersion);
            w.WriteNumber("toVersion", toVersion);
            w.WriteNumber("pending", pendingCount);
        });

    // The description is the migration's own fixed text, written in code.
    public void MigrationApplied(int version, string description) =>
        Write(LogLevel.Information, "MigrationApplied", w =>
        {
            w.WriteNumber("version", version);
            w.WriteString("description", description);
        });

    public void MigrationCompleted(int version) =>
        Write(LogLevel.Information, "MigrationCompleted", w => w.WriteNumber("version", version));

    public void MigrationFailed(int version, ExceptionSummary failure) =>
        Write(LogLevel.Error, "MigrationFailed", w =>
        {
            w.WriteNumber("version", version);
            WriteFailure(w, failure);
        });

    public void SettingFellBackToDefault(SettingKey key, SettingFallbackReason reason) =>
        Write(LogLevel.Warning, "SettingFellBackToDefault", w =>
        {
            w.WriteString("key", key.Name);
            w.WriteString("reason", reason.ToString());
            w.WriteString("kind", key.Kind.ToString());
        });

    public void SettingsUnreadable(ExceptionSummary failure) =>
        Write(LogLevel.Error, "SettingsUnreadable", w => WriteFailure(w, failure));

    public void SettingSubscriberFailed(SettingKey key, ExceptionSummary failure) =>
        Write(LogLevel.Warning, "SettingSubscriberFailed", w =>
        {
            w.WriteString("key", key.Name);
            WriteFailure(w, failure);
        });

    public void StartupFailed(StorageFailure reason, ExceptionSummary failure) =>
        Write(LogLevel.Error, "StartupFailed", w =>
        {
            w.WriteString("reason", reason.ToString());
            WriteFailure(w, failure);
        });

    public void UnhandledException(UnhandledSource source, ExceptionSummary failure) =>
        Write(LogLevel.Error, "UnhandledException", w =>
        {
            w.WriteString("source", source.ToString());
            WriteFailure(w, failure);
        });

    public void EmergencySave(bool saved) =>
        Write(saved ? LogLevel.Information : LogLevel.Warning, "EmergencySave", w => w.WriteBoolean("saved", saved));

    public void EmergencySaveFailed(ExceptionSummary failure) =>
        Write(LogLevel.Error, "EmergencySaveFailed", w => WriteFailure(w, failure));

    public void UnobservedTaskException(ExceptionSummary failure) =>
        Write(LogLevel.Warning, "UnobservedTaskException", w => WriteFailure(w, failure));

    private static void WriteFailure(Utf8JsonWriter w, ExceptionSummary failure)
    {
        w.WriteStartArray("exception");

        foreach (string type in failure.Types)
        {
            w.WriteStringValue(type);
        }

        w.WriteEndArray();
        w.WriteString("hresult", "0x" + failure.HResult.ToString("X8", CultureInfo.InvariantCulture));
    }

    private void Write(LogLevel level, string name, Action<Utf8JsonWriter> fields)
    {
        if (level < Level)
        {
            return;
        }

        try
        {
            var line = new ArrayBufferWriter<byte>();

            using (var w = new Utf8JsonWriter(line))
            {
                w.WriteStartObject();
                w.WriteString("time", _time.GetUtcNow());
                w.WriteString("level", level.ToString());
                w.WriteString("event", name);
                fields(w);
                w.WriteEndObject();
            }

            line.Write("\n"u8);

            lock (_gate)
            {
                Append(line.WrittenSpan);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // Dropped: the log is how a failure is diagnosed, never a reason for one.
        }
    }

    private void Append(ReadOnlySpan<byte> line)
    {
        if (_current is null || (_currentLength > 0 && _currentLength + line.Length > _maxFileBytes))
        {
            Start(line.Length);
        }

        using (var stream = new FileStream(_current!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Write(line);
            _currentLength = stream.Length;
        }
    }

    /// <summary>Continues this process's file, or the newest one if it has room for the line; otherwise starts a new one.</summary>
    private void Start(int needed)
    {
        Directory.CreateDirectory(_directory);

        if (_current is null && Files().FirstOrDefault() is { } newest)
        {
            long length = new FileInfo(newest).Length;

            if (length + needed <= _maxFileBytes)
            {
                _current = newest;
                _currentLength = length;
                return;
            }
        }

        string stamp = _time.GetUtcNow().ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
        string path;

        do
        {
            path = Path.Join(_directory, $"noto-{stamp}-{_sequence++:D4}.log");
        }
        while (File.Exists(path));

        _current = path;
        _currentLength = 0;
        Prune();
    }

    /// <summary>Deletes all but the newest files. The current file is always kept.</summary>
    private void Prune()
    {
        foreach (string old in Files().Skip(_maxFiles - 1))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held open elsewhere; the next start tries again.
            }
        }
    }

    /// <summary>The log files, newest first.</summary>
    private IEnumerable<string> Files() =>
        Directory.EnumerateFiles(_directory, FilePattern).Order(StringComparer.Ordinal).Reverse();
}
