using Noto.Core.Workspace;
using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// Escape's four behaviours on the three surfaces (#16 slice 6; parity A12,
/// G8, G51), as one table.
/// </summary>
public sealed class WorkspaceEscapeTests
{
    [Theory]
    [InlineData(EscapeBehavior.LeaveFolderOrHide, WorkspaceSurface.Editor, EscapeAction.LeaveEditor)]
    [InlineData(EscapeBehavior.LeaveFolderOrHide, WorkspaceSurface.NoteList, EscapeAction.LeaveFolder)]
    [InlineData(EscapeBehavior.LeaveFolderOrHide, WorkspaceSurface.FolderList, EscapeAction.Hide)]
    [InlineData(EscapeBehavior.LeaveFolder, WorkspaceSurface.Editor, EscapeAction.LeaveEditor)]
    [InlineData(EscapeBehavior.LeaveFolder, WorkspaceSurface.NoteList, EscapeAction.LeaveFolder)]
    [InlineData(EscapeBehavior.LeaveFolder, WorkspaceSurface.FolderList, EscapeAction.None)]
    [InlineData(EscapeBehavior.Hide, WorkspaceSurface.Editor, EscapeAction.Hide)]
    [InlineData(EscapeBehavior.Hide, WorkspaceSurface.NoteList, EscapeAction.Hide)]
    [InlineData(EscapeBehavior.Hide, WorkspaceSurface.FolderList, EscapeAction.Hide)]
    [InlineData(EscapeBehavior.None, WorkspaceSurface.Editor, EscapeAction.None)]
    [InlineData(EscapeBehavior.None, WorkspaceSurface.NoteList, EscapeAction.None)]
    [InlineData(EscapeBehavior.None, WorkspaceSurface.FolderList, EscapeAction.None)]
    public void Each_behaviour_on_each_surface(EscapeBehavior behavior, WorkspaceSurface surface, EscapeAction expected)
    {
        Assert.Equal(expected, WorkspaceEscape.Resolve(behavior, surface));
    }

    [Fact]
    public void The_default_goes_back_a_level_then_hides()
    {
        // SideNotes' default (feature inventory A12), which the setting's
        // default is: back from the note list, then a press at the folder
        // list hides.
        EscapeBehavior defaultBehavior = Noto.Core.Settings.SettingKeys.WorkspaceEscape.Default;

        Assert.Equal(EscapeBehavior.LeaveFolderOrHide, defaultBehavior);
        Assert.Equal(EscapeAction.LeaveFolder, WorkspaceEscape.Resolve(defaultBehavior, WorkspaceSurface.NoteList));
        Assert.Equal(EscapeAction.Hide, WorkspaceEscape.Resolve(defaultBehavior, WorkspaceSurface.FolderList));
    }

    [Fact]
    public void Only_a_hiding_behaviour_ever_hides()
    {
        foreach (WorkspaceSurface surface in Enum.GetValues<WorkspaceSurface>())
        {
            Assert.NotEqual(EscapeAction.Hide, WorkspaceEscape.Resolve(EscapeBehavior.LeaveFolder, surface));
            Assert.NotEqual(EscapeAction.Hide, WorkspaceEscape.Resolve(EscapeBehavior.None, surface));
        }
    }
}
