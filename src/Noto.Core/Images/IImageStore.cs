namespace Noto.Core.Images;

/// <summary>What the editor and the cards need of the image files beside the notes (ADR-019 §4).</summary>
public interface IImageStore
{
    /// <summary>Keeps <paramref name="content"/> as an image of <paramref name="type"/>; the reference to write, or why not.</summary>
    (ImageKeeping Outcome, string? Reference) Keep(byte[] content, string type);

    /// <summary>Keeps a copy of the image file at <paramref name="path"/>; the reference to write, or why not.</summary>
    (ImageKeeping Outcome, string? Reference) KeepCopyOf(string path);

    /// <summary>The file of a reference Noto made, when it is there; otherwise <see langword="null"/>.</summary>
    string? PathOf(string? reference);
}

/// <summary>What keeping an image did.</summary>
public enum ImageKeeping
{
    /// <summary>Kept: the note may refer to it.</summary>
    Kept,

    /// <summary>Not a type Noto keeps, or its bytes are not that type.</summary>
    NotAnImage,

    /// <summary>Larger than <see cref="ImageReference.MaxBytes"/>.</summary>
    TooLarge,

    /// <summary>It could not be read or written.</summary>
    Failed,
}
