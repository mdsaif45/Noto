using Noto.Core.Diagnostics;

namespace Noto.Infrastructure.Diagnostics;

/// <summary>
/// The record of Noto's recent failures (F16, #10): a few lines of times in the data root, read at a start to tell a
/// crash loop (<see cref="CrashLoop"/>). Best effort throughout: a record that cannot be read or written never stops
/// Noto from starting or from ending.
/// </summary>
public static class FailureHistory
{
    /// <summary>The failures recorded in <paramref name="file"/>; none when it is missing or unreadable.</summary>
    public static IReadOnlyList<Failure> Read(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        try
        {
            return File.Exists(file) ? CrashLoop.Parse(File.ReadAllLines(file)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Adds a failure, keeping only the newest <see cref="CrashLoop.Kept"/>.</summary>
    public static void Append(string file, Failure failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        try
        {
            var lines = Read(file).Append(failure).TakeLast(CrashLoop.Kept).Select(CrashLoop.Line);
            File.WriteAllLines(file, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The process is already failing: a record that cannot be written is the one thing not recorded.
        }
    }

    /// <summary>Forgets every failure: the user chose to start anyway.</summary>
    public static void Clear(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left as it is: the same question is asked again at the next start, which is safe.
        }
    }
}
