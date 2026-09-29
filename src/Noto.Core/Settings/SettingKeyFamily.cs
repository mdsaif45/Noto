namespace Noto.Core.Settings;

/// <summary>
/// A declared set of settings that share a name, a type, a default and a
/// validity rule, and differ only by a scope.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> #16 remembers the workspace width per display, and a
/// display is not known when the code is written, so its setting cannot be a
/// <c>static readonly</c> field on <see cref="SettingKeys"/>. A family is the
/// declared form of that: the <i>prefix</i> is declared once, in
/// <see cref="SettingKeys.Families"/>, and each member is
/// <c>prefix::scope</c>.
/// </para>
/// <para>
/// <b>Not an arbitrary-key escape hatch.</b> A setting is known only if it is
/// a declared static key or a member of a declared family. The store
/// materialises a row only when its name is one of those; every other row is
/// left alone, exactly as for any unknown key (#9). A family member has the
/// family's type and validity rule and no other.
/// </para>
/// <para>
/// <b><c>::</c> is reserved as the separator.</b> No static key name and no
/// family prefix may contain it, which is what guarantees a static key can
/// never be mistaken for a family member or the other way round. The registry
/// tests pin that rule; the family constructor enforces it for prefixes.
/// </para>
/// </remarks>
public abstract class SettingKeyFamily
{
    /// <summary>The text between a family's prefix and a member's scope.</summary>
    public const string Separator = "::";

    private protected SettingKeyFamily(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (prefix.Contains(Separator, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A family prefix may not contain '{Separator}'.", nameof(prefix));
        }

        Prefix = prefix;
    }

    /// <summary>The declared name every member begins with.</summary>
    public string Prefix { get; }

    /// <summary>
    /// The member a stored name denotes, or <see langword="null"/> when the
    /// name does not belong to this family.
    /// </summary>
    /// <remarks>
    /// A name belongs to the family only when it is exactly the prefix, the
    /// separator and a non-blank scope. <c>prefix::</c> with nothing after it
    /// is not a member: it is an unknown key.
    /// </remarks>
    public abstract SettingKey? MemberNamed(string name);

    /// <summary>Whether a key is a member of this family.</summary>
    public bool Owns(SettingKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return ScopeOf(key.Name) is not null && key.Kind == Kind && key.ValueType == ValueType;
    }

    /// <summary>Which of the five supported value types every member holds.</summary>
    public abstract SettingValueKind Kind { get; }

    /// <summary>The CLR type every member's value has.</summary>
    public abstract Type ValueType { get; }

    public override string ToString() => Prefix + Separator + "*";

    /// <summary>The scope a name carries, or <see langword="null"/> if the name is not a member's.</summary>
    private protected string? ScopeOf(string name)
    {
        string head = Prefix + Separator;

        if (!name.StartsWith(head, StringComparison.Ordinal))
        {
            return null;
        }

        string scope = name[head.Length..];

        return string.IsNullOrWhiteSpace(scope) ? null : scope;
    }
}

/// <summary>
/// A typed <see cref="SettingKeyFamily"/>.
/// </summary>
/// <typeparam name="T">
/// One of the types <see cref="SettingKey{T}"/> supports.
/// </typeparam>
public sealed class SettingKeyFamily<T> : SettingKeyFamily
{
    private readonly T _default;
    private readonly Func<T, bool>? _isValid;
    private readonly SettingKey<T> _template;

    /// <summary>
    /// Declares a family.
    /// </summary>
    /// <param name="prefix">The shared name. Not blank; may not contain <see cref="SettingKeyFamily.Separator"/>.</param>
    /// <param name="defaultValue">
    /// What every member's <see cref="SettingKey{T}.Default"/> is. It must
    /// satisfy <paramref name="isValid"/>, as for any key. A caller that needs
    /// to know whether a member is actually stored uses
    /// <see cref="ISettingsStore.TryRead{T}"/>, never a sentinel default.
    /// </param>
    /// <param name="isValid">What counts as a legal value, as for <see cref="SettingKey{T}"/>.</param>
    public SettingKeyFamily(string prefix, T defaultValue, Func<T, bool>? isValid = null)
        : base(prefix)
    {
        // Built once, so an unsupported type or a default that breaks its own
        // rule fails here, at declaration, rather than at the first member.
        _template = new SettingKey<T>(prefix, defaultValue, isValid);
        _default = defaultValue;
        _isValid = isValid;
    }

    public override SettingValueKind Kind => _template.Kind;

    public override Type ValueType => typeof(T);

    /// <summary>
    /// The member for one scope: <c>prefix::scope</c>.
    /// </summary>
    /// <remarks>
    /// Deterministic: the same scope always names the same setting, so two
    /// instances returned for one scope read and write the same row. The scope
    /// is opaque to the family and stored exactly as given.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is blank.</exception>
    public SettingKey<T> For(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        return new SettingKey<T>(Prefix + Separator + scope, _default, _isValid);
    }

    public override SettingKey? MemberNamed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        string? scope = ScopeOf(name);

        return scope is null ? null : For(scope);
    }
}
