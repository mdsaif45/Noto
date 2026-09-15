using Noto.Core.Commands;
using Noto.Core.Notes;
using Xunit;

namespace Noto.Core.Tests.Commands;

/// <summary>
/// The failure model frozen in core-note-engine-contract.md §6.
/// </summary>
public sealed class CommandResultTests
{
    [Fact]
    public void A_successful_result_carries_its_value()
    {
        var id = NoteId.New();

        var result = CommandResult.Success(id);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);
        Assert.Equal(id, result.Value);
    }

    [Fact]
    public void A_failed_result_carries_its_reason()
    {
        var result = CommandResult.Failed<NoteId>(CommandFailure.NotFound("No note 'x'."));

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
        Assert.Equal(CommandFailureReason.NotFound, result.Failure!.Reason);
    }

    [Fact]
    public void Reading_the_value_of_a_failed_result_throws()
    {
        // Silently returning default would let a caller that forgot to check
        // proceed with a null note. Failing loudly is a programming error
        // surfacing as one.
        var result = CommandResult.Failed<NoteId>(CommandFailure.NotFound("No note."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void TryGetValue_reports_success_without_throwing()
    {
        var id = NoteId.New();

        Assert.True(CommandResult.Success(id).TryGetValue(out var value, out var failure));
        Assert.Equal(id, value);
        Assert.Null(failure);
    }

    [Fact]
    public void TryGetValue_reports_failure_without_throwing()
    {
        var result = CommandResult.Failed<NoteId>(CommandFailure.InvalidInput("bad"));

        Assert.False(result.TryGetValue(out _, out var failure));
        Assert.NotNull(failure);
        Assert.Equal(CommandFailureReason.InvalidInput, failure!.Reason);
    }

    [Fact]
    public void A_valueless_result_can_succeed_and_fail()
    {
        Assert.True(CommandResult.Success().IsSuccess);

        var failed = CommandResult.Failed(CommandFailure.InvalidState("deleted"));
        Assert.False(failed.IsSuccess);
        Assert.Equal(CommandFailureReason.InvalidState, failed.Failure!.Reason);
    }

    [Fact]
    public void Exactly_four_business_failure_reasons_exist()
    {
        // Contract §6 freezes the set. A fifth reason added without a gate
        // would be a specification change smuggled in as code, so this test
        // exists to make that fail visibly.
        var reasons = Enum.GetValues<CommandFailureReason>();

        Assert.Equal(4, reasons.Length);
        Assert.Contains(CommandFailureReason.NotFound, reasons);
        Assert.Contains(CommandFailureReason.DuplicateName, reasons);
        Assert.Contains(CommandFailureReason.InvalidInput, reasons);
        Assert.Contains(CommandFailureReason.InvalidState, reasons);
    }

    [Fact]
    public void Every_frozen_reason_is_constructible()
    {
        Assert.Equal(CommandFailureReason.NotFound, CommandFailure.NotFound("m").Reason);
        Assert.Equal(CommandFailureReason.DuplicateName, CommandFailure.DuplicateName("m").Reason);
        Assert.Equal(CommandFailureReason.InvalidInput, CommandFailure.InvalidInput("m").Reason);
        Assert.Equal(CommandFailureReason.InvalidState, CommandFailure.InvalidState("m").Reason);
    }

    [Fact]
    public void Failing_with_no_reason_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CommandResult.Failed<NoteId>(null!));
        Assert.Throws<ArgumentNullException>(() => CommandResult.Failed(null!));
    }

    [Fact]
    public void The_result_model_leaks_no_storage_type()
    {
        // The whole point of separating business failure from infrastructure
        // failure: a SQLite type reachable from here would put "disk full" and
        // "already deleted" back on one channel (contract §6).
        var surface = new[] { typeof(CommandResult), typeof(CommandResult<NoteId>), typeof(CommandFailure) }
            .SelectMany(t => t.GetMembers())
            .Select(m => m.DeclaringType?.Assembly.FullName ?? string.Empty);

        Assert.DoesNotContain(surface, a => a.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
    }
}
