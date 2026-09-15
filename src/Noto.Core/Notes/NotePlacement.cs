namespace Noto.Core.Notes;

/// <summary>
/// Where a note should sit within its ordering scope.
/// </summary>
/// <remarks>
/// <para>
/// An intent, not a number. <c>SortOrder</c> is computed by the engine and
/// never supplied by the caller (contract §5), so the command layer says
/// <i>first</i> or <i>after this sibling</i> and the repository turns that into
/// a value — which is what keeps the O6 renormalisation trigger inside the
/// engine rather than leaking to every call site.
/// </para>
/// </remarks>
public readonly record struct NotePlacement
{
    private NotePlacement(NoteId? afterSibling, bool atEnd)
    {
        AfterSibling = afterSibling;
        AtEnd = atEnd;
    }

    /// <summary>The sibling to sit immediately after, when there is one.</summary>
    public NoteId? AfterSibling { get; }

    /// <summary>Whether the note goes to the end of the scope.</summary>
    public bool AtEnd { get; }

    /// <summary>First in the scope — O3's <c>min − 1</c>.</summary>
    public static NotePlacement First { get; } = new(null, atEnd: false);

    /// <summary>Last in the scope — O3's <c>max + 1</c>.</summary>
    public static NotePlacement Last { get; } = new(null, atEnd: true);

    /// <summary>Immediately after an active sibling in the same scope.</summary>
    public static NotePlacement After(NoteId sibling) => new(sibling, atEnd: false);
}
