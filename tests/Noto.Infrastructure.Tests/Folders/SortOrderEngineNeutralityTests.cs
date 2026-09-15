using System.Reflection;
using Xunit;

namespace Noto.Infrastructure.Tests.Folders;

/// <summary>
/// The ordering engine must stay free of any one domain.
/// </summary>
/// <remarks>
/// <para>
/// The engine was extracted so notes and folders could share one implementation
/// of O1–O6. That only holds while it knows about neither. The easiest way to
/// break it is also the most tempting: a single <c>NoteId</c> parameter, or a
/// <c>"Notes"</c> literal in one SQL string, added because it was convenient
/// for the call site at hand.
/// </para>
/// <para>
/// Checked two ways, because neither alone is sufficient. Metadata catches a
/// domain type in a signature and survives reformatting; the source scan
/// catches a table name or a domain type used only inside a method body, which
/// metadata cannot see. The scan is confined to the engine's own file — the
/// note repository shares this assembly and legitimately contains
/// <c>"FROM Notes"</c>, so an assembly-wide search would report a violation
/// that is not one.
/// </para>
/// </remarks>
public sealed class SortOrderEngineNeutralityTests
{
    private static readonly Type Engine =
        typeof(Noto.Infrastructure.Storage.NotoDatabase).Assembly
            .GetType("Noto.Infrastructure.Storage.SortOrderEngine")
            ?? throw new InvalidOperationException("SortOrderEngine was not found.");

    [Fact]
    public void The_engine_exists_and_is_internal()
    {
        // It is an implementation detail of this assembly. Making it public
        // would invite a caller outside the repositories, which is how the
        // "one place ordering values are produced" property gets lost.
        Assert.False(Engine.IsPublic);
        Assert.True(Engine.IsAbstract && Engine.IsSealed, "The engine should be a static class.");
    }

    [Fact]
    public void No_engine_signature_mentions_a_domain_type()
    {
        // NoteId, FolderId, Note, Folder, NotePlacement, FolderPlacement —
        // anything from Noto.Core's domain namespaces. The engine takes plain
        // row ids as strings; translation belongs at the repository boundary.
        var offenders = new List<string>();

        foreach (MethodInfo method in Engine.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (Type type in method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType))
            {
                if (IsDomainType(type))
                {
                    offenders.Add($"{method.Name} -> {type.FullName}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "SortOrderEngine must not name a domain type: " + string.Join("; ", offenders));
    }

    [Fact]
    public void No_engine_field_holds_a_domain_type()
    {
        var offenders = Engine
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => IsDomainType(f.FieldType))
            .Select(f => f.Name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "SortOrderEngine must not hold a domain type: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_engine_hard_codes_no_table_name()
    {
        // The table arrives through SortOrderScope. A "Notes" or "Folders"
        // literal inside the engine means one collection has been
        // special-cased, which is exactly the drift the extraction prevents.
        //
        // Scoped to the engine's own source file. Scanning the compiled
        // assembly instead would be meaningless: SqliteNoteRepository lives in
        // the same DLL and legitimately contains "FROM Notes", so an
        // image-wide search reports a violation that is not one.
        string source = EngineSourcePath();

        string[] offenders = File.ReadLines(source)
            .Select((line, index) => (Text: line, Number: index + 1))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(l => l.Text.Contains("\"Notes\"", StringComparison.Ordinal)
                        || l.Text.Contains("\"Folders\"", StringComparison.Ordinal)
                        || l.Text.Contains("FROM Notes", StringComparison.Ordinal)
                        || l.Text.Contains("FROM Folders", StringComparison.Ordinal))
            .Select(l => $"line {l.Number}: {l.Text.Trim()}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "SortOrderEngine must not name a table: " + string.Join("; ", offenders));
    }

    [Fact]
    public void The_engine_names_no_domain_type_in_its_source()
    {
        // The signature checks above read metadata, which cannot see a domain
        // type used only inside a method body — a cast, a local, a call to
        // NoteId.From. This closes that gap.
        string[] offenders = File.ReadLines(EngineSourcePath())
            .Select((line, index) => (Text: line, Number: index + 1))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal)
                        && !l.Text.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Where(l => l.Text.Contains("NoteId", StringComparison.Ordinal)
                        || l.Text.Contains("FolderId", StringComparison.Ordinal)
                        || l.Text.Contains("Noto.Core", StringComparison.Ordinal))
            .Select(l => $"line {l.Number}: {l.Text.Trim()}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "SortOrderEngine must not name a domain type: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// The engine's source file, located from the test assembly.
    /// </summary>
    /// <remarks>
    /// Walks up to the repository root rather than hard-coding a relative
    /// depth, so the check survives a change in build output layout. Failing
    /// loudly when the file cannot be found is deliberate: a silently skipped
    /// guard is worse than no guard.
    /// </remarks>
    private static string EngineSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src", "Noto.Infrastructure", "Storage", "SortOrderEngine.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate SortOrderEngine.cs by walking up from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void The_scope_type_carries_the_table_instead()
    {
        // The positive half: the engine is neutral BECAUSE SortOrderScope holds
        // what differs. Asserting only the absence would pass if the table were
        // hard-coded somewhere else entirely.
        Type scope = Engine.Assembly.GetType("Noto.Infrastructure.Storage.SortOrderScope")
            ?? throw new InvalidOperationException("SortOrderScope was not found.");

        Assert.NotNull(scope.GetProperty("Table", BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(scope.GetProperty("Predicate", BindingFlags.Public | BindingFlags.Instance));

        // Both collections are expressed as scopes, neither as a special case.
        Assert.NotNull(scope.GetProperty("AllFolders", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(scope.GetMethod("NotesIn", BindingFlags.Public | BindingFlags.Static));
    }

    private static bool IsDomainType(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        string? name = target.FullName;

        return name is not null
            && (name.StartsWith("Noto.Core.Notes", StringComparison.Ordinal)
                || name.StartsWith("Noto.Core.Folders", StringComparison.Ordinal)
                || name.StartsWith("Noto.Core.Tags", StringComparison.Ordinal));
    }

}
