using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #11: the performance harness times a save from the <c>Noto.Save completed</c> trace, so the trace must mean what it
/// says: emitted once the save has been written, only on success, never on a failed save, and carrying nothing but the
/// duration. Checked in the source, as the other wiring guards are; the XAML layer has no unit tests.
/// </summary>
public sealed class SaveTraceGuardTests
{
    private const string Signal = "Noto.Save completed";

    private static readonly string WindowSource = WithoutComments(File.ReadAllText(SourcePath("src", "Noto.Windows", "MainWindow.xaml.cs")));

    [Fact]
    public void The_save_trace_is_written_after_the_save_returns_in_the_success_branch_only()
    {
        string save = Body(WindowSource, "TrySaveOpenNote");

        int call = Index(save, "new UpdateNoteContent(");
        int success = Index(save, "if (result.IsSuccess)");
        int baseline = Index(save, "_openNoteBaseline = NoteEditor.Text;");
        int signal = Index(save, Signal);
        int saved = Index(save, "return true;");

        Assert.True(call < success && success < baseline && baseline < signal && signal < saved, $"call={call} success={success} baseline={baseline} signal={signal} saved={saved}");

        // Everything after the success branch is a failure: a failure result or a storage exception. No signal there.
        string failures = save[saved..];
        Assert.DoesNotContain("Noto.Save", failures, StringComparison.Ordinal);
    }

    [Fact]
    public void The_save_trace_appears_once_in_the_window_layer_and_carries_only_the_duration()
    {
        string all = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!, "*.cs")
                .Select(f => WithoutComments(File.ReadAllText(f))));

        Assert.Equal(1, Regex.Count(all, Regex.Escape(Signal)));
        Assert.Contains("Trace.WriteLine($\"Noto.Save completed ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}\");", WindowSource, StringComparison.Ordinal);
    }

    private static int Index(string text, string value)
    {
        int index = text.IndexOf(value, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Not found: {value}");
        return index;
    }

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"private \w+ {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] == '{' ? 1 : source[i] == '}' ? -1 : 0;

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

    private static string WithoutComments(string source) => Regex.Replace(source, @"//.*", string.Empty);

    /// <summary>Walks up to the repository root; a guard that silently skips is worse than none.</summary>
    private static string SourcePath(params string[] parts)
    {
        string relative = Path.Join(parts);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Join(directory.FullName, relative);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {relative} by walking up from {AppContext.BaseDirectory}");
    }
}
