namespace Noto.Core.Tags;

/// <summary>
/// The tag-name normalisation rule (contract §7a, APPROVED U12a).
/// </summary>
/// <remarks>
/// <para>
/// One function rather than a <c>Trim()</c> at each call site. The rule has to
/// hold for validation, for the uniqueness comparison, for what is persisted
/// and for the rename comparison; four separate trims are four places for one
/// of them to be forgotten, and the one that gets forgotten is the comparison —
/// which is exactly the case that lets an invisible duplicate through.
/// </para>
/// <para>
/// <b>Why normalisation is needed at all.</b> <c>UX_Tags_Name ... COLLATE
/// NOCASE</c> makes <c>'work'</c> and <c>'Work'</c> collide, but collation says
/// nothing about whitespace: the index treats <c>" Work"</c>, <c>"Work "</c>
/// and <c>"Work"</c> as three distinct names. Without this rule the engine
/// would admit tags indistinguishable in any list the user sees, while
/// correctly rejecting the visible duplicate.
/// </para>
/// <para>
/// <b>The database cannot enforce this.</b> It is an application-layer rule
/// applied before the value reaches SQLite, so tests must prove the handler
/// normalises rather than leaning on the unique index to catch it.
/// </para>
/// </remarks>
public static class TagName
{
    /// <summary>
    /// Trims leading and trailing whitespace. Internal whitespace is untouched.
    /// </summary>
    /// <returns>
    /// The normalised name, or <see langword="null"/> when the input is
    /// <see langword="null"/>, empty, or whitespace only — the caller turns
    /// that into <see cref="Commands.CommandFailureReason.InvalidInput"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Returning <see langword="null"/> for "not a usable name" rather than
    /// returning an empty string means the caller cannot accidentally persist
    /// <c>""</c> by skipping a separate emptiness check — the normalisation and
    /// the validation are one step.
    /// </para>
    /// <para>
    /// <c>string.Trim()</c> removes every Unicode whitespace character, so a
    /// tab- or newline-padded name normalises the same way a space-padded one
    /// does. <c>"Work  Item"</c> keeps both of its inner spaces: collapsing
    /// those would change a name the user chose (contract §7a, "not in scope").
    /// </para>
    /// </remarks>
    public static string? Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Trim();
    }
}
