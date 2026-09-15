namespace Noto.Core;

/// <summary>
/// Anchors this assembly for reflection in architecture tests (#20).
/// </summary>
/// <remarks>
/// The domain model — Note, Folder, Tag, Attachment, Task — arrives in M1
/// (#13). This project currently holds the ADR-009 boundary and nothing else:
/// no platform dependency, and no type referring to a window, screen,
/// monitor, coordinate or z-order.
/// </remarks>
public static class AssemblyMarker
{
    /// <summary>The product name, used where a neutral identifier is needed.</summary>
    public const string ProductName = "Noto";
}
