using System.Runtime.CompilerServices;
using System.Text.Json;
using Noto.Core.Diagnostics;
using Xunit;

namespace Noto.Core.Tests.Diagnostics;

/// <summary>
/// The logging contract (#7): what a log is given can hold no exception message, no stack trace and no absolute
/// path, because the types it is given have no way to hold them.
/// </summary>
public sealed class DiagnosticsContractTests
{
    private const string Secret = "SECRET-NOTE-TEXT-4711";

    private static readonly string Root = Path.Join(Path.GetTempPath(), "noto-root-3f1c");

    [Fact]
    public void An_exception_summary_holds_the_type_chain_and_hresult_and_nothing_else()
    {
        var inner = new IOException(@"C:\Users\SecretUser\Documents\private.txt");
        var outer = new InvalidOperationException(Secret, inner) { HResult = unchecked((int)0x80070005) };

        var summary = ExceptionSummary.From(outer);

        Assert.Equal(["System.InvalidOperationException", "System.IO.IOException"], summary.Types);
        Assert.Equal(unchecked((int)0x80070005), summary.HResult);

        // Its public surface is exactly those two members: there is nowhere to put a message or a trace.
        Assert.Equal(
            ["HResult", "Types"],
            typeof(ExceptionSummary).GetProperties().Select(p => p.Name).Order());

        string serialized = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretUser", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_summary_does_not_carry_the_stack_trace()
    {
        ExceptionSummary summary;

        try
        {
            ThrowFromAFrameNamedLeakyFrame4711();
            return;
        }
        catch (InvalidOperationException ex)
        {
            Assert.Contains("LeakyFrame4711", ex.StackTrace, StringComparison.Ordinal);
            summary = ExceptionSummary.From(ex);
        }

        Assert.DoesNotContain("LeakyFrame4711", JsonSerializer.Serialize(summary), StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_lists_its_inner_exceptions_in_order_and_a_long_chain_is_bounded()
    {
        var aggregate = new AggregateException(new TimeoutException(Secret), new FormatException(Secret));
        Assert.Equal(["System.AggregateException", "System.TimeoutException", "System.FormatException"], ExceptionSummary.From(aggregate).Types);

        Exception deep = new InvalidOperationException(Secret);

        for (int i = 0; i < 100; i++)
        {
            deep = new InvalidOperationException(Secret, deep);
        }

        Assert.Equal(16, ExceptionSummary.From(deep).Types.Count);
    }

    [Theory]
    [InlineData(@"noto-premigration-v1-20261013T101500.db", "noto-premigration-v1-20261013T101500.db")]
    [InlineData(@"backups\noto 2026.db", "backups/noto 2026.db")]
    [InlineData(@"logs\..\backups\a.db", "backups/a.db")]
    [InlineData(@"backups\.\a.db", "backups/a.db")]
    [InlineData(@"backups/a.db", "backups/a.db")]
    public void A_path_inside_the_data_root_is_recorded_relative_and_normalized(string inside, string expected)
    {
        var path = DataRootPath.Of(Root, Path.Join(Root, inside));

        Assert.True(path.IsInside);
        Assert.Equal(expected, path.Value);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\'), path.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Users\SecretUser\Documents\notes.db")]       // another folder
    [InlineData(@"D:\SecretUser\notes.db")]                       // another drive
    [InlineData(@"\\fileserver\SecretUser\notes.db")]             // a share
    [InlineData(@"\\?\C:\Users\SecretUser\notes.db")]             // a device path
    [InlineData(@"..\SecretUser\notes.db")]                       // climbs out, relative
    [InlineData(@"..\..\..\..\Users\SecretUser\notes.db")]        // climbs out, far
    [InlineData(@"backups\..\..\SecretUser.db")]                  // climbs out through an inner folder
    [InlineData(@"notes.db:SecretUser")]                          // an alternate data stream
    [InlineData(@"backups\SecretUser\naïve.db")]                  // not a plain name
    [InlineData("")]
    [InlineData("   ")]
    public void A_path_that_is_not_plainly_inside_the_data_root_is_outside(string hostile)
    {
        // Relative inputs are resolved against the data root, as the code that writes there would.
        var path = DataRootPath.Of(Root, hostile.StartsWith(@"\\", StringComparison.Ordinal) || Path.IsPathFullyQualified(hostile) ? hostile : Path.Join(Root, hostile));

        Assert.False(path.IsInside);
        Assert.Equal(DataRootPath.OutsideMarker, path.Value);
        Assert.DoesNotContain("SecretUser", path.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_that_differs_from_the_root_only_in_case_is_inside_and_keeps_only_its_relative_part()
    {
        // Windows paths are case-insensitive; the recorded part is the path's own, below the root.
        var path = DataRootPath.Of(Root.ToUpperInvariant(), Path.Join(Root.ToLowerInvariant(), "Backups", "A.db"));

        Assert.Equal("Backups/A.db", path.Value);
    }

    [Fact]
    public void The_representation_is_lexical_so_a_link_inside_the_root_is_recorded_by_its_own_name_never_its_target()
    {
        // No file system access: a junction or symbolic link under the root is recorded by the name it has there.
        // Its target, wherever it points, is never resolved and so can never reach the log.
        Assert.Equal("logs/linked/noto.db", DataRootPath.Of(Root, Path.Join(Root, "logs", "linked", "noto.db")).Value);
    }

    [Fact]
    public void A_sibling_folder_that_shares_the_roots_prefix_is_outside()
    {
        // "…\noto-root-3f1c-SecretUser" starts with the root's text but is not inside it.
        Assert.Equal(DataRootPath.OutsideMarker, DataRootPath.Of(Root, Root + @"-SecretUser\notes.db").Value);
    }

    [Fact]
    public void The_root_itself_a_relative_root_and_the_default_value_are_outside()
    {
        Assert.Equal(DataRootPath.OutsideMarker, DataRootPath.Of(Root, Root).Value);
        Assert.Equal(DataRootPath.OutsideMarker, DataRootPath.Of(@"relative\root", @"relative\root\a.db").Value);
        Assert.Equal(DataRootPath.OutsideMarker, default(DataRootPath).Value);
        Assert.Equal(DataRootPath.OutsideMarker, default(DataRootPath).ToString());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowFromAFrameNamedLeakyFrame4711() => throw new InvalidOperationException(Secret);
}
