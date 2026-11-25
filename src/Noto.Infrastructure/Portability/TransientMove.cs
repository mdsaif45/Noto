namespace Noto.Infrastructure.Portability;

/// <summary>
/// A rename that another process may block for a moment (#280). Right after Noto writes files, Windows Defender or the
/// search indexer often has them open, and a move fails at once with "access denied" or "in use". Export, backup and
/// restore each end with such a move, so each tries again briefly before reporting a real failure.
/// </summary>
public static class TransientMove
{
    // About 1.5 s in all: longer than a scan of a just-written file usually takes, short enough to wait for.
    private static readonly int[] WaitsMs = [50, 100, 200, 400, 800];

    /// <summary>
    /// Runs <paramref name="move"/>, again after each wait while it fails as a held file does. A missing source or folder
    /// is not held, so it fails at once; after the last wait, the failure is thrown as it is.
    /// </summary>
    public static void Run(Action move)
    {
        ArgumentNullException.ThrowIfNull(move);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                move();
                return;
            }
            catch (Exception ex) when (IsHeld(ex) && attempt < WaitsMs.Length)
            {
                Thread.Sleep(WaitsMs[attempt]);
            }
        }
    }

    private static bool IsHeld(Exception ex) =>
        ex is UnauthorizedAccessException or IOException and not (FileNotFoundException or DirectoryNotFoundException);
}
