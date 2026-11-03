using System.Text.RegularExpressions;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Links' wiring (ADR-019 §2, §3), checked in source as the other surfaces' are. What a link is, is Core's
/// (<c>LinkTargetTests</c>); what opening does, Platform's (<c>ShellLaunchTests</c>); at run time, Invoke-Links.ps1's.
/// </summary>
public sealed class LinkGuardTests
{
    private static readonly string Windows = Path.GetDirectoryName(SourcePath("src", "Noto.Windows", "App.xaml.cs"))!;
    private static readonly string Host = Code(Path.Join(Windows, "NoteEditorHost.xaml.cs"));
    private static readonly string Main = Code(Path.Join(Windows, "MainWindow.xaml.cs"));
    private static readonly string Links = Code(Path.Join(Windows, "MainWindow.Links.cs"));
    private static readonly string ReadView = Code(Path.Join(Windows, "MarkdownReadView.cs"));

    [Fact]
    public void Ctrl_Click_and_Ctrl_Enter_open_a_link_and_a_plain_click_only_places_the_caret()
    {
        Assert.Matches(@"if \(IsDown\(VirtualKey\.Control\) && LinkTarget\.At\(_document, at\) is \{ \} destination\)\s*\{\s*LinkOpenRequested\?\.Invoke\(this, destination\);\s*return;", Body(Host, "OnPointerReleased"));
        Assert.Matches(@"if \(e\.Key == VirtualKey\.Enter && ctrl && !alt && !shift\)\s*\{\s*Flush\(\);\s*if \(LinkTarget\.At\(_document, _selection\.Active\) is \{ \} destination\)\s*\{\s*e\.Handled = true;\s*LinkOpenRequested\?\.Invoke\(this, destination\);", Host);
    }

    [Fact]
    public void Paste_prefers_copied_files_then_a_web_address_over_a_selection()
    {
        string paste = Body(Host, "PasteAsync");
        Assert.Matches(@"TextEdit\? edit = paths\.Count > 0 \? MarkdownCommands\.LinkPaths\(_text, at, paths\)\s*: plain is not null \? MarkdownCommands\.LinkSelection\(_text, at, plain\)\s*: null;", paste);
        Assert.Contains(".Where(Path.IsPathFullyQualified)", paste, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_link_opens_through_one_checked_path_that_asks_before_a_program()
    {
        Assert.Contains("host.LinkOpenRequested += OnEditorLinkOpenRequested;", Main, StringComparison.Ordinal);
        Assert.Contains("MarkdownReadView.OpenLink = QueueOpenLink;", Main, StringComparison.Ordinal);

        // Never inside an input handler: the launch runs after the input that asked for it (measured: a stuck UI thread).
        Assert.Contains("private void OnEditorLinkOpenRequested(object? sender, string destination) => QueueOpenLink(destination);", Links, StringComparison.Ordinal);
        Assert.Contains("internal void QueueOpenLink(string destination) => _ = DispatcherQueue.TryEnqueueGuarded(() => OpenLink(destination));", Links, StringComparison.Ordinal);
        Assert.Contains("hyperlink.Click += (_, _) => OpenLink?.Invoke(destination);", ReadView, StringComparison.Ordinal);

        string open = Body(Links, "OpenLink");
        Assert.Contains("LinkCheck check = ShellLaunch.Inspect(destination);", open, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close,", open, StringComparison.Ordinal);
        Assert.Matches(@"confirmed = await dialog\.ShowAsync\(\) == ContentDialogResult\.Primary;\s*if \(!confirmed\)\s*\{\s*return;", open);
        Assert.Contains("switch (ShellLaunch.OpenLink(destination, confirmed))", open, StringComparison.Ordinal);

        // No other way to the shell from the windows layer.
        string all = string.Join('\n', Directory.EnumerateFiles(Windows, "*.cs").Select(Code));
        Assert.DoesNotMatch(@"Process\.Start|Launcher\.Launch", all);
        Assert.Equal(1, Regex.Count(all, @"ShellLaunch\.OpenLink\("));
    }

    [Fact]
    public void A_card_link_is_never_a_tab_stop()
    {
        // The stack's keys stay the stack's; the keyboard opens a link from the editor (Ctrl+Enter).
        Assert.Contains("new Hyperlink { IsTabStop = false, UnderlineStyle = UnderlineStyle.Single }", ReadView, StringComparison.Ordinal);
    }

    private static string Code(string path) => Regex.Replace(File.ReadAllText(path), @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string Body(string source, string method)
    {
        var declaration = Regex.Match(source, $@"(private|internal|public)(?: static)?(?: async)? [\w<>?,\[\] ]+? {method}\(");
        Assert.True(declaration.Success, $"Not found: {method}");
        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            depth += source[i] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced body for {method}");
    }

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
