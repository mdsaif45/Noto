using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Noto.Infrastructure.Storage;

/// <summary>
/// The ordering rules O1–O6, over any table with an <c>Id</c>, a
/// <c>SortOrder</c> and a <c>DeletedAt</c>.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from the note repository unchanged so folders can reuse it. The
/// algorithm, the threshold and the SQL shapes are exactly what shipped for
/// notes; only the table, the scope predicate and the id type became
/// parameters (<see cref="SortOrderScope"/>).
/// </para>
/// <para>
/// It deliberately knows nothing about notes or folders. Naming either here
/// would mean the next ordered collection needs a third copy of the same
/// midpoint maths, which is how two implementations drift apart.
/// </para>
/// </remarks>
internal static class SortOrderEngine
{
    /// <summary>
    /// The gap below which a midpoint is no longer meaningfully between its
    /// neighbours, so the scope is renormalised instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Implementation-defined.</b> The contract fixes the observable
    /// behaviour — ordering stays correct, renormalisation is atomic and yields
    /// 1, 2, 3… — and states that the numeric trigger is deliberately not part
    /// of it (contract §5, U5).
    /// </para>
    /// <para>
    /// Chosen well above the point where <c>double</c> actually fails: with ~52
    /// bits of mantissa, midpoints between neighbours around 1.0 stop being
    /// distinguishable after roughly 50 halvings. Renormalising at 1e-9 leaves a
    /// wide margin, so the ordering is rebuilt long before two rows could
    /// collide.
    /// </para>
    /// </remarks>
    private const double RenormalisationThreshold = 1e-9;

    /// <summary>Where a renormalised scope starts, then 2, 3, … (O6).</summary>
    public const double RenormalisationStart = 1;

    /// <summary>
    /// Turns a placement into a concrete <c>SortOrder</c>, renormalising the
    /// scope first if the gap is exhausted.
    /// </summary>
    /// <remarks>
    /// The single place ordering values are produced, so every caller — create,
    /// move, reorder, for notes and folders alike — cannot drift apart.
    /// </remarks>
    public static double ResolvePosition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SortOrderScope scope,
        SortOrderPlacement placement,
        string movingId,
        DateTimeOffset updatedAt)
    {
        (double? before, double? after) = Neighbours(connection, transaction, scope, placement, movingId);

        // An end of the scope: O3's min - 1 / max + 1. No renumbering.
        if (before is null && after is null)
        {
            return RenormalisationStart;
        }

        if (before is null)
        {
            return after!.Value - 1;
        }

        if (after is null)
        {
            return before.Value + 1;
        }

        double gap = after.Value - before.Value;

        // Tested against the gap this insertion would LEAVE BEHIND, not the one
        // it finds. Checking `gap` alone lets a gap of 2e-9 through, which then
        // writes a midpoint 1e-9 away from its neighbour — under the threshold,
        // and the next insertion there has nothing left to split.
        if (gap / 2 > RenormalisationThreshold)
        {
            // Midpoint: one row written, not the whole scope (design §7).
            return before.Value + (gap / 2);
        }

        // O6: the gap is exhausted. Rewrite the scope to 1, 2, 3... inside this
        // transaction, then take the midpoint of the now-separated neighbours.
        Renormalise(connection, transaction, scope, movingId, updatedAt);

        (before, after) = Neighbours(connection, transaction, scope, placement, movingId);

        return (before, after) switch
        {
            (null, null) => RenormalisationStart,
            (null, { } a) => a - 1,
            ({ } b, null) => b + 1,
            ({ } b, { } a) => b + ((a - b) / 2),
        };
    }

    /// <summary>Whether the row already sits where the placement asks.</summary>
    /// <remarks>
    /// Compared by neighbours rather than by value: the question is not "is the
    /// number already right" but "is anything actually between the row and
    /// where it is being asked to go".
    /// </remarks>
    public static bool AlreadyInPosition(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SortOrderScope scope,
        SortOrderPlacement placement,
        string movingId,
        double current)
    {
        if (placement.AfterId is { } sibling
            && string.Equals(sibling, movingId, StringComparison.Ordinal))
        {
            // "After itself" is meaningless; treat it as staying put rather
            // than computing a position from its own value.
            return true;
        }

        (double? before, double? after) = Neighbours(connection, transaction, scope, placement, movingId);

        // Already after `before` and before `after` means nothing would move.
        bool afterLowerBound = before is null || current > before.Value;
        bool beforeUpperBound = after is null || current < after.Value;

        return afterLowerBound && beforeUpperBound;
    }

    /// <summary>
    /// The highest active <c>SortOrder</c> in a scope, or <see langword="null"/>
    /// when the scope holds none.
    /// </summary>
    /// <remarks>
    /// Feeds the O3 end-insertion rule — <c>max + 1</c>. Only active rows count:
    /// deleted rows keep their <c>SortOrder</c> but never participate in
    /// ordering (I6).
    /// </remarks>
    public static double? MaxSortOrder(SqliteConnection connection, SortOrderScope scope)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT MAX(SortOrder) FROM {scope.Table} WHERE {scope.Predicate} AND DeletedAt IS NULL;";
        scope.Bind(command);

        object? result = command.ExecuteScalar();

        return result is null or DBNull ? null : ToDouble(result);
    }

    /// <summary>
    /// The <c>SortOrder</c> values a placement sits between, excluding the row
    /// being moved.
    /// </summary>
    /// <remarks>
    /// The moving row is excluded so that its own current position never acts
    /// as its own neighbour, which would make "move after my predecessor" a
    /// no-op by accident. Deleted rows are excluded because they never
    /// participate in ordering (I6).
    /// </remarks>
    private static (double? Before, double? After) Neighbours(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SortOrderScope scope,
        SortOrderPlacement placement,
        string movingId)
    {
        if (placement.AtEnd)
        {
            return (ScopeExtreme(connection, transaction, scope, movingId, highest: true), null);
        }

        if (placement.AfterId is not { } sibling)
        {
            // First position: nothing before, the current minimum after.
            return (null, ScopeExtreme(connection, transaction, scope, movingId, highest: false));
        }

        double anchor = ReadSortOrder(connection, transaction, scope, sibling);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        // The next active sibling strictly after the anchor. Ties on SortOrder
        // fall back to Id, matching O2's total order, so "after" is
        // unambiguous even when two rows share a value.
        command.CommandText =
            $"""
             SELECT MIN(SortOrder) FROM {scope.Table}
             WHERE {scope.Predicate} AND DeletedAt IS NULL AND Id <> $moving
               AND (SortOrder > $anchor OR (SortOrder = $anchor AND Id > $sibling));
             """;

        command.Parameters.AddWithValue("$anchor", anchor);
        command.Parameters.AddWithValue("$sibling", sibling);
        command.Parameters.AddWithValue("$moving", movingId);
        scope.Bind(command);

        object? next = command.ExecuteScalar();

        return (anchor, next is null or DBNull ? null : ToDouble(next));
    }

    /// <summary>The highest or lowest active <c>SortOrder</c> in a scope.</summary>
    private static double? ScopeExtreme(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SortOrderScope scope,
        string excludingId,
        bool highest)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        string aggregate = highest ? "MAX" : "MIN";

        command.CommandText =
            $"SELECT {aggregate}(SortOrder) FROM {scope.Table} "
            + $"WHERE {scope.Predicate} AND DeletedAt IS NULL AND Id <> $moving;";

        command.Parameters.AddWithValue("$moving", excludingId);
        scope.Bind(command);

        object? result = command.ExecuteScalar();

        return result is null or DBNull ? null : ToDouble(result);
    }

    /// <summary>
    /// O6: rewrites the scope's <c>SortOrder</c> to 1, 2, 3… preserving the
    /// current order, inside the caller's transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The moving row is excluded</b>, so what this rewrites is the scope
    /// <i>minus</i> that row. It is about to be positioned explicitly by
    /// <see cref="ResolvePosition"/>, and including it would assign a value
    /// that the very next statement overwrites.
    /// </para>
    /// <para>
    /// The consequence is worth stating plainly, because the method name alone
    /// suggests otherwise: after the whole operation the scope is
    /// <b>not</b> contiguous. Renormalisation restores 1, 2, 3… and the
    /// insertion then places the moving row between two of them:
    /// </para>
    /// <code>
    ///   before      A=1.0  X=1.0000000005  B=2.0   M=9.0   (gap exhausted)
    ///   renormalise A=1    X=2             B=3     M=9.0   &lt;- O6, this method
    ///   place M     A=1    M=1.5           X=2     B=3     &lt;- O3 midpoint
    /// </code>
    /// <para>
    /// That satisfies the contract: O6 governs what renormalisation produces,
    /// O3 governs the insertion that follows, and O5 says the resulting gaps
    /// are harmless.
    /// </para>
    /// <para>
    /// Every row it rewrites is a row it changes, so each takes the
    /// transaction's single timestamp (contract §4).
    /// </para>
    /// </remarks>
    private static void Renormalise(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SortOrderScope scope,
        string excludingId,
        DateTimeOffset updatedAt)
    {
        var ids = new List<string>();

        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;

            // O2's ordering, so renormalising preserves what the user sees.
            read.CommandText =
                $"""
                 SELECT Id FROM {scope.Table}
                 WHERE {scope.Predicate} AND DeletedAt IS NULL AND Id <> $moving
                 ORDER BY SortOrder ASC, Id ASC;
                 """;

            read.Parameters.AddWithValue("$moving", excludingId);
            scope.Bind(read);

            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }

        double position = RenormalisationStart;

        foreach (string id in ids)
        {
            using var write = connection.CreateCommand();
            write.Transaction = transaction;
            write.CommandText =
                $"UPDATE {scope.Table} SET SortOrder = $sortOrder, UpdatedAt = $updatedAt WHERE Id = $id;";
            write.Parameters.AddWithValue("$sortOrder", position);
            write.Parameters.AddWithValue("$updatedAt", Timestamps.Format(updatedAt));
            write.Parameters.AddWithValue("$id", id);
            write.ExecuteNonQuery();

            position++;
        }
    }

    private static double ReadSortOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SortOrderScope scope,
        string id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT SortOrder FROM {scope.Table} WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        object? result = command.ExecuteScalar();

        return result is null or DBNull ? 0 : ToDouble(result);
    }

    private static double ToDouble(object value) =>
        Convert.ToDouble(value, CultureInfo.InvariantCulture);
}
