namespace Noto.UseCases.Commands;

/// <summary>
/// An event: a fact about a mutation that has succeeded, such as <c>NoteCreated</c> (ADR-010, #21).
/// </summary>
/// <remarks>
/// Only types that implement this can be registered as a command's event or published through
/// <see cref="EventHub"/>, so a command cannot announce an arbitrary object, and every event in Noto can be found by
/// what implements this interface. Events are facts, not requests, and carry identities, never note content.
/// </remarks>
public interface IEvent
{
}
