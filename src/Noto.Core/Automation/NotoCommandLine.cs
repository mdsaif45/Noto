namespace Noto.Core.Automation;

/// <summary>
/// Noto's command line (#192, ADR-022): the <c>noto://</c> verbs as switches, each turned into its link and read by
/// <see cref="NotoUri.Parse"/>, so a switch and a link obey the same rules.
/// </summary>
/// <remarks>
/// <code>
///   --new-note &lt;text&gt; [--folder &lt;id-or-name&gt;]    noto://new?text=…[&amp;folder=…]
///   --open-folder &lt;id-or-name&gt;                  noto://open/…
///   --folders                                   noto://folders
///   --new-folder &lt;name&gt;                         noto://new-folder?name=…
///   --search &lt;query&gt;                            noto://search?q=…
///   noto://…                                    as given: how Windows passes a link
/// </code>
/// A switch's value is the next argument, whatever it says. Other arguments (<c>--data-root=</c>, <c>--login</c>) are
/// not this parser's and are passed over. More than one request on one command line is refused.
/// </remarks>
public static class NotoCommandLine
{
    private const string NewNotePrefix = "noto://new?text=";

    /// <summary>Reads the arguments after the executable.</summary>
    /// <returns>A request, a refusal, or neither when nothing was asked: a plain launch.</returns>
    public static AutomationParse Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? uri = null;
        string? folder = null;
        bool newNote = false;

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];

            // ASCII only: a switch is never matched through a Unicode case mapping.
            string key = argument.All(char.IsAscii) ? argument.ToUpperInvariant() : string.Empty;
            string? link;

            if (argument.StartsWith("noto:", StringComparison.OrdinalIgnoreCase))
            {
                link = argument;
            }
            else if (key == "--FOLDERS")
            {
                link = "noto://folders";
            }
            else if (LinkOf(key) is { } prefix)
            {
                if (++i >= arguments.Count)
                {
                    return Refused(AutomationRefusal.MissingArgument);
                }

                link = prefix + NotoUri.Escape(arguments[i]);
                newNote = prefix == NewNotePrefix;
            }
            else if (key == "--FOLDER")
            {
                if (folder is not null)
                {
                    return Refused(AutomationRefusal.UnknownArgument);
                }

                if (++i >= arguments.Count)
                {
                    return Refused(AutomationRefusal.MissingArgument);
                }

                folder = arguments[i];
                continue;
            }
            else
            {
                continue;
            }

            if (uri is not null)
            {
                return Refused(AutomationRefusal.MoreThanOne);
            }

            uri = link;
        }

        if (folder is not null)
        {
            // --folder belongs to --new-note alone.
            if (!newNote)
            {
                return Refused(AutomationRefusal.UnknownArgument);
            }

            uri += "&folder=" + NotoUri.Escape(folder);
        }

        return uri is null ? default : NotoUri.Parse(uri);
    }

    private static string? LinkOf(string key) => key switch
    {
        "--NEW-NOTE" => NewNotePrefix,
        "--OPEN-FOLDER" => "noto://open/",
        "--NEW-FOLDER" => "noto://new-folder?name=",
        "--SEARCH" => "noto://search?q=",
        _ => null,
    };

    private static AutomationParse Refused(AutomationRefusal refusal) => new(null, refusal);
}
