namespace Noto.Core.Portability;

/// <summary>
/// How often Noto backs itself up (#199; parity I8, J38): once a day, Noto's own default, or one of SideNotes' three
/// intervals. Stored by member name.
/// </summary>
public enum BackupFrequency
{
    /// <summary>Once a day: the default, as automatic backups have always been (F15, #15).</summary>
    Daily,

    /// <summary>Every 45 minutes.</summary>
    Every45Minutes,

    /// <summary>Every 30 minutes.</summary>
    Every30Minutes,

    /// <summary>Every 15 minutes.</summary>
    Every15Minutes,
}
