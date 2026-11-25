using System.Text;
using Noto.Core.Themes;
using Noto.Infrastructure.Storage;
using Xunit;

namespace Noto.Infrastructure.Tests.Storage;

/// <summary>#269 (ADR-023 §8): installed themes are a folder Noto names its files in, written whole or not at all.</summary>
public sealed class ThemeStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("noto-themes-").FullName;

    private string Themes => Path.Join(_root, "themes");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void The_folder_is_created_at_the_first_install_and_the_bytes_are_copied_unchanged_through_a_temporary_file()
    {
        var store = new ThemeStore(Themes);
        Assert.Empty(store.List());
        Assert.False(Directory.Exists(Themes));

        byte[] bytes = Theme("Harbour", "// comments are kept\n");
        string file = store.Install(bytes, "Harbour", replacing: null);

        Assert.Equal("Harbour.nototheme", file);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Join(Themes, file)));
        Assert.Equal(["Harbour.nototheme"], Directory.EnumerateFiles(Themes).Select(Path.GetFileName));
        Assert.Equal([new InstalledTheme("Harbour.nototheme", "Harbour", "Noto")], store.List());
    }

    [Fact]
    public void The_file_is_named_by_noto_with_2_for_another_name_that_gives_the_same_file_name()
    {
        var store = new ThemeStore(Themes);

        Assert.Equal("a_b.nototheme", store.Install(Theme("a/b"), "a/b", null));
        Assert.Equal("a_b (2).nototheme", store.Install(Theme("a:b"), "a:b", null));
        Assert.Equal("CON_.nototheme", store.Install(Theme("CON"), "CON", null));
        Assert.Equal("x._y.nototheme", store.Install(Theme("x..y"), "x..y", null));
        Assert.All(store.List(), t => Assert.True(ThemeFile.IsFileName(t.File)));
    }

    [Fact]
    public void Replacing_writes_over_the_installed_file_of_that_name()
    {
        var store = new ThemeStore(Themes);
        string file = store.Install(Theme("Harbour"), "Harbour", null);
        byte[] newer = Theme("harbour", "// newer\n");

        Assert.Equal(file, store.Install(newer, "harbour", replacing: file));
        Assert.Equal(newer, store.Read(file));
        Assert.Single(Directory.EnumerateFiles(Themes));
    }

    [Fact]
    public void Listing_skips_other_files_names_one_that_cant_be_read_and_stops_at_100()
    {
        Directory.CreateDirectory(Themes);
        File.WriteAllText(Path.Join(Themes, "notes.txt"), "x");
        File.WriteAllText(Path.Join(Themes, "Broken.nototheme"), "{ not json");
        for (int i = 0; i < 120; i++)
        {
            File.WriteAllBytes(Path.Join(Themes, $"T{i:000}.nototheme"), Theme($"T{i:000}"));
        }

        IReadOnlyList<InstalledTheme> listed = new ThemeStore(Themes).List();

        Assert.Equal(ThemeStore.Limit, listed.Count);
        Assert.Equal(new InstalledTheme("Broken.nototheme", null, null), listed[0]);
        Assert.DoesNotContain(listed, t => t.File == "notes.txt");
    }

    [Fact]
    public void A_file_larger_than_the_cap_is_never_read_and_a_path_is_never_an_installed_name()
    {
        Directory.CreateDirectory(Themes);
        File.WriteAllBytes(Path.Join(Themes, "Big.nototheme"), new byte[ThemeFile.MaxBytes + 1]);
        var store = new ThemeStore(Themes);

        Assert.Null(store.Read("Big.nototheme"));
        Assert.Null(store.Read("Gone.nototheme"));
        Assert.Throws<ArgumentException>(() => store.PathOf(@"..\noto.db.nototheme"));
        Assert.Throws<ArgumentException>(() => store.PathOf(string.Empty));
    }

    private static byte[] Theme(string name, string prefix = "") =>
        Encoding.UTF8.GetBytes(prefix + $$"""{ "version": 1, "name": "{{name}}", "author": "Noto", "dark": { "text": "#fff" } }""");
}
