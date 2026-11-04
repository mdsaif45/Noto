using System.Text.Json;

namespace Noto.Core.Portability;

/// <summary>
/// The small index an export writes beside its Markdown (ADR-020 §4): each folder's directory and name, each note's
/// file, in the workspace's order, with what Markdown cannot carry (pinned, folded, colour). Import reads it when it is
/// there and valid; without it an export is still a folder of plain Markdown.
/// </summary>
public sealed record ExportIndex
{
    /// <summary>The file's name in the export's root.</summary>
    public const string FileName = "noto-export.json";

    /// <summary>The value of <see cref="Format"/>.</summary>
    public const string FormatName = "noto-export";

    /// <summary>The version this build writes and the newest it reads.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Format { get; init; } = FormatName;

    public int Version { get; init; } = CurrentVersion;

    public required DateTimeOffset ExportedAt { get; init; }

    public required IReadOnlyList<ExportFolder> Folders { get; init; }

    /// <summary>The index as written.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// The index in <paramref name="json"/>, or <see langword="null"/> when it is not one this build reads (another
    /// format, a newer version, or not valid): the export is then imported as plain Markdown.
    /// </summary>
    public static ExportIndex? Read(string json)
    {
        try
        {
            ExportIndex? index = JsonSerializer.Deserialize<ExportIndex>(json, Json);
            return index is { Format: FormatName, Version: >= 1 and <= CurrentVersion, Folders: not null }
                && index.Folders.All(f => f is { Directory: not null, Name: not null, Notes: not null } && f.Notes.All(n => n is { File: not null }))
                ? index
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A folder of an export: its directory, its name in Noto, its state and its notes in order.</summary>
public sealed record ExportFolder(string Directory, string Name, bool Pinned, string? ColorKey, IReadOnlyList<ExportNote> Notes);

/// <summary>A note of an export: its file in the folder's directory and its state.</summary>
public sealed record ExportNote(string File, bool Pinned, bool Folded, string? ColorKey);

/// <summary>How far a long piece of work is: <paramref name="Done"/> of <paramref name="Total"/> items.</summary>
public readonly record struct PortabilityProgress(int Done, int Total);
