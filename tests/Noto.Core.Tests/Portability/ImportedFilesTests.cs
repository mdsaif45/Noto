using Noto.Core.Portability;
using Xunit;

namespace Noto.Core.Tests.Portability;

/// <summary>#267 (J29): which files a drop import may move to Windows' Recycle Bin, given what is known of each path.</summary>
public sealed class ImportedFilesTests
{
    [Fact]
    public void A_file_on_a_local_fixed_drive_may_be_recycled()
    {
        Assert.True(ImportedFiles.MayRecycle(@"C:\Users\me\Desktop\list.md", FileAttributes.Archive, DriveType.Fixed));
        Assert.True(ImportedFiles.MayRecycle(@"d:\notes\plain.txt", FileAttributes.Normal, DriveType.Fixed));
        Assert.True(ImportedFiles.MayRecycle(@"C:\a\photo.png", FileAttributes.Hidden | FileAttributes.Archive, DriveType.Fixed));
    }

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.Removable)]
    [InlineData(DriveType.CDRom)]
    [InlineData(DriveType.Ram)]
    [InlineData(DriveType.NoRootDirectory)]
    [InlineData(DriveType.Unknown)]
    public void A_file_on_any_drive_but_a_fixed_one_stays_where_it_has_no_recycle_bin(DriveType drive) =>
        Assert.False(ImportedFiles.MayRecycle(@"E:\list.md", FileAttributes.Archive, drive));

    [Fact]
    public void A_drive_whose_type_is_unknown_keeps_the_file() =>
        Assert.False(ImportedFiles.MayRecycle(@"C:\list.md", FileAttributes.Archive, null));

    [Theory]
    [InlineData(@"\\server\share\list.md")]
    [InlineData(@"\\?\C:\list.md")]
    [InlineData(@"\\.\C:\list.md")]
    [InlineData(@"\\?\UNC\server\share\list.md")]
    [InlineData(@"list.md")]
    [InlineData(@"C:list.md")]
    [InlineData(@"\list.md")]
    [InlineData("")]
    public void A_path_not_under_a_drive_letter_stays_even_if_its_drive_reads_as_fixed(string path) =>
        Assert.False(ImportedFiles.MayRecycle(path, FileAttributes.Archive, DriveType.Fixed));

    [Theory]
    [InlineData(FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.Archive)]
    [InlineData(FileAttributes.Directory)]
    public void A_read_only_file_or_a_folder_stays(FileAttributes attributes) =>
        Assert.False(ImportedFiles.MayRecycle(@"C:\a\list.md", attributes, DriveType.Fixed));

    [Fact]
    public void A_file_that_cannot_be_read_stays() =>
        Assert.False(ImportedFiles.MayRecycle(@"C:\a\list.md", null, DriveType.Fixed));

    [Fact]
    public void Each_stored_file_is_offered_once_and_never_noto_s_own_copy_of_a_virtual_file()
    {
        string copies = @"C:\Users\me\AppData\Local\Temp\Noto\drops\abc";
        IReadOnlyList<string> offered = ImportedFiles.ToRecycle(
            [@"C:\a\list.md", @"C:\A\LIST.md", copies + @"\0\Harbour.png", @"C:\a\plain.txt", copies + "x" + @"\0\other.png"],
            copies + @"\");

        Assert.Equal([@"C:\a\list.md", @"C:\a\plain.txt", copies + "x" + @"\0\other.png"], offered);
        Assert.Equal([@"C:\a\list.md"], ImportedFiles.ToRecycle([@"C:\a\list.md"], null));
        Assert.Empty(ImportedFiles.ToRecycle([], copies));
    }
}
