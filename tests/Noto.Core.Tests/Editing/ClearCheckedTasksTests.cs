using Noto.Core.Editing;
using Xunit;

namespace Noto.Core.Tests.Editing;

/// <summary>F19 (#158; parity D22, G34): every checked task removed in one edit, everything else kept.</summary>
public sealed class ClearCheckedTasksTests
{
    [Theory]
    [InlineData("Shop\n- [ ] milk\n- [x] bread\n- [ ] eggs", "Shop\n- [ ] milk\n- [ ] eggs")]
    [InlineData("- [x] a\n- [X] b\ntext\n- [x] c", "text")]
    [InlineData("- [x] only", "")]
    [InlineData("Plan\n- [ ] keep\n  - [x] nested done\n- [x] done\nend", "Plan\n- [ ] keep\nend")]
    [InlineData("```\n- [x] in code\n```\n- [x] real", "```\n- [x] in code\n```")]
    [InlineData("1. [x] numbered done\n- [ ] open\n", "- [ ] open\n")]
    public void Every_checked_task_goes_and_everything_else_stays(string before, string after)
    {
        TextEdit edit = MarkdownCommands.ClearCheckedTasks(before, TextSelection.Caret(0))!.Value;

        Assert.Equal(after, edit.ApplyTo(before));
    }

    [Theory]
    [InlineData("- [ ] open")]
    [InlineData("no tasks at all")]
    [InlineData("")]
    public void Nothing_checked_is_nothing_to_do(string text)
    {
        Assert.Null(MarkdownCommands.ClearCheckedTasks(text, TextSelection.Caret(0)));
    }

    [Fact]
    public void The_caret_keeps_its_place_in_the_text_that_stays()
    {
        const string text = "- [x] a\n- [ ] b\n- [x] c\nend";
        int inB = text.IndexOf('b', StringComparison.Ordinal);
        int inC = text.IndexOf('c', StringComparison.Ordinal);

        TextEdit edit = MarkdownCommands.ClearCheckedTasks(text, TextSelection.Caret(inB))!.Value;
        string after = edit.ApplyTo(text);

        Assert.Equal("- [ ] b\nend", after);
        Assert.Equal('b', after[edit.After.Active]);

        // Inside a removed task, the caret goes to where its line was: the start of the line after it.
        TextEdit fromC = MarkdownCommands.ClearCheckedTasks(text, TextSelection.Caret(inC))!.Value;
        Assert.Equal(after.IndexOf("end", StringComparison.Ordinal), fromC.After.Active);
    }
}
