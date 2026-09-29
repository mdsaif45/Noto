namespace Noto.Core.Workspace;

/// <summary>
/// Which vertical screen edge the workspace docks to (parity A2, J3).
/// </summary>
/// <remarks>
/// The product's own value, stored in the <c>workspace.edge</c> setting. It is
/// deliberately not the platform's <c>DockEdge</c>: Core knows nothing about
/// windows or screens (ADR-009), and the platform maps this value onto its own
/// type. Stored by member name, so the order of the members is not a contract.
/// </remarks>
public enum WorkspaceEdge
{
    Left,
    Right,
}
