namespace Noto.UseCases.Lifetime;

/// <summary>
/// What the user is asked when Noto has failed at every start (F16, #10): it has stopped instead of failing again.
/// </summary>
/// <remarks>
/// Names the data folder, where the notes and their daily backups are: shown on screen only, never logged
/// (principle 10).
/// </remarks>
public static class CrashLoopMessage
{
    public static string For(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        return "Noto stopped unexpectedly several times in a row, each time soon after it started, so it has not started "
            + "again on its own.\n\nYour notes are safe in its data folder, with its daily backups:\n"
            + dataRoot
            + "\n\nStart Noto anyway?";
    }
}
