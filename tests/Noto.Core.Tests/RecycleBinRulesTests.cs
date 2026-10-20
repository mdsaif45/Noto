using Noto.Core.Commands;
using Noto.Core.Folders;
using Noto.Core.Notes;
using Xunit;

namespace Noto.Core.Tests;

/// <summary>The recycle bin's decisions (deletion-semantics §13), every branch, without a database.</summary>
public sealed class RecycleBinRulesTests
{
    private static readonly NoteId Note = NoteId.New();
    private static readonly FolderId Folder = FolderId.New();

    [Fact]
    public void Only_a_live_folder_reaches_its_active_notes()
    {
        // I7 in one line: the workspace shows notes only inside a live folder.
        Assert.True(RecycleBinRules.Reaches(FolderState.Active));
        Assert.False(RecycleBinRules.Reaches(FolderState.Deleted));
        Assert.False(RecycleBinRules.Reaches(FolderState.Missing));
    }

    [Theory]
    [InlineData(NoteLifecycle.Missing, CommandFailureReason.NotFound)]
    [InlineData(NoteLifecycle.Active, CommandFailureReason.InvalidState)]
    public void A_note_not_in_the_bin_is_never_purged(NoteLifecycle state, CommandFailureReason expected)
    {
        // An active note is deleted first (I5): a purge never removes live content.
        Assert.Equal(expected, RecycleBinRules.RefusePurge(Note, state)!.Reason);
    }

    [Fact]
    public void A_note_in_the_bin_may_be_purged() =>
        Assert.Null(RecycleBinRules.RefusePurge(Note, NoteLifecycle.Deleted));

    [Theory]
    [InlineData(FolderLifecycle.Missing, CommandFailureReason.NotFound)]
    [InlineData(FolderLifecycle.Active, CommandFailureReason.InvalidState)]
    public void A_folder_not_in_the_bin_is_never_purged(FolderLifecycle state, CommandFailureReason expected) =>
        Assert.Equal(expected, RecycleBinRules.RefusePurge(Folder, state)!.Reason);

    [Fact]
    public void A_folder_in_the_bin_may_be_purged_as_far_as_its_own_state_decides() =>
        Assert.Null(RecycleBinRules.RefusePurge(Folder, FolderLifecycle.Deleted));

    [Fact]
    public void A_folder_an_active_note_lives_in_is_kept_as_an_invalid_state()
    {
        CommandFailure failure = RecycleBinRules.ActiveNotesRemain(Folder);

        Assert.Equal(CommandFailureReason.InvalidState, failure.Reason);
        Assert.Contains(Folder.Value, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NoteLifecycle.Missing, FolderState.Active, CommandFailureReason.NotFound)]
    [InlineData(NoteLifecycle.Active, FolderState.Active, CommandFailureReason.InvalidState)]
    [InlineData(NoteLifecycle.Deleted, FolderState.Missing, CommandFailureReason.NotFound)]
    [InlineData(NoteLifecycle.Deleted, FolderState.Deleted, CommandFailureReason.InvalidState)]
    public void A_restore_into_a_folder_is_refused_unless_the_note_is_binned_and_the_folder_live(
        NoteLifecycle note, FolderState target, CommandFailureReason expected) =>
        Assert.Equal(expected, RecycleBinRules.RefuseRestoreInto(Note, note, Folder, target)!.Reason);

    [Fact]
    public void The_note_is_judged_before_the_folder()
    {
        // A missing note is NotFound whatever the target is: the command names the note first.
        Assert.Equal(
            CommandFailureReason.NotFound,
            RecycleBinRules.RefuseRestoreInto(Note, NoteLifecycle.Missing, Folder, FolderState.Deleted)!.Reason);
        Assert.Contains(Note.Value, RecycleBinRules.RefuseRestoreInto(Note, NoteLifecycle.Missing, Folder, FolderState.Deleted)!.Message, StringComparison.Ordinal);
        Assert.Contains(Folder.Value, RecycleBinRules.RefuseRestoreInto(Note, NoteLifecycle.Deleted, Folder, FolderState.Missing)!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_binned_note_may_be_restored_into_a_live_folder() =>
        Assert.Null(RecycleBinRules.RefuseRestoreInto(Note, NoteLifecycle.Deleted, Folder, FolderState.Active));

    [Fact]
    public void A_note_whose_folder_is_binned_is_refused_as_an_invalid_state()
    {
        CommandFailure failure = RecycleBinRules.FolderInBin(Note);

        Assert.Equal(CommandFailureReason.InvalidState, failure.Reason);
        Assert.Contains(Note.Value, failure.Message, StringComparison.Ordinal);
    }
}
