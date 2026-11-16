using Noto.Core.Automation;
using Noto.Core.Folders;
using Xunit;
using R = Noto.Core.Automation.AutomationRequest;

namespace Noto.Core.Tests.Automation;

/// <summary>
/// The <c>noto://</c> grammar (#192, ADR-022): exactly the five requests, each only creating or showing, and every
/// other input refused rather than guessed at.
/// </summary>
public sealed class NotoUriTests
{
    private const char LoneHigh = (char)0xD800;
    private const char LoneLow = (char)0xDC00;
    private const char Bell = (char)0x07;

    public static TheoryData<string, R> Valid => new()
    {
        { "noto://new?text=hello", new R.NewNote("hello", null) },
        { "noto://new?text=hello&folder=Work", new R.NewNote("hello", "Work") },
        { "noto://new?folder=Work&text=hello", new R.NewNote("hello", "Work") },
        { "noto://new?text=", new R.NewNote(string.Empty, null) },
        { "noto://open/01HZY8M3QJ7KX6B2V9T4N5PQRS", new R.OpenFolder("01HZY8M3QJ7KX6B2V9T4N5PQRS") },
        { "noto://open/My%20Work", new R.OpenFolder("My Work") },
        { "noto://folders", new R.ShowFolders() },
        { "noto://new-folder?name=Ideas", new R.NewFolder("Ideas") },
        { "noto://search?q=milk", new R.Search("milk") },
        { "noto://search?q=", new R.Search(string.Empty) },

        // Case: scheme, verb and parameter names ignore it; values keep theirs.
        { "NOTO://NEW?TEXT=Hello", new R.NewNote("Hello", null) },
        { "Noto://Open/Work", new R.OpenFolder("Work") },
        { "noto://Search?Q=Milk", new R.Search("Milk") },

        // A trailing slash, as browsers add one.
        { "noto://folders/", new R.ShowFolders() },
        { "noto://open/Work/", new R.OpenFolder("Work") },
        { "noto://new/?text=a", new R.NewNote("a", null) },

        // Encodings: percent-escapes are UTF-8; raw characters stay; '+' is a plus; '=' inside a value is kept.
        { "noto://new?text=a%20b%0Ac", new R.NewNote("a b\nc", null) },
        { "noto://new?text=caf%C3%A9%20%E2%82%AC%20%F0%9F%98%80", new R.NewNote("café € 😀", null) },
        { "noto://new?text=café € 😀", new R.NewNote("café € 😀", null) },
        { "noto://new?text=C++", new R.NewNote("C++", null) },
        { "noto://new?text=a=b", new R.NewNote("a=b", null) },
        { "noto://new?text=50%25%26more", new R.NewNote("50%&more", null) },
        { "noto://new?text=x&&folder=Work&", new R.NewNote("x", "Work") },
        { "noto://new-folder?name=%E6%97%A5%E8%A8%98", new R.NewFolder("日記") },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void The_five_requests_are_read(string uri, R expected)
    {
        AutomationParse parsed = NotoUri.Parse(uri);

        Assert.Equal(AutomationRefusal.None, parsed.Refusal);
        Assert.Equal(expected, parsed.Request);
    }

    [Theory]
    [InlineData("noto://delete?folder=Work")]
    [InlineData("noto://move?note=1")]
    [InlineData("noto://export?folder=Work")]
    [InlineData("noto://run?cmd=calc")]
    [InlineData("noto://settings")]
    [InlineData("noto://")]
    [InlineData("noto:///folders")]
    [InlineData("noto://add-note-with-text/hello")]
    [InlineData("noto://ſearch?q=x")] // a long s would upper-case to S
    [InlineData("noto://fol%64ers")] // a verb is not percent-decoded
    public void Any_other_verb_is_refused(string uri)
    {
        Assert.Equal(new AutomationParse(null, AutomationRefusal.UnknownVerb), NotoUri.Parse(uri));
    }

    [Theory]
    [InlineData("https://example.com/", AutomationRefusal.NotNoto)]
    [InlineData("javascript:alert(1)", AutomationRefusal.NotNoto)]
    [InlineData(@"C:\Windows\System32\calc.exe", AutomationRefusal.NotNoto)]
    [InlineData("notoo://folders", AutomationRefusal.NotNoto)]
    [InlineData("noto:folders", AutomationRefusal.Malformed)]
    [InlineData("noto:/folders", AutomationRefusal.Malformed)]
    [InlineData("", AutomationRefusal.Malformed)]
    [InlineData(null, AutomationRefusal.Malformed)]
    [InlineData("noto://folders#x", AutomationRefusal.Malformed)]
    [InlineData("noto://new?text=a#b", AutomationRefusal.Malformed)]
    [InlineData("noto://new?text", AutomationRefusal.Malformed)]
    [InlineData("noto://new?=x", AutomationRefusal.Malformed)]
    [InlineData("noto://new?text=%", AutomationRefusal.Malformed)]
    [InlineData("noto://new?text=%2", AutomationRefusal.Malformed)]
    [InlineData("noto://new?text=%zz", AutomationRefusal.Malformed)]
    [InlineData("noto://open/a/b", AutomationRefusal.Malformed)]
    [InlineData("noto://folders/x", AutomationRefusal.Malformed)]
    [InlineData("noto://search/x?q=y", AutomationRefusal.Malformed)]
    [InlineData("noto://new?text=a&text=b", AutomationRefusal.UnknownArgument)]
    [InlineData("noto://new?text=a&path=C:%5C", AutomationRefusal.UnknownArgument)]
    [InlineData("noto://folders?x=1", AutomationRefusal.UnknownArgument)]
    [InlineData("noto://open/Work?x=1", AutomationRefusal.UnknownArgument)]
    [InlineData("noto://search?query=x", AutomationRefusal.UnknownArgument)]
    [InlineData("noto://new-folder?name=a&color=red", AutomationRefusal.UnknownArgument)]
    [InlineData("noto://new", AutomationRefusal.MissingArgument)]
    [InlineData("noto://new?folder=Work", AutomationRefusal.MissingArgument)]
    [InlineData("noto://open", AutomationRefusal.MissingArgument)]
    [InlineData("noto://open/", AutomationRefusal.MissingArgument)]
    [InlineData("noto://new-folder", AutomationRefusal.MissingArgument)]
    [InlineData("noto://search", AutomationRefusal.MissingArgument)]
    [InlineData("noto://new-folder?name=", AutomationRefusal.EmptyName)]
    [InlineData("noto://new-folder?name=%20%09%0A", AutomationRefusal.EmptyName)]
    [InlineData("noto://open/%20", AutomationRefusal.EmptyName)]
    [InlineData("noto://new?text=x&folder=", AutomationRefusal.EmptyName)]
    [InlineData("noto://new?text=%FF", AutomationRefusal.InvalidText)]
    [InlineData("noto://new?text=%C3", AutomationRefusal.InvalidText)]
    [InlineData("noto://new?text=%C0%AF", AutomationRefusal.InvalidText)] // an overlong '/'
    [InlineData("noto://new?text=%ED%A0%80", AutomationRefusal.InvalidText)] // an encoded surrogate
    [InlineData("noto://open/%FF", AutomationRefusal.InvalidText)]
    public void Everything_else_is_refused_with_its_reason(string? uri, AutomationRefusal reason)
    {
        Assert.Equal(new AutomationParse(null, reason), NotoUri.Parse(uri));
    }

    [Fact]
    public void A_lone_surrogate_is_refused()
    {
        // Built here, not in an attribute: attribute metadata cannot hold one.
        Assert.Equal(new AutomationParse(null, AutomationRefusal.InvalidText), NotoUri.Parse("noto://new?text=a" + LoneHigh));
        Assert.Equal(new AutomationParse(null, AutomationRefusal.InvalidText), NotoUri.Parse("noto://open/" + LoneLow));
        Assert.Equal(new AutomationParse(null, AutomationRefusal.InvalidText), NotoCommandLine.Parse(["--new-note", LoneLow + "x"]));
    }

    [Fact]
    public void Oversized_input_is_refused()
    {
        Assert.Equal(AutomationRefusal.TooLong, NotoUri.Parse("noto://new?text=" + new string('a', NotoUri.MaxLength)).Refusal);
        Assert.Equal(AutomationRefusal.TooLong, NotoUri.Parse("noto://new?text=" + new string('a', NotoUri.MaxTextLength + 1)).Refusal);
        Assert.Equal(AutomationRefusal.TooLong, NotoUri.Parse("noto://new-folder?name=" + new string('a', NotoUri.MaxNameLength + 1)).Refusal);
        Assert.Equal(AutomationRefusal.TooLong, NotoUri.Parse("noto://open/" + new string('a', NotoUri.MaxNameLength + 1)).Refusal);
        Assert.Equal(AutomationRefusal.TooLong, NotoUri.Parse("noto://search?q=" + new string('a', NotoUri.MaxNameLength + 1)).Refusal);
        Assert.Equal(AutomationRefusal.TooLong, NotoUri.Parse("noto://new?text=x&folder=" + new string('a', NotoUri.MaxNameLength + 1)).Refusal);

        // The caps themselves are allowed; escapes count once decoded.
        Assert.Equal(AutomationRefusal.None, NotoUri.Parse("noto://new?text=" + new string('a', NotoUri.MaxTextLength)).Refusal);
        Assert.Equal(AutomationRefusal.None, NotoUri.Parse("noto://new?text=" + string.Concat(Enumerable.Repeat("%0A", NotoUri.MaxTextLength))).Refusal);
        Assert.Equal(AutomationRefusal.None, NotoUri.Parse("noto://new-folder?name=" + new string('a', NotoUri.MaxNameLength)).Refusal);
    }

    [Fact]
    public void A_notes_text_loses_its_control_characters_but_keeps_newlines_and_tabs()
    {
        R? request = NotoUri.Parse("noto://new?text=a%0D%0Ab%09c%00d%1Be%7Ff%C2%85g" + Bell + "h").Request;

        Assert.Equal(new R.NewNote("a\nb\tcdefgh", null), request);
    }

    [Fact]
    public void Script_like_and_path_like_input_is_kept_as_text_never_interpreted()
    {
        Assert.Equal(
            new R.NewNote("<script>alert(1)</script> [x](javascript:alert(1))", null),
            NotoUri.Parse("noto://new?text=%3Cscript%3Ealert(1)%3C%2Fscript%3E%20[x](javascript:alert(1))").Request);

        // A path is a folder name to look up, nothing more; a name is one line.
        Assert.Equal(new R.OpenFolder(@"..\..\Windows"), NotoUri.Parse("noto://open/..%5C..%5CWindows").Request);
        Assert.Equal(new R.OpenFolder("../etc"), NotoUri.Parse("noto://open/..%2Fetc").Request);
        Assert.Equal(new R.NewFolder("a b"), NotoUri.Parse("noto://new-folder?name=%20a%0D%0Ab%20").Request);
        Assert.Equal(new R.Search("x y"), NotoUri.Parse("noto://search?q=x%0Ay").Request);
    }

    public static TheoryData<R> Requests => new()
    {
        new R.NewNote("hello", null),
        new R.NewNote("line 1\nline 2\twith tab", "Work & Home"),
        new R.NewNote("50% #1 a/b? \"q\" C++ a=b", "01HZY8M3QJ7KX6B2V9T4N5PQRS"),
        new R.NewNote("café € 😀 日記", "日記"),
        new R.NewNote(string.Empty, null),
        new R.OpenFolder("01HZY8M3QJ7KX6B2V9T4N5PQRS"),
        new R.OpenFolder("a/b #c"),
        new R.ShowFolders(),
        new R.NewFolder("Ideas & plans?"),
        new R.Search("50% off"),
        new R.Search(string.Empty),
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public void A_formatted_request_reads_back_as_itself(R request)
    {
        string uri = NotoUri.Format(request);

        Assert.StartsWith("noto://", uri, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", uri, StringComparison.Ordinal);
        Assert.Equal(new AutomationParse(request, AutomationRefusal.None), NotoUri.Parse(uri));
    }

    [Fact]
    public void A_folders_link_is_its_id()
    {
        Assert.Equal("noto://open/01HZY8M3QJ7KX6B2V9T4N5PQRS", NotoUri.Format(new R.OpenFolder("01HZY8M3QJ7KX6B2V9T4N5PQRS")));
    }

    [Fact]
    public void A_folder_is_found_by_id_then_by_name_ignoring_case()
    {
        Folder work = NewFolder("Work");
        Folder other = NewFolder("work ");
        Folder named = NewFolder(work.Id.Value);
        Folder[] folders = [named, work, other];

        Assert.Same(work, R.FindFolder(folders, work.Id.Value));
        Assert.Same(work, R.FindFolder(folders, work.Id.Value.ToLowerInvariant()));
        Assert.Same(work, R.FindFolder(folders, "WORK"));
        Assert.Null(R.FindFolder(folders, "Home"));
        Assert.Null(R.FindFolder([], "Work"));
    }

    private static Folder NewFolder(string name) =>
        new() { Id = FolderId.New(), Name = name, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch };
}

/// <summary>The command line (#192, ADR-022): the same five requests as switches, read through the same parser.</summary>
public sealed class NotoCommandLineTests
{
    public static TheoryData<string[], R> Valid => new()
    {
        { ["--new-note", "hello"], new R.NewNote("hello", null) },
        { ["--new-note", "a & b\nc #1 50%", "--folder", "Work"], new R.NewNote("a & b\nc #1 50%", "Work") },
        { ["--folder", "Work", "--new-note", "x"], new R.NewNote("x", "Work") },
        { ["--new-note", "--folder"], new R.NewNote("--folder", null) },
        { ["--open-folder", "My Work"], new R.OpenFolder("My Work") },
        { ["--folders"], new R.ShowFolders() },
        { ["--new-folder", "Ideas"], new R.NewFolder("Ideas") },
        { ["--search", "milk"], new R.Search("milk") },
        { ["--SEARCH", "milk"], new R.Search("milk") },
        { ["noto://open/Work"], new R.OpenFolder("Work") },
        { ["--data-root=C:\\x", "--new-folder", "Ideas", "--smoke-test"], new R.NewFolder("Ideas") },
        { ["--new-note", "noto://folders"], new R.NewNote("noto://folders", null) },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void Switches_and_a_bare_link_are_read(string[] arguments, R expected)
    {
        Assert.Equal(new AutomationParse(expected, AutomationRefusal.None), NotoCommandLine.Parse(arguments));
    }

    [Theory]
    [InlineData]
    [InlineData("--data-root=C:\\x")]
    [InlineData("--login")]
    [InlineData("--smoke-test", "--unknown")]
    public void Nothing_asked_is_a_plain_launch(params string[] arguments)
    {
        Assert.Equal(default, NotoCommandLine.Parse(arguments));
    }

    [Theory]
    [InlineData(AutomationRefusal.MissingArgument, "--new-note")]
    [InlineData(AutomationRefusal.MissingArgument, "--search")]
    [InlineData(AutomationRefusal.MissingArgument, "--new-note", "x", "--folder")]
    [InlineData(AutomationRefusal.MoreThanOne, "--folders", "--search", "x")]
    [InlineData(AutomationRefusal.MoreThanOne, "noto://folders", "noto://folders")]
    [InlineData(AutomationRefusal.MoreThanOne, "--new-note", "x", "noto://folders")]
    [InlineData(AutomationRefusal.UnknownArgument, "--search", "x", "--folder", "Work")]
    [InlineData(AutomationRefusal.UnknownArgument, "--folder", "Work")]
    [InlineData(AutomationRefusal.UnknownArgument, "--new-note", "x", "--folder", "a", "--folder", "b")]
    [InlineData(AutomationRefusal.UnknownArgument, "noto://new?text=x", "--folder", "Work")]
    [InlineData(AutomationRefusal.UnknownVerb, "noto://delete?folder=Work")]
    [InlineData(AutomationRefusal.EmptyName, "--new-folder", " ")]
    public void A_command_line_obeys_the_links_rules(AutomationRefusal reason, params string[] arguments)
    {
        Assert.Equal(new AutomationParse(null, reason), NotoCommandLine.Parse(arguments));
    }

    [Fact]
    public void A_switchs_text_is_capped_and_cleaned_as_a_links()
    {
        Assert.Equal(AutomationRefusal.TooLong, NotoCommandLine.Parse(["--new-note", new string('a', NotoUri.MaxTextLength + 1)]).Refusal);
        Assert.Equal(new R.NewNote("ab\nc", null), NotoCommandLine.Parse(["--new-note", "a" + (char)0 + "b\r\nc"]).Request);
    }
}
