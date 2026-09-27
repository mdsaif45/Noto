namespace Noto.Core.Settings;

/// <summary>
/// The value types a setting may hold.
/// </summary>
/// <remarks>
/// A closed set, deliberately. Every member has one documented text form, so
/// the round trip through the <c>Settings</c> table's <c>TEXT</c> column is
/// exact and culture-independent. Widening it means deciding a text form
/// first, not discovering one at the call site.
/// </remarks>
public enum SettingValueKind
{
    /// <summary>Stored as <c>True</c> or <c>False</c>.</summary>
    Boolean,

    /// <summary>A 32-bit whole number, invariant culture.</summary>
    Whole,

    /// <summary>A double, round-trip format, invariant culture.</summary>
    Real,

    /// <summary>Stored verbatim.</summary>
    Text,

    /// <summary>Stored as the member's name, never its number.</summary>
    Enumeration,
}

/// <summary>
/// Names a setting, its type, its default, and what counts as a legal value.
/// </summary>
/// <remarks>
/// <para>
/// The non-generic half of <see cref="SettingKey{T}"/>. It exists so the
/// registry can be enumerated and the store can hold keys of different value
/// types in one collection, which a generic-only design cannot do without
/// casting through <see cref="object"/> at every call.
/// </para>
/// <para>
/// <b>There is no <c>Read(string)</c> anywhere in this contract.</b> A setting
/// is reachable only through a <see cref="SettingKey{T}"/> that some code
/// declared, which is what keeps this a typed contract rather than a
/// configuration bag: a mistyped name does not compile, and a key nobody
/// declared cannot be read at all.
/// </para>
/// </remarks>
public abstract class SettingKey
{
    private protected SettingKey(string name, SettingValueKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Kind = kind;
    }

    /// <summary>The key as stored in the <c>Settings</c> table.</summary>
    public string Name { get; }

    /// <summary>Which of the five supported value types this key holds.</summary>
    public SettingValueKind Kind { get; }

    /// <summary>
    /// Whether an already-parsed value is legal for this key.
    /// </summary>
    /// <remarks>
    /// Exists so the store can apply a key's validity rule while holding the
    /// key non-generically, which it must do to load a set of differently
    /// typed keys in one pass. The alternative — reflecting onto
    /// <see cref="SettingKey{T}.IsValid"/> — would put a reflective call on
    /// the startup path to reach a rule the key can simply answer itself.
    /// </remarks>
    public abstract bool IsValidValue(object value);

    /// <summary>The CLR type this key's value has.</summary>
    public abstract Type ValueType { get; }

    public override string ToString() => Name;
}

/// <summary>
/// A typed setting: its name, its default, and its validity rule.
/// </summary>
/// <typeparam name="T">
/// One of <see cref="bool"/>, <see cref="int"/>, <see cref="double"/>,
/// <see cref="string"/>, or an enum. Anything else throws at construction
/// rather than failing later at a call site.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Every key carries a default, so a read can never fail.</b> Absence from
/// the table is not an error state to handle — it is the normal way a setting
/// the user has never touched is represented (#9: "a missing key is normal,
/// not an error"). That is also why there is no <c>Default</c> column: the
/// default lives here, in one place, and a column would be a second source of
/// truth for the same value.
/// </para>
/// <para>
/// Declared as <c>static readonly</c> fields on <see cref="SettingKeys"/>, in
/// the shape of <see cref="Folders.FolderId"/> and the other value objects —
/// a small type that makes an illegal state unrepresentable rather than a
/// convention someone has to remember.
/// </para>
/// </remarks>
public sealed class SettingKey<T> : SettingKey
{
    private readonly Func<T, bool>? _isValid;

    /// <summary>
    /// Declares a setting.
    /// </summary>
    /// <param name="name">The stored key. Must not be blank.</param>
    /// <param name="defaultValue">
    /// The value a read returns when the key is absent, unparseable or invalid.
    /// It must itself satisfy <paramref name="isValid"/>; a default outside its
    /// own valid range would make every fallback produce an illegal value.
    /// </param>
    /// <param name="isValid">
    /// What counts as a legal value, or <see langword="null"/> when every value
    /// of <typeparamref name="T"/> is legal.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> is not one of the five supported types.
    /// </exception>
    public SettingKey(string name, T defaultValue, Func<T, bool>? isValid = null)
        : base(name, KindOf())
    {
        ArgumentNullException.ThrowIfNull(defaultValue);

        if (isValid is not null && !isValid(defaultValue))
        {
            throw new ArgumentException(
                $"The default for '{name}' does not satisfy its own validity rule.",
                nameof(defaultValue));
        }

        Default = defaultValue;
        _isValid = isValid;
    }

    /// <summary>What a read returns when nothing usable is stored.</summary>
    public T Default { get; }

    /// <summary>Whether a value is legal for this key.</summary>
    public bool IsValid(T value) => _isValid is null || _isValid(value);

    /// <inheritdoc />
    public override bool IsValidValue(object value) => value is T typed && IsValid(typed);

    /// <summary>
    /// The enum type this key holds, for parsing its stored name.
    /// </summary>
    /// <remarks>
    /// Only meaningful when <see cref="SettingKey.Kind"/> is
    /// <see cref="SettingValueKind.Enumeration"/>. Exposed here because
    /// <typeparamref name="T"/> is not reachable from the non-generic base,
    /// and the store parses while holding the base type.
    /// </remarks>
    public override Type ValueType => typeof(T);

    private static SettingValueKind KindOf()
    {
        if (typeof(T) == typeof(bool))
        {
            return SettingValueKind.Boolean;
        }

        if (typeof(T) == typeof(int))
        {
            return SettingValueKind.Whole;
        }

        if (typeof(T) == typeof(double))
        {
            return SettingValueKind.Real;
        }

        if (typeof(T) == typeof(string))
        {
            return SettingValueKind.Text;
        }

        if (typeof(T).IsEnum)
        {
            return SettingValueKind.Enumeration;
        }

        throw new NotSupportedException(
            $"'{typeof(T).Name}' is not a supported setting type. "
            + "Supported: bool, int, double, string, enum.");
    }
}
