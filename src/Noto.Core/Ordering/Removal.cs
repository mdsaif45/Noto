namespace Noto.Core.Ordering;

/// <summary>Where focus goes when an item leaves a list (workspace-management.md, "B5 precisely").</summary>
public static class Removal
{
    /// <summary>
    /// The item after <paramref name="removed"/> in <paramref name="items"/>, else the one before it; <see langword="null"/>
    /// when it was the only one, or is not in the list.
    /// </summary>
    public static TId? Next<T, TId>(IReadOnlyList<T> items, TId removed, Func<T, TId> id)
        where TId : struct, IEquatable<TId>
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(id);

        for (int i = 0; i < items.Count; i++)
        {
            if (!id(items[i]).Equals(removed))
            {
                continue;
            }

            if (i + 1 < items.Count)
            {
                return id(items[i + 1]);
            }

            return i > 0 ? id(items[i - 1]) : null;
        }

        return null;
    }
}
