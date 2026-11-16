using Noto.Core.Editing;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>Code detection on paste (D39, #187): what is code, and the fenced block it becomes.</summary>
public sealed class PastedCodeTests
{
    public static TheoryData<string> Code => new()
    {
        // C#
        "public sealed class Greeter\n{\n    private readonly string _name;\n\n    public string Greet() => $\"Hello {_name}\";\n}",
        "    var total = items.Sum(i => i.Price);\n    if (total > 100)\n    {\n        discount = 0.1m;\n    }",
        "using System;\nnamespace Demo;\n",
        // JavaScript
        "const add = (a, b) => a + b;\nfunction greet(name) {\n  console.log(`hi ${name}`);\n}",
        "import React from 'react';\nexport default function App() {\n  return <div />;\n}",
        // Python
        "def greet(name):\n    if name:\n        print(f\"Hello {name}\")\n    return None",
        "import os\nfor path in os.listdir('.'):\n    total += 1\nprint(total)",
        // JSON
        "{\n  \"name\": \"noto\",\n  \"version\": 3,\n  \"tags\": [\"notes\", \"windows\"]\n}",
        // Shell
        "cd src\ndotnet build -c Release\ngit status",
        "#!/bin/sh\nset -e\nnpm ci\nnpm test",
        "$ ls -la\n$ echo done",
        // CRLF from the clipboard
        "if (x)\r\n{\r\n    y();\r\n}\r\n",
    };

    public static TheoryData<string> NotCode => new()
    {
        // one line, however code-like
        "var x = 1;",
        "  return x;\n",
        // prose
        "The meeting moved to Thursday at 3pm.\nBring the Q4 numbers and the draft plan.\nWe will decide on the launch date.",
        "Thanks for the update. I read it twice.\nLet's talk tomorrow, if that works for you.",
        // bullet and numbered lists
        "- milk\n- eggs\n- bread\n- coffee",
        "1. Open Settings\n2. Turn on the toolbar\n3. Close Settings",
        "- [ ] call the dentist\n- [x] book the room",
        // addresses
        "Jane Doe\n12 Main Street, Flat 4\nSpringfield, IL 62701\nUnited States",
        // poems, indents and all
        "The woods are lovely, dark and deep,\n    But I have promises to keep,\nAnd miles to go before I sleep,\n    And miles to go before I sleep.",
        "Shall I compare thee to a summer's day?\n  Thou art more lovely and more temperate:\nRough winds do shake the darling buds of May,\n  And summer's lease hath all too short a date;",
        // Markdown that already holds a code block
        "Run this:\n```\nnpm ci\nnpm test\n```",
        // a note with Noto's own marks
        "**Attendees:** Priya, Tom\n==underlined== and ::highlighted:: words\n> a quote",
        "a -> b -> c\n| a || b |\n| c || d |",
    };

    [Theory]
    [MemberData(nameof(Code))]
    public void Code_from_the_usual_languages_is_code(string pasted) => Assert.True(PastedCode.IsCode(pasted));

    [Theory]
    [MemberData(nameof(NotCode))]
    public void Prose_lists_addresses_poems_and_single_lines_are_not(string pasted) => Assert.False(PastedCode.IsCode(pasted));

    [Fact]
    public void Code_at_the_end_of_a_line_goes_on_lines_of_its_own_and_the_caret_after_it()
    {
        const string text = "Notes:";
        TextEdit edit = Assert.NotNull(PastedCode.Fence(text, TextSelection.Caret(6), "cd src\ngit status\n"));

        string result = edit.ApplyTo(text);
        Assert.Equal("Notes:\n```\ncd src\ngit status\n```\n", result);
        Assert.Equal(TextSelection.Caret(result.Length), edit.After);
    }

    [Fact]
    public void Code_on_an_empty_line_before_more_text_adds_no_blank_lines_and_the_caret_goes_to_the_next_line()
    {
        const string text = "a\n\nb";
        TextEdit edit = Assert.NotNull(PastedCode.Fence(text, TextSelection.Caret(2), "x = 1;\ny = 2;"));

        string result = edit.ApplyTo(text);
        Assert.Equal("a\n```\nx = 1;\ny = 2;\n```\nb", result);
        Assert.Equal(TextSelection.Caret(result.IndexOf('b', StringComparison.Ordinal)), edit.After);
    }

    [Fact]
    public void Code_in_the_middle_of_a_line_splits_it_and_replaces_the_selection()
    {
        const string text = "before SELECTED after";
        TextEdit edit = Assert.NotNull(PastedCode.Fence(text, new TextSelection(7, 15), "x = 1;\ny = 2;"));

        Assert.Equal("before \n```\nx = 1;\ny = 2;\n```\n after", edit.ApplyTo(text));
    }

    [Fact]
    public void Leading_blank_lines_and_trailing_space_go_and_the_first_indent_stays()
    {
        TextEdit edit = Assert.NotNull(PastedCode.Fence(string.Empty, TextSelection.Caret(0), "\n  \n    if (a)\n        b();\n    "));

        Assert.Equal("```\n    if (a)\n        b();\n```\n", edit.ApplyTo(string.Empty));
    }

    [Theory]
    [InlineData(8)]   // inside the code
    [InlineData(5)]   // at the end of the opening fence's line
    [InlineData(24)]  // at the end of the closing fence
    public void A_caret_inside_a_code_block_is_never_wrapped(int caret)
    {
        const string text = "x\n```\nold();\nmore();\n```\ny";
        Assert.Null(PastedCode.Fence(text, TextSelection.Caret(caret), "a = 1;\nb = 2;"));
    }

    [Fact]
    public void An_unclosed_fence_runs_to_the_end_so_its_end_is_inside()
    {
        const string text = "```\nold();\n";
        Assert.Null(PastedCode.Fence(text, TextSelection.Caret(text.Length), "a = 1;\nb = 2;"));
    }

    [Fact]
    public void Before_a_code_block_and_after_it_on_a_new_line_is_outside()
    {
        const string text = "```\nold();\n```\n";
        Assert.NotNull(PastedCode.Fence(text, TextSelection.Caret(0), "a = 1;\nb = 2;"));
        Assert.NotNull(PastedCode.Fence(text, TextSelection.Caret(text.Length), "a = 1;\nb = 2;"));
    }

    [Fact]
    public void Html_that_brought_its_own_code_block_wins_and_other_html_does_not()
    {
        const string code = "a = 1;\nb = 2;";
        Assert.Null(PastedCode.Fence(string.Empty, TextSelection.Caret(0), code, "```\na = 1;\nb = 2;\n```"));
        Assert.NotNull(PastedCode.Fence(string.Empty, TextSelection.Caret(0), code, "a = 1; b = 2;"));
    }

    [Fact]
    public void Text_that_is_not_code_is_an_ordinary_paste()
    {
        Assert.Null(PastedCode.Fence("x", TextSelection.Caret(1), "- milk\n- eggs"));
    }
}
