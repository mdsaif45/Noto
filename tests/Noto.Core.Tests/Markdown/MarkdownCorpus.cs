using System.Globalization;
using System.Text;

namespace Noto.Core.Tests.Markdown;

/// <summary>
/// Notes as people write them (ADR-017 §5 asks for these, not only synthetic markup), and the hidden-markdown spike's
/// dense synthetic corpus (about one span per twelve characters, the worst case).
/// </summary>
internal static class MarkdownCorpus
{
    public static readonly (string Name, string Text)[] Realistic =
    [
        ("groceries", """
            # Groceries
            - [ ] milk
            - [ ] eggs (free range)
            - [x] bread
            - [ ] **coffee** — the dark roast, not the decaf
            - [x] apples
            - [ ] olive oil
            """),

        ("meeting notes", """
            ## Weekly sync — 3 Nov

            **Attendees:** Priya, Tom, Chen, me

            ### Decisions
            - Ship the *beta* to the internal group on Friday
            - Keep the old importer until v2; see [the RFC](https://example.com/rfc/42)
            - Chen owns the release notes

            ### Actions
            - [ ] Tom: fix the crash on startup when the data folder is missing
            - [ ] Priya: draft the onboarding email
            - [x] me: book the room for the demo
            - [ ] everyone: review `release-checklist.md` before Thursday

            > "Done is better than perfect, but broken is worse than late." — Chen

            Next sync: **Monday 10:00**, same room.
            """),

        ("code snippets", """
            # Handy commands

            Rebase the branch onto main without a merge commit:

            ```bash
            git fetch origin
            git rebase origin/main
            git push --force-with-lease
            ```

            Find what holds a file open: `handle.exe -a notes.db` (Sysinternals).

            ```csharp
            // Retry with a short back-off; *not* a spin loop.
            for (int i = 0; i < 3; i++)
            {
                if (TryOpen(path, out var db)) return db;
                await Task.Delay(100 * (i + 1));
            }
            ```

            - `Ctrl+Shift+P` — command palette
            - `Ctrl+K Ctrl+C` — comment the selection
            """),

        ("long-form prose", Prose(12)),

        ("reading list", """
            # Reading list

            1. [Designing Data-Intensive Applications](https://dataintensive.net) — chapters 5–7 again
            2. [The Pragmatic Programmer](https://pragprog.com/titles/tpp20/) — *20th anniversary edition*
            3. [Crafting Interpreters](https://craftinginterpreters.com/contents.html)
            4. ~~Clean Architecture~~ — finished in October
            5. [SQLite's WAL docs](https://www.sqlite.org/wal.html) (short, read twice)

            ---

            Papers:
            - [Out of the Tar Pit](https://curtclifton.net/papers/MoseleyMarks06a.pdf)
            - [Time, Clocks, and the Ordering of Events](https://lamport.azurewebsites.net/pubs/time-clocks.pdf)
            """),

        ("project plan", """
            # Garden shed — plan

            ## Phase 1: base
            1. Clear and level the ground
               - remove the old paving
               - check for pipes ==before== digging
            2. Lay the gravel and the slabs
               - [ ] order 20 slabs
               - [x] borrow the plate compactor

            ## Phase 2: frame
            - Treated timber only
            - Roof pitch at least 10°

            ***

            > Budget: **£1,200** all in. Anything over needs a second quote.

            ### Open questions
            - Does it need *planning permission* at 2.5 m?
            - Gutter to the water butt, or a soakaway?
            """),

        ("pasted document", Pasted()),

        ("journal", """
            Tuesday. Slept badly — the neighbours' dog again 🐶.

            Good morning though: finished the **storage layer** refactor and the tests pass on the first run, which never happens. Lunch with Sam at the new ramen place (🍜 was *excellent*).

            Things to remember:
            - call the dentist
            - the bike needs a new chain
            - ask Mum about the photos from 2019

            Evening: read for an hour. Quote of the day:
            > The best way out is always through.
            """),
    ];

    /// <summary>The spike's corpus: its twelve line templates, repeated until the note is at least <paramref name="chars"/> long.</summary>
    public static string Dense(int chars)
    {
        string[] templates =
        [
            "## Section {0}",
            "Some **bold text** and *italic words* with `inline code` and a [link](https://example.com/{0}) here.",
            "- [ ] Task item number {0} with ~~struck~~ text",
            "- [x] Done item {0}",
            "1. Ordered item with ==underlined== and ::marked:: words",
            "> A quote line {0} with **strong** emphasis",
            "```",
            "var x = {0}; // code inside a fence",
            "```",
            "---",
            "Plain paragraph text that goes on for a while to simulate a real note, number {0}, with no markup at all.",
            string.Empty,
        ];

        var text = new StringBuilder();
        for (int i = 0; text.Length < chars; i++)
        {
            // Not string.Format: most templates have no placeholder, and the fence lines must stay exactly as written.
            text.Append(templates[i % templates.Length].Replace("{0}", i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>A long realistic note: the realistic notes, repeated with their numbers changed, to at least <paramref name="chars"/>.</summary>
    public static string LargeRealistic(int chars)
    {
        var text = new StringBuilder();
        for (int round = 0; text.Length < chars; round++)
        {
            foreach ((_, string note) in Realistic)
            {
                text.Append(note.Replace("Phase", $"Phase {round}.", StringComparison.Ordinal)).Append("\n\n");
            }
        }

        return text.ToString();
    }

    private static string Prose(int paragraphs)
    {
        string[] sentences =
        [
            "The first draft of anything is mostly a conversation with yourself about what you actually think.",
            "Local-first software keeps the data on the device, so the app works on a plane and the notes outlive the company.",
            "A note-taking tool earns its place by being faster to reach than the scratch file that is already open.",
            "Most of the work was not the feature itself but deciding what would happen when it failed.",
            "We measured before optimising, and the slow part was not where anyone had guessed.",
            "Keyboard-first does not mean keyboard-only; it means nothing important is mouse-only.",
            "The *interesting* bugs were all at the edges: empty notes, huge notes, and notes pasted from somewhere else.",
            "If a setting has no clear default, that is usually a sign the feature is not finished.",
        ];

        var text = new StringBuilder("# Notes on building small tools\n\n");
        for (int p = 0; p < paragraphs; p++)
        {
            for (int s = 0; s < 6; s++)
            {
                text.Append(sentences[(p * 3 + s) % sentences.Length]).Append(' ');
            }

            text.Append(p % 4 == 3 ? "This is **the** point.\n\n" : "\n\n");
        }

        return text.ToString();
    }

    /// <summary>Text pasted from a web page and a chat: Windows line breaks, long lines, emoji, CJK, stray markup.</summary>
    private static string Pasted() => string.Join(
        "\r\n",
        "Release notes 2.4 (copied from the vendor page)",
        string.Empty,
        "Fixed: the sync agent no longer uploads a file twice when its name differs only by case; previously, on case-insensitive volumes, renaming README.md to readme.md produced two uploads and a conflict copy that users then had to delete by hand, which caused a lot of support tickets in the last quarter.",
        "Improved: startup is about 30% faster on large libraries (> 50 000 items).",
        "Known issue: * on some systems the tray icon disappears after Explorer restarts * — restart the agent.",
        string.Empty,
        "From the team chat:",
        "林: 明天的会议改到下午三点 👍",
        "Ana: works for me 🙌 — I'll bring the **printed** agenda",
        "Ravi: price is 5*3 = 15, not 5 * 3 * 2",
        "snake_case_names and file_v2_final_FINAL.docx stay as they are");
}
