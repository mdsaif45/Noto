namespace Noto.Core.Workspace;

/// <summary>
/// Where a workspace stands with its welcome (ADR-021): the short tour a new workspace opens with.
/// </summary>
/// <remarks>
/// Only a workspace Noto has just created is ever <see cref="Due"/>. Every other workspace, whether opened, upgraded
/// from a Noto without a welcome, or restored from a backup, is <see cref="None"/>, the default, and never shows it.
/// </remarks>
public enum WelcomeState
{
    /// <summary>Not owed: a workspace that existed before, or one whose welcome was settled otherwise. The default.</summary>
    None,

    /// <summary>A new workspace whose welcome has not been finished or skipped: it opens with the welcome.</summary>
    Due,

    /// <summary>The welcome was finished.</summary>
    Completed,

    /// <summary>The welcome was skipped, or put away with Escape.</summary>
    Skipped,
}
