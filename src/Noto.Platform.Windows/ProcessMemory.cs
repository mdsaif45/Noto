namespace Noto.Platform.Windows;

/// <summary>
/// Gives back the physical memory a hidden Noto is not using (F22, #160): Windows takes the process's pages out of its
/// working set, and brings back each page the moment it is touched. Nothing is freed or lost: the pages stay Noto's
/// (private bytes do not change), so the cost is paid once, by the first show that touches them.
/// </summary>
public static class ProcessMemory
{
    /// <summary>Asks Windows to trim this process's working set to what it touches from now on.</summary>
    /// <returns>Whether Windows accepted.</returns>
    public static bool Trim() => NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);
}
