using Microsoft.Data.Sqlite;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// Names one ordered collection for <see cref="SortOrderEngine"/>: which table
/// it lives in, and which rows belong to it.
/// </summary>
/// <remarks>
/// <para>
/// The ordering rules O1–O6 are identical for notes and folders — midpoint
/// insertion, <c>min − 1</c> / <c>max + 1</c> at the ends, renormalisation when
/// the gap is exhausted. Only three things differ, and this type carries all
/// three so the algorithm itself can stay free of either domain.
/// </para>
/// <para>
/// Notes are scoped per folder, with root as its own scope (O1), so their
/// predicate is <c>FolderId IS NULL</c> or <c>FolderId = @scope</c>. Folders are
/// the degenerate case: the whole collection is one scope, so their predicate
/// is constant.
/// </para>
/// <para>
/// The SQL fragments here come from this assembly's own call sites and name
/// compile-time constants — never caller input. Values are always parameterised.
/// </para>
/// </remarks>
internal sealed class SortOrderScope
{
    private readonly Action<SqliteCommand>? _bindScope;

    private SortOrderScope(string table, string predicate, Action<SqliteCommand>? bindScope)
    {
        Table = table;
        Predicate = predicate;
        _bindScope = bindScope;
    }

    /// <summary>The table the ordered rows live in.</summary>
    public string Table { get; }

    /// <summary>
    /// A SQL boolean naming the rows in this scope, excluding the deleted
    /// filter, which the engine always adds itself (I6).
    /// </summary>
    public string Predicate { get; }

    /// <summary>The folder list: one scope, no discriminator.</summary>
    public static SortOrderScope AllFolders { get; } =
        new("Folders", "1 = 1", bindScope: null);

    /// <summary>
    /// Notes in one folder, or at root when <paramref name="folderId"/> is
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Two predicates rather than one, because <c>FolderId = NULL</c> matches
    /// nothing in SQL's three-valued logic — root would silently become empty.
    /// </remarks>
    public static SortOrderScope NotesIn(string? folderId) =>
        folderId is null
            ? new SortOrderScope("Notes", "FolderId IS NULL", bindScope: null)
            : new SortOrderScope(
                "Notes",
                "FolderId = $scope",
                command => command.Parameters.AddWithValue("$scope", folderId));

    /// <summary>Binds the scope parameter, when the predicate uses one.</summary>
    public void Bind(SqliteCommand command) => _bindScope?.Invoke(command);
}
