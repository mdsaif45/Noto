namespace Noto.Core.Diagnostics;

/// <summary>
/// A location as a log may record it: relative to Noto's data root, or <c>&lt;outside&gt;</c>. Never absolute.
/// </summary>
/// <remarks>
/// <para>
/// An absolute path names the user (<c>C:\Users\&lt;name&gt;\…</c>) and, outside the data root, their own files.
/// Logs never contain user file paths (principle 10, quality-gates). This type has no absolute form: a path inside
/// the data root becomes a normalized relative path with <c>/</c> separators; anything else — another folder, another
/// drive, a share, a path that climbs out with <c>..</c>, or a name that is not plain — is <c>&lt;outside&gt;</c>.
/// </para>
/// <para>
/// Everything Noto writes under its data root has a plain generated name (<c>noto.db</c>,
/// <c>noto-premigration-v1-….db</c>, <c>logs\…</c>), so the strict character rule loses nothing real.
/// </para>
/// </remarks>
public readonly struct DataRootPath : IEquatable<DataRootPath>
{
    /// <summary>What is recorded for a location that is not plainly inside the data root.</summary>
    public const string OutsideMarker = "<outside>";

    private readonly string? _value;

    private DataRootPath(string value) => _value = value;

    /// <summary>The relative path with <c>/</c> separators, or <see cref="OutsideMarker"/>.</summary>
    public string Value => _value ?? OutsideMarker;

    /// <summary>Whether the location is inside the data root.</summary>
    public bool IsInside => _value is not null && _value != OutsideMarker;

    /// <summary>Represents <paramref name="path"/> relative to <paramref name="dataRoot"/>. Never throws.</summary>
    public static DataRootPath Of(string dataRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(dataRoot))
        {
            return default;
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
            string relative = Path.GetRelativePath(root, Path.GetFullPath(path, root));

            // Another drive or share comes back rooted; a path that climbs out comes back starting with "..".
            if (relative == "." || Path.IsPathRooted(relative))
            {
                return default;
            }

            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return segments.All(IsPlainSegment) ? new DataRootPath(string.Join('/', segments)) : default;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return default;
        }
    }

    /// <summary>A plain file or folder name: letters, digits, '.', '-', '_' and spaces; never "." or "..".</summary>
    private static bool IsPlainSegment(string segment) =>
        segment.Length > 0
        && segment != "."
        && segment != ".."
        && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ' ');

    public bool Equals(DataRootPath other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is DataRootPath other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    public static bool operator ==(DataRootPath left, DataRootPath right) => left.Equals(right);

    public static bool operator !=(DataRootPath left, DataRootPath right) => !left.Equals(right);
}
