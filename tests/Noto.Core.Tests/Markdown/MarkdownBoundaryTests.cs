using System.Reflection;
using Noto.Core.Markdown;
using Xunit;

namespace Noto.Core.Tests.Markdown;

/// <summary>
/// Invariant I8: the Markdown model is a description in plain types. The assembly-wide guards (ArchitectureBoundaryTests)
/// already keep WinUI out of Core; these keep the model's own surface free of editor, rendering and threading concepts,
/// and free of any text output that could rewrite a note.
/// </summary>
public sealed class MarkdownBoundaryTests
{
    private static readonly string[] Forbidden = ["Html", "Rtf", "RichEdit", "TextRange", "Automation", "Dispatcher", "Xaml", "Render", "Format", "Thread"];

    private static readonly Type[] Model = [.. typeof(MarkdownDocument).Assembly.GetExportedTypes().Where(t => t.Namespace == "Noto.Core.Markdown")];

    [Fact]
    public void The_model_names_no_editor_rendering_or_threading_concept()
    {
        var violations =
            from type in Model
            from name in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).Append(type.Name)
            from word in Forbidden
            where name.Contains(word, StringComparison.OrdinalIgnoreCase)
            select $"{type.Name}.{name} ({word})";

        Assert.Empty(violations);
    }

    [Fact]
    public void The_model_has_no_way_to_produce_text_so_parsing_cannot_rewrite_a_note()
    {
        // Source is the parsed string itself; no other public member returns a string (records' ToString aside).
        var textOut =
            from type in Model
            from method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            where method.ReturnType == typeof(string) && method.Name is not ("ToString" or "get_Source")
            select $"{type.Name}.{method.Name}";

        Assert.Empty(textOut);
        Assert.Equal(["MarkdownBlock", "MarkdownBlockKind", "MarkdownDocument", "MarkdownSpan", "MarkdownSpanKind"], Model.Select(t => t.Name).Order());
    }
}
