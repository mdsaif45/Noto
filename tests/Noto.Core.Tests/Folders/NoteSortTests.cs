using System.Globalization;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Xunit;

namespace Noto.Core.Tests.Folders;

/// <summary>
/// A folder's note sort (#242; parity C10): Manual is the order given, Name is by title A to Z, culture-aware and
/// case-insensitive, Created and Modified are newest first; pinned notes stay first in every order, and ties keep the
/// manual order.
/// </summary>
public sealed class NoteSortTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Manual_is_first_and_the_default()
    {
        Assert.Equal([NoteSort.Manual, NoteSort.Name, NoteSort.Created, NoteSort.Modified], Enum.GetValues<NoteSort>());
        Assert.Equal(NoteSort.Manual, default(NoteSort));
        Assert.Equal(NoteSort.Manual, new Folder { Id = FolderId.New(), Name = "F", CreatedAt = Day, UpdatedAt = Day }.NoteSort);
    }

    [Theory]
    [InlineData("Manual", NoteSort.Manual)]
    [InlineData("Name", NoteSort.Name)]
    [InlineData("Created", NoteSort.Created)]
    [InlineData("Modified", NoteSort.Modified)]
    [InlineData(null, NoteSort.Manual)]
    [InlineData("", NoteSort.Manual)]
    [InlineData("name", NoteSort.Manual)]
    [InlineData("2", NoteSort.Manual)]
    public void A_stored_sort_is_read_by_its_exact_name_and_anything_else_is_manual(string? stored, NoteSort expected) =>
        Assert.Equal(expected, NoteSorts.Parse(stored));

    [Fact]
    public void Every_sort_reads_back_from_the_name_it_is_stored_as()
    {
        foreach (NoteSort sort in Enum.GetValues<NoteSort>())
        {
            Assert.Equal(sort, NoteSorts.Parse(sort.ToString()));
        }
    }

    [Theory]
    [InlineData(NoteSort.Manual, "Manual")]
    [InlineData(NoteSort.Name, "Name")]
    [InlineData(NoteSort.Created, "Date created")]
    [InlineData(NoteSort.Modified, "Date modified")]
    public void Each_sort_has_its_menu_name(NoteSort sort, string name) => Assert.Equal(name, NoteSorts.NameOf(sort));

    [Fact]
    public void The_refusal_names_the_sort_and_the_way_out()
    {
        Assert.Equal("Sorted by name: choose Manual to arrange notes yourself", NoteSorts.ManualOnly(NoteSort.Name));
        Assert.Equal("Sorted by date modified: choose Manual to arrange notes yourself", NoteSorts.ManualOnly(NoteSort.Modified));
    }

    [Fact]
    public void Manual_is_the_order_given()
    {
        Note[] notes = [N("zebra", pinned: true), N("apple"), N("mango")];
        Assert.Same(notes, NoteSorts.Apply(notes, NoteSort.Manual));
    }

    [Fact]
    public void Name_is_by_title_case_insensitive_and_culture_aware_with_the_pinned_first()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        try
        {
            // The title is the first line as titled: "# Banana" is "Banana". Ordinal comparison would put "Zebra"
            // before "apple" and "éclair" after "Zebra".
            Note[] notes = [N("Zebra"), N("# Banana\nbody"), N("éclair"), N("apple"), N("Mango", pinned: true), N("cherry", pinned: true)];

            Assert.Equal(["cherry", "Mango", "apple", "Banana", "éclair", "Zebra"], Titles(NoteSorts.Apply(notes, NoteSort.Name)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Name_puts_a_note_with_no_title_after_the_named_ones_in_their_manual_order()
    {
        Note empty = N(""), hashOnly = N("#"), blank = N("   \n");
        Note[] notes = [empty, N("zebra"), hashOnly, N("apple"), blank];

        IReadOnlyList<Note> sorted = NoteSorts.Apply(notes, NoteSort.Name);

        Assert.Equal(["apple", "zebra"], Titles(sorted.Take(2).ToList()));
        Assert.Equal([empty, hashOnly, blank], sorted.Skip(2));
    }

    [Fact]
    public void Created_and_modified_are_newest_first_with_the_pinned_first()
    {
        Note[] notes =
        [
            N("old", created: 1, modified: 9),
            N("new", created: 5, modified: 2),
            N("mid", created: 3, modified: 3),
            N("pinned old", pinned: true, created: 0, modified: 0),
        ];

        Assert.Equal(["pinned old", "new", "mid", "old"], Titles(NoteSorts.Apply(notes, NoteSort.Created)));
        Assert.Equal(["pinned old", "old", "mid", "new"], Titles(NoteSorts.Apply(notes, NoteSort.Modified)));
    }

    [Fact]
    public void Ties_keep_the_manual_order_so_the_result_is_deterministic()
    {
        Note[] notes = [N("Same", created: 1), N("same", created: 1), N("SAME", created: 1)];

        Assert.Equal(notes, NoteSorts.Apply(notes, NoteSort.Name));
        Assert.Equal(notes, NoteSorts.Apply(notes, NoteSort.Created));
        Assert.Equal(notes, NoteSorts.Apply(notes, NoteSort.Modified));
    }

    private static Note N(string content, bool pinned = false, int created = 0, int modified = 0) => new()
    {
        Id = NoteId.New(),
        Content = content,
        IsPinned = pinned,
        CreatedAt = Day.AddHours(created),
        UpdatedAt = Day.AddHours(modified),
    };

    private static IEnumerable<string> Titles(IEnumerable<Note> notes) => notes.Select(n => n.Title);
}
