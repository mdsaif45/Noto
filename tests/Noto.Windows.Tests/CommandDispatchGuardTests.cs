using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// #21 in the source: the window layer mutates only by dispatching, the composition root registers every handler it
/// builds, a subscriber's failure reaches the log, and the dispatcher is built without reflection. Checked in the
/// source, as the other wiring guards are; the behaviour itself is tested in Noto.UseCases.Tests.
/// </summary>
public sealed class CommandDispatchGuardTests
{
    private static readonly string AppDirectory = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string App = WithoutComments(File.ReadAllText(Path.Join(AppDirectory, "App.xaml.cs")));

    [Fact]
    public void The_window_layer_never_calls_a_command_handler_directly()
    {
        // A mutation is a Dispatch; a handler's Handle appears only as a method group passed to Register.
        var direct = Directory.EnumerateFiles(AppDirectory, "*.cs")
            .Where(f => Regex.IsMatch(WithoutComments(File.ReadAllText(f)), @"\.Handle\("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(direct.Count == 0, $"Direct handler calls in: {string.Join(", ", direct)}");
    }

    [Fact]
    public void Every_handler_the_composition_root_builds_is_registered()
    {
        int built = Regex.Count(App, @"new \w+Handler\(");
        int registered = Regex.Count(App, @"\.Register<[^(]+>\(\s*new \w+Handler\(");

        Assert.True(built > 0, "No command handler is built in the composition root.");
        Assert.Equal(built, registered);
        Assert.Contains("new CommandDispatcher(commands, events)", App, StringComparison.Ordinal);
    }

    [Fact]
    public void Creating_a_note_is_announced_as_NoteCreated()
    {
        Assert.Matches(
            @"\.Register<CreateNote, CommandResult<NoteId>, NoteCreated>\(\s*new CreateNoteHandler\(notes, clock\)\.Handle,\s*static \(command, result\) => new NoteCreated\(result\.Value, command\.FolderId\)\)",
            App);
    }

    [Fact]
    public void A_subscribers_failure_is_written_to_the_application_log()
    {
        Assert.Contains("new EventHub(failure => log.EventSubscriberFailed(failure))", App, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registry_dispatcher_and_event_hub_use_no_reflection()
    {
        string[] forbidden = ["System.Reflection", "Activator", "GetMethod", "GetTypes", "MakeGenericType", "Assembly", "DynamicInvoke", "BindingFlags"];
        string commands = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.GetDirectoryName(SourcePath("src", "Noto.UseCases", "Commands", "CommandDispatcher.cs"))!, "*.cs")
                .Select(f => WithoutComments(File.ReadAllText(f))));

        var found = forbidden.Where(token => commands.Contains(token, StringComparison.Ordinal)).ToList();

        Assert.True(found.Count == 0, $"Reflection in Noto.UseCases.Commands: {string.Join(", ", found)}");
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
