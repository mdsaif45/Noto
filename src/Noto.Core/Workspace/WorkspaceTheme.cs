namespace Noto.Core.Workspace;

/// <summary>
/// Which theme the workspace draws in (parity J21, F1): follow Windows, or one chosen by the user.
/// </summary>
/// <remarks>
/// High contrast is not one of these. It is the user's accessibility choice, made in Windows, and it wins over all
/// three: a workspace set to Light still draws in the user's high-contrast colours while high contrast is on.
/// </remarks>
public enum WorkspaceTheme
{
    /// <summary>Light or dark as Windows' app mode is. The default.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
}
