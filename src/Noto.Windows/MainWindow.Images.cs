using Noto.Core.Images;

namespace Noto;

/// <summary>A note's images (ADR-019 §4): the editor and the cards draw them from the files kept beside the notes.</summary>
public sealed partial class MainWindow
{
    private IImageStore? _images;

    /// <summary>The image files beside the notes; set once by the app before the window is shown.</summary>
    internal IImageStore? Images
    {
        get => _images;
        set
        {
            _images = value;
            MarkdownReadView.ResolveImage = value is null ? null : value.PathOf;
        }
    }

    private void OnEditorImageNotice(object? sender, string message) => ShowNoteNotice(message);
}
