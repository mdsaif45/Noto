using System.Text;
using Noto.Core.Folders;

namespace Noto.Core.Automation;

/// <summary>
/// The <c>noto://</c> grammar (#192, ADR-022): the one parser every link and every command line goes through.
/// </summary>
/// <remarks>
/// <code>
///   noto://new?text=&lt;text&gt;[&amp;folder=&lt;id-or-name&gt;]   a note
///   noto://open/&lt;id-or-name&gt;                          a folder's notes
///   noto://folders                                    the folder list
///   noto://new-folder?name=&lt;name&gt;                     a folder
///   noto://search?q=&lt;query&gt;                           Search with the query
/// </code>
/// <para>
/// The scheme, verbs and parameter names ignore case; values are percent-decoded as UTF-8, and <c>+</c> is a plus,
/// not a space. One trailing <c>/</c> is allowed, since browsers add one. Everything else is refused, never guessed
/// at: an unknown verb or parameter, a repeated one, a fragment, a bad escape, text that is not valid Unicode, and
/// anything over the caps.
/// </para>
/// <para>
/// <b>Cleaned, never interpreted.</b> A note's text loses its control characters except newline and tab, and is
/// otherwise kept as given; markup or a script in it is text. A name, a folder reference and a query are one line,
/// normalised as a folder name is (<see cref="FolderName.Normalise"/>). A reference that looks like a path is a name
/// to look up among the folders, nothing more.
/// </para>
/// </remarks>
public static class NotoUri
{
    /// <summary>The longest link read at all, in characters; checked before anything else.</summary>
    /// <remarks>Above Windows' 32,767-character command line, which bounds every real launch anyway.</remarks>
    public const int MaxLength = 64 * 1024;

    /// <summary>The longest note text, in characters, once cleaned: about five pages.</summary>
    /// <remarks>
    /// Sized so the longest request, in <see cref="Format"/>'s form, always fits the activation pipe's 64 KiB message
    /// (ADR-013): at most three UTF-8 bytes a character.
    /// </remarks>
    public const int MaxTextLength = 16 * 1024;

    /// <summary>The longest folder name, folder reference or search query, in characters.</summary>
    public const int MaxNameLength = 256;

    private const string Prefix = "noto://";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> Verbs = new(["new", "open", "folders", "new-folder", "search"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a link.</summary>
    public static AutomationParse Parse(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return Refused(AutomationRefusal.Malformed);
        }

        if (uri.Length > MaxLength)
        {
            return Refused(AutomationRefusal.TooLong);
        }

        if (!uri.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(uri.StartsWith("noto:", StringComparison.OrdinalIgnoreCase) ? AutomationRefusal.Malformed : AutomationRefusal.NotNoto);
        }

        string rest = uri[Prefix.Length..];

        if (rest.Contains('#'))
        {
            return Refused(AutomationRefusal.Malformed);
        }

        int question = rest.IndexOf('?');
        string path = question < 0 ? rest : rest[..question];

        if (path.EndsWith('/'))
        {
            path = path[..^1];
        }

        int slash = path.IndexOf('/');
        string verb = slash < 0 ? path : path[..slash];
        string? segment = slash < 0 ? null : path[(slash + 1)..];

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (question >= 0 && ReadQuery(rest[(question + 1)..], arguments) is var queryRefusal and not AutomationRefusal.None)
        {
            return Refused(queryRefusal);
        }

        if (segment is not null && !verb.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            return Refused(verb.All(char.IsAscii) && Verbs.Contains(verb) ? AutomationRefusal.Malformed : AutomationRefusal.UnknownVerb);
        }

        // ASCII only: a verb is never matched through a Unicode case mapping.
        return (verb.All(char.IsAscii) ? verb.ToUpperInvariant() : string.Empty) switch
        {
            "NEW" => NewNote(arguments),
            "OPEN" => OpenFolder(segment, arguments),
            "FOLDERS" => arguments.Count == 0 ? new(new AutomationRequest.ShowFolders(), AutomationRefusal.None) : Refused(AutomationRefusal.UnknownArgument),
            "NEW-FOLDER" => NewFolder(arguments),
            "SEARCH" => Search(arguments),
            _ => Refused(AutomationRefusal.UnknownVerb),
        };
    }

    /// <summary>
    /// A request as a link: what the activation pipe carries (ADR-013) and what Copy link copies (C16).
    /// <see cref="Parse"/> reads it back as the same request.
    /// </summary>
    public static string Format(AutomationRequest request) => request switch
    {
        AutomationRequest.NewNote { Folder: null } note => $"{Prefix}new?text={Escape(note.Text)}",
        AutomationRequest.NewNote note => $"{Prefix}new?text={Escape(note.Text)}&folder={Escape(note.Folder)}",
        AutomationRequest.OpenFolder open => $"{Prefix}open/{Escape(open.Folder)}",
        AutomationRequest.ShowFolders => $"{Prefix}folders",
        AutomationRequest.NewFolder folder => $"{Prefix}new-folder?name={Escape(folder.Name)}",
        AutomationRequest.Search search => $"{Prefix}search?q={Escape(search.Query)}",
        _ => throw new ArgumentOutOfRangeException(nameof(request)),
    };

    /// <summary>
    /// Escapes what would end or change a value: controls, space, <c>"</c>, <c>#</c>, <c>%</c>, <c>&amp;</c>, <c>/</c>
    /// and <c>?</c>. Everything else, other scripts included, stays as it is, so a link stays near its text's size.
    /// </summary>
    internal static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            if (c < 0x20 || c == 0x7F || c is ' ' or '"' or '#' or '%' or '&' or '/' or '?')
            {
                _ = escaped.Append('%').Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                _ = escaped.Append(c);
            }
        }

        return escaped.ToString();
    }

    private static AutomationParse NewNote(Dictionary<string, string> arguments)
    {
        if (Unexpected(arguments, "text", "folder"))
        {
            return Refused(AutomationRefusal.UnknownArgument);
        }

        if (!arguments.TryGetValue("text", out string? raw))
        {
            return Refused(AutomationRefusal.MissingArgument);
        }

        string text = CleanText(raw);

        if (text.Length > MaxTextLength)
        {
            return Refused(AutomationRefusal.TooLong);
        }

        string? folder = null;

        if (arguments.TryGetValue("folder", out string? reference) && OneLine(reference, out folder) is var refusal and not AutomationRefusal.None)
        {
            return Refused(refusal);
        }

        return new(new AutomationRequest.NewNote(text, folder), AutomationRefusal.None);
    }

    private static AutomationParse OpenFolder(string? segment, Dictionary<string, string> arguments)
    {
        if (arguments.Count > 0)
        {
            return Refused(AutomationRefusal.UnknownArgument);
        }

        if (segment is null)
        {
            return Refused(AutomationRefusal.MissingArgument);
        }

        // One segment: a '/' in a folder reference is escaped.
        if (segment.Contains('/'))
        {
            return Refused(AutomationRefusal.Malformed);
        }

        (string? decoded, AutomationRefusal decodeRefusal) = Decode(segment);

        if (decoded is null)
        {
            return Refused(decodeRefusal);
        }

        return OneLine(decoded, out string? folder) is var refusal and not AutomationRefusal.None
            ? Refused(refusal)
            : new(new AutomationRequest.OpenFolder(folder!), AutomationRefusal.None);
    }

    private static AutomationParse NewFolder(Dictionary<string, string> arguments)
    {
        if (Unexpected(arguments, "name"))
        {
            return Refused(AutomationRefusal.UnknownArgument);
        }

        if (!arguments.TryGetValue("name", out string? raw))
        {
            return Refused(AutomationRefusal.MissingArgument);
        }

        return OneLine(raw, out string? name) is var refusal and not AutomationRefusal.None
            ? Refused(refusal)
            : new(new AutomationRequest.NewFolder(name!), AutomationRefusal.None);
    }

    private static AutomationParse Search(Dictionary<string, string> arguments)
    {
        if (Unexpected(arguments, "q"))
        {
            return Refused(AutomationRefusal.UnknownArgument);
        }

        if (!arguments.TryGetValue("q", out string? raw))
        {
            return Refused(AutomationRefusal.MissingArgument);
        }

        // An empty query is Search as Ctrl+F opens it.
        string query = FolderName.Normalise(raw) ?? string.Empty;

        return query.Length > MaxNameLength
            ? Refused(AutomationRefusal.TooLong)
            : new(new AutomationRequest.Search(query), AutomationRefusal.None);
    }

    /// <summary>A name or folder reference: one line, normalised as a folder name is, not empty, capped.</summary>
    private static AutomationRefusal OneLine(string raw, out string? value)
    {
        value = FolderName.Normalise(raw);

        return value is null ? AutomationRefusal.EmptyName
            : value.Length > MaxNameLength ? AutomationRefusal.TooLong
            : AutomationRefusal.None;
    }

    /// <summary>A note's text without control characters, newline and tab kept; a CR of a CRLF goes too.</summary>
    private static string CleanText(string raw)
    {
        var text = new StringBuilder(raw.Length);

        foreach (char c in raw)
        {
            if (!char.IsControl(c) || c is '\n' or '\t')
            {
                _ = text.Append(c);
            }
        }

        return text.ToString();
    }

    private static bool Unexpected(Dictionary<string, string> arguments, params string[] allowed) =>
        arguments.Keys.Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase));

    /// <summary>Reads <c>key=value</c> pairs; empty pairs are skipped, a repeated key is refused.</summary>
    private static AutomationRefusal ReadQuery(string query, Dictionary<string, string> arguments)
    {
        foreach (string pair in query.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }

            int equals = pair.IndexOf('=');

            if (equals <= 0)
            {
                return AutomationRefusal.Malformed;
            }

            (string? value, AutomationRefusal refusal) = Decode(pair[(equals + 1)..]);

            if (value is null)
            {
                return refusal;
            }

            if (!arguments.TryAdd(pair[..equals], value))
            {
                return AutomationRefusal.UnknownArgument;
            }
        }

        return AutomationRefusal.None;
    }

    /// <summary>
    /// Percent-decodes as UTF-8. Characters that are not escaped stay as they are; a lone surrogate, a bad escape or
    /// bytes that are not UTF-8 are refused.
    /// </summary>
    private static (string? Value, AutomationRefusal Refusal) Decode(string text)
    {
        byte[] raw;

        try
        {
            raw = StrictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            return (null, AutomationRefusal.InvalidText);
        }

        if (Array.IndexOf(raw, (byte)'%') < 0)
        {
            return (text, AutomationRefusal.None);
        }

        byte[] decoded = new byte[raw.Length];
        int length = 0;

        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] != (byte)'%')
            {
                decoded[length++] = raw[i];
                continue;
            }

            if (i + 2 >= raw.Length || HexValue(raw[i + 1]) is not int high || HexValue(raw[i + 2]) is not int low)
            {
                return (null, AutomationRefusal.Malformed);
            }

            decoded[length++] = (byte)((high << 4) | low);
            i += 2;
        }

        try
        {
            return (StrictUtf8.GetString(decoded, 0, length), AutomationRefusal.None);
        }
        catch (DecoderFallbackException)
        {
            return (null, AutomationRefusal.InvalidText);
        }
    }

    private static int? HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => null,
    };

    private static AutomationParse Refused(AutomationRefusal refusal) => new(null, refusal);
}
