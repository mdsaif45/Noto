namespace Noto.Core.Diagnostics;

/// <summary>How much Noto writes to its log. Each level includes the ones above it.</summary>
/// <remarks>Stored by name as the <c>diagnostics.log-level</c> setting, so the names are part of the contract.</remarks>
public enum LogLevel
{
    /// <summary>Detail for diagnosing a problem; off unless asked for.</summary>
    Debug,

    /// <summary>What Noto did that is worth knowing afterwards: a migration ran, a safety copy was taken. The default.</summary>
    Information,

    /// <summary>Something was wrong and Noto carried on: a setting fell back to its default.</summary>
    Warning,

    /// <summary>Something failed: a migration rolled back, settings could not be read.</summary>
    Error,
}
