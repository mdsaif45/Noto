namespace Noto.Core.Diagnostics;

/// <summary>
/// What a log may say about an exception: the types in its chain and its HResult. Nothing else.
/// </summary>
/// <remarks>
/// <para>
/// An exception's message and stack trace are where note content and user file paths leak into logs: a message
/// quotes the value it choked on or the file it could not open (principle 10, quality-gates *Log redaction*). This
/// type has no member that could hold either, so a log written from it cannot contain them, whoever writes the
/// sink. Exceptions reach the log only through <see cref="From"/>.
/// </para>
/// </remarks>
public sealed class ExceptionSummary
{
    // Bounded: a chain that loops through a custom InnerException, or a huge aggregate, still ends.
    private const int MaxTypes = 16;

    private ExceptionSummary(IReadOnlyList<string> types, int hResult)
    {
        Types = types;
        HResult = hResult;
    }

    /// <summary>The exception's type, then each inner exception's, outermost first; for an aggregate, its inner ones in order.</summary>
    public IReadOnlyList<string> Types { get; }

    /// <summary>The outermost exception's HResult: for a Win32 or COM failure, the error code.</summary>
    public int HResult { get; }

    /// <summary>Summarises an exception. Its message, data, source and stack trace are not read.</summary>
    public static ExceptionSummary From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var types = new List<string>();
        Add(exception, types, depth: 0);
        return new ExceptionSummary(types, exception.HResult);
    }

    private static void Add(Exception exception, List<string> types, int depth)
    {
        if (types.Count >= MaxTypes || depth > MaxTypes)
        {
            return;
        }

        types.Add(exception.GetType().FullName ?? exception.GetType().Name);

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                Add(inner, types, depth + 1);
            }
        }
        else if (exception.InnerException is { } inner)
        {
            Add(inner, types, depth + 1);
        }
    }
}
