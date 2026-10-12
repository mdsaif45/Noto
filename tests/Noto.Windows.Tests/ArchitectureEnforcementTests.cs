using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The architecture rules (#20) that span every layer, checked against the compiled assemblies of the built
/// application — the code that actually runs — rather than against its source.
/// </summary>
/// <remarks>
/// <para>
/// This project cannot reference the WinUI application, so the rules read the IL of the application's own build
/// output with <c>System.Reflection.Metadata</c>, loading nothing. A rule that silently checked a stale build would
/// be worse than none: every assembly read must be newer than its project's sources, or the test fails and says so.
/// </para>
/// </remarks>
public sealed class ArchitectureEnforcementTests
{
    /// <summary>A repository method whose name starts with one of these reads; every other one changes state.</summary>
    private static readonly string[] ReadPrefixes = ["Find", "Get", "List", "Max", "Is", "Count", "Exists", "Has", "Contains"];

    /// <summary>Every layer whose code may hold a repository. Infrastructure is not here: it implements the writes.</summary>
    private static readonly string[] CallerLayers = ["Noto.Core", "Noto.UseCases", "Noto.Platform.Windows", "Noto.UI", "Noto"];

    [Fact]
    public void Repository_writes_are_reached_only_from_command_handlers()
    {
        var app = BuiltApplication.Load();
        var writes = app.RepositoryWrites();
        var handlers = app.CommandHandlers();

        // The rule is only as good as the scan: it must see the writes the handlers make.
        Assert.Contains("SoftDelete", writes["Noto.Core.Notes.INoteRepository"]);
        Assert.Contains("Noto.UseCases.Notes.DeleteNoteHandler", handlers);

        var calls = CallerLayers
            .SelectMany(app.CallsIn)
            .Where(c => writes.TryGetValue(c.CalleeType, out var names) && names.Contains(c.CalleeMethod))
            .ToList();

        Assert.True(
            calls.Any(c => c.CallerType == "Noto.UseCases.Notes.DeleteNoteHandler" && c.CalleeMethod == "SoftDelete"),
            "The scan found no repository write from DeleteNoteHandler, so it is not reading calls correctly.");

        var violations = calls
            .Where(c => !(c.CallerAssembly == "Noto.UseCases" && handlers.Contains(c.CallerType)))
            .Select(c => $"{c.CallerAssembly}: {c.CallerType}.{c.CallerMethod} -> {c.CalleeType}.{c.CalleeMethod}")
            .Distinct()
            .Order()
            .ToList();

        Assert.True(
            violations.Count == 0,
            "ADR-010: every mutation goes through a command. A repository write is reachable only from a command "
            + "handler: a Noto.UseCases type with a public Handle method returning CommandResult. Queries, views, "
            + "the composition root and helpers read, or dispatch a command. Found:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void The_design_system_references_no_other_layer()
    {
        // Noto.UI holds the design tokens and styles (ADR-011). The application depends on it; it depends on no
        // layer of Noto. Presentation logic lives in the application until a Noto.Presentation project exists.
        string[] forbidden = ["Noto", "Noto.Core", "Noto.UseCases", "Noto.Infrastructure", "Noto.Platform.Windows"];

        var violations = BuiltApplication.Load().ReferencesOf("Noto.UI")
            .Where(name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Noto.UI must not reference another Noto layer: {string.Join(", ", violations)}.");
    }

    /// <summary>One call or method-pointer load found in IL.</summary>
    internal sealed record Call(string CallerAssembly, string CallerType, string CallerMethod, string CalleeType, string CalleeMethod);

    /// <summary>The built application's assemblies, read as metadata.</summary>
    internal sealed class BuiltApplication
    {
        private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => (ushort)o.Value);

        private readonly Dictionary<string, (PEReader Pe, MetadataReader Md)> _assemblies;

        private BuiltApplication(Dictionary<string, (PEReader, MetadataReader)> assemblies) => _assemblies = assemblies;

        internal static BuiltApplication Load()
        {
            string root = RepositoryRoot();
            string configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
            string output = Path.Join(root, "src", "Noto.Windows", "bin", configuration, "net9.0-windows10.0.19041.0", "win-x64");

            var assemblies = new Dictionary<string, (PEReader, MetadataReader)>();

            foreach (var (name, project) in new[] { ("Noto", "Noto.Windows"), ("Noto.Core", "Noto.Core"), ("Noto.UseCases", "Noto.UseCases"), ("Noto.Infrastructure", "Noto.Infrastructure"), ("Noto.Platform.Windows", "Noto.Platform.Windows"), ("Noto.UI", "Noto.UI") })
            {
                string dll = Path.Join(output, name + ".dll");

                if (!File.Exists(dll))
                {
                    throw new InvalidOperationException($"{dll} is missing: build the solution in {configuration} first.");
                }

                DateTime built = File.GetLastWriteTimeUtc(dll);
                var newer = Directory.EnumerateFiles(Path.Join(root, "src", project), "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(f => File.GetLastWriteTimeUtc(f) > built);

                if (newer is not null)
                {
                    throw new InvalidOperationException($"{dll} is older than {newer}: build the solution in {configuration} before running this test, or it would check stale code.");
                }

                var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(dll)));
                assemblies[name] = (pe, pe.GetMetadataReader());
            }

            return new BuiltApplication(assemblies);
        }

        internal IEnumerable<string> ReferencesOf(string assembly)
        {
            var md = _assemblies[assembly].Md;
            return md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name));
        }

        /// <summary>Each repository interface in Core, and each Infrastructure type implementing one, with its write methods.</summary>
        internal Dictionary<string, HashSet<string>> RepositoryWrites()
        {
            var core = _assemblies["Noto.Core"].Md;
            var writes = new Dictionary<string, HashSet<string>>();

            foreach (var h in core.TypeDefinitions)
            {
                var type = core.GetTypeDefinition(h);
                string name = core.GetString(type.Name);

                if ((type.Attributes & System.Reflection.TypeAttributes.Interface) != 0 && name.StartsWith('I') && name.EndsWith("Repository", StringComparison.Ordinal))
                {
                    writes[FullName(core, h)] = type.GetMethods()
                        .Select(m => core.GetString(core.GetMethodDefinition(m).Name))
                        .Where(m => !ReadPrefixes.Any(p => m.StartsWith(p, StringComparison.Ordinal)))
                        .ToHashSet();
                }
            }

            var infrastructure = _assemblies["Noto.Infrastructure"].Md;

            foreach (var h in infrastructure.TypeDefinitions)
            {
                var type = infrastructure.GetTypeDefinition(h);
                var implemented = type.GetInterfaceImplementations()
                    .Select(i => TypeName(infrastructure, infrastructure.GetInterfaceImplementation(i).Interface))
                    .Where(writes.ContainsKey)
                    .ToList();

                if (implemented.Count > 0)
                {
                    writes[FullName(infrastructure, h)] = implemented.SelectMany(i => writes[i]).ToHashSet();
                }
            }

            return writes;
        }

        /// <summary>The command handlers: Noto.UseCases types with a public Handle method returning Noto.Core.Commands.CommandResult.</summary>
        internal HashSet<string> CommandHandlers()
        {
            var md = _assemblies["Noto.UseCases"].Md;
            var handlers = new HashSet<string>();

            foreach (var h in md.TypeDefinitions)
            {
                foreach (var m in md.GetTypeDefinition(h).GetMethods())
                {
                    var method = md.GetMethodDefinition(m);

                    if (md.GetString(method.Name) == "Handle" && (method.Attributes & System.Reflection.MethodAttributes.Public) == System.Reflection.MethodAttributes.Public
                        && method.DecodeSignature(new Names(md), null).ReturnType.StartsWith("Noto.Core.Commands.CommandResult", StringComparison.Ordinal))
                    {
                        handlers.Add(FullName(md, h));
                    }
                }
            }

            return handlers;
        }

        /// <summary>Every call, virtual call and method-pointer load in an assembly's IL, with the type that makes it.</summary>
        internal IEnumerable<Call> CallsIn(string assembly)
        {
            var (pe, md) = _assemblies[assembly];

            foreach (var h in md.TypeDefinitions)
            {
                string caller = FullName(md, OuterType(md, h));

                foreach (var m in md.GetTypeDefinition(h).GetMethods())
                {
                    var method = md.GetMethodDefinition(m);

                    if (method.RelativeVirtualAddress == 0)
                    {
                        continue;
                    }

                    var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();

                    while (il.RemainingBytes > 0)
                    {
                        int at = il.Offset;
                        byte first = il.ReadByte();
                        ushort value = first == 0xFE ? (ushort)(0xFE00 | il.ReadByte()) : first;

                        if (!OpCodesByValue.TryGetValue(value, out var op))
                        {
                            throw new InvalidOperationException($"Unknown opcode 0x{value:X} at IL_{at:X4} in {assembly}: {caller}.{md.GetString(method.Name)}.");
                        }

                        if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn)
                        {
                            var (type, name) = Callee(md, MetadataTokens.EntityHandle(il.ReadInt32()));
                            yield return new Call(assembly, caller, md.GetString(method.Name), type, name);
                        }
                        else if (op.OperandType == OperandType.InlineSwitch)
                        {
                            // Not "Offset += 4 * ReadInt32()": that reads Offset before the count is consumed.
                            int targets = il.ReadInt32();
                            il.Offset += 4 * targets;
                        }
                        else
                        {
                            il.Offset += op.OperandType switch
                            {
                                OperandType.InlineNone => 0,
                                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                                OperandType.InlineVar => 2,
                                OperandType.InlineI8 or OperandType.InlineR => 8,
                                _ => 4,
                            };
                        }
                    }
                }
            }
        }

        private static (string Type, string Name) Callee(MetadataReader md, EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    var definition = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    return (FullName(md, definition.GetDeclaringType()), md.GetString(definition.Name));
                case HandleKind.MemberReference:
                    var reference = md.GetMemberReference((MemberReferenceHandle)handle);
                    return (TypeName(md, reference.Parent), md.GetString(reference.Name));
                case HandleKind.MethodSpecification:
                    return Callee(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
                default:
                    return (string.Empty, string.Empty);
            }
        }

        /// <summary>Compiler-generated types (lambdas, closures, iterators, async) are attributed to the type that wrote them.</summary>
        private static TypeDefinitionHandle OuterType(MetadataReader md, TypeDefinitionHandle h)
        {
            var type = md.GetTypeDefinition(h);

            while (md.GetString(type.Name).StartsWith('<') && type.IsNested)
            {
                h = type.GetDeclaringType();
                type = md.GetTypeDefinition(h);
            }

            return h;
        }

        private static string TypeName(MetadataReader md, EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => FullName(md, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => ReferenceName(md, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(new Names(md), null),
            _ => string.Empty,
        };

        private static string FullName(MetadataReader md, TypeDefinitionHandle h)
        {
            var type = md.GetTypeDefinition(h);
            string name = md.GetString(type.Name);
            return type.IsNested ? $"{FullName(md, type.GetDeclaringType())}+{name}" : Join(md.GetString(type.Namespace), name);
        }

        private static string ReferenceName(MetadataReader md, TypeReferenceHandle h)
        {
            var type = md.GetTypeReference(h);
            string name = md.GetString(type.Name);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? $"{ReferenceName(md, (TypeReferenceHandle)type.ResolutionScope)}+{name}"
                : Join(md.GetString(type.Namespace), name);
        }

        private static string Join(string ns, string name) => ns.Length == 0 ? name : $"{ns}.{name}";

        /// <summary>Just enough of a signature decoder to name a type: generic instances keep their definition's name.</summary>
        private sealed class Names(MetadataReader md) : ISignatureTypeProvider<string, object?>
        {
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FullName(reader, handle);
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => ReferenceName(reader, handle);
            public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => TypeName(md, handle);
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[,]";
            public string GetByReferenceType(string elementType) => elementType + "&";
            public string GetPointerType(string elementType) => elementType + "*";
            public string GetPinnedType(string elementType) => elementType;
            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
            public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
            public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
            public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        }
    }

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "Noto.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Could not locate Noto.sln by walking up from {AppContext.BaseDirectory}");
    }
}
