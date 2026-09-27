namespace Noto.Windows.Tests;

/// <summary>
/// Whether this test run has an interactive Windows desktop to talk to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists before anything uses it.</b> The tests that will need it
/// — monitor enumeration, <c>DwmGetWindowAttribute</c>, hotkey registration —
/// arrive with the production code they exercise (#16 slice 1, and #58's
/// runtime validation). The distinction they depend on has to be decided once,
/// here, rather than three times in three slices with three different answers.
/// </para>
/// <para>
/// <b>The distinction that matters:</b> "this machine has no desktop" is not
/// the same result as "this behaviour is broken". A headless CI agent has no
/// window station a window can appear on, so a runtime test there proves
/// nothing either way. Skipping says so; failing would claim a defect that was
/// never observed, and passing would claim a verification that never ran.
/// </para>
/// <para>
/// No placeholder test exercises this. A test written only to call the helper
/// would assert that the current machine is whatever it happens to be, which
/// is not a contract.
/// </para>
/// </remarks>
public static class DesktopSession
{
    /// <summary>
    /// Whether an interactive desktop session is available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Environment.UserInteractive"/> is the framework's own answer
    /// to this question and is what a service or a session-0 process reports
    /// false for. It is deliberately the only signal used: probing further —
    /// opening a window station, counting monitors, reading a session id —
    /// would be environment-specific guesswork, and a fragile probe that
    /// wrongly reports "no desktop" silently converts a real failure into a
    /// skip, which is the one outcome this must not produce.
    /// </para>
    /// <para>
    /// A machine with a desktop but no attached display still reports true.
    /// That is correct for the question being asked: the tests that consume
    /// this need a window station, and a test needing a <i>display</i> states
    /// that itself.
    /// </para>
    /// </remarks>
    public static bool IsInteractive => Environment.UserInteractive;

    /// <summary>
    /// The reason a runtime test was skipped, for the test output.
    /// </summary>
    /// <remarks>
    /// Named rather than inlined so every skipped runtime test reports the
    /// same sentence, and a reader scanning a CI log can tell at a glance that
    /// the run was incomplete rather than clean.
    /// </remarks>
    public const string SkipReason =
        "Requires an interactive Windows desktop session. Not run — not passed.";

    /// <summary>
    /// The trait every runtime test carries, so the suite can be filtered.
    /// </summary>
    /// <remarks>
    /// <c>dotnet test --filter "Category!=RequiresDesktop"</c> excludes them
    /// wholesale. That is the mechanism a headless agent should use: a test
    /// that is filtered out is visibly not run, whereas one that skips itself
    /// at runtime still had to start. Both outcomes are honest; the filter is
    /// cheaper and states the intent in the command.
    /// </remarks>
    public const string RequiresDesktopTrait = "RequiresDesktop";

    /// <summary>
    /// Whether the calling runtime test should proceed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// xUnit 2.9's <c>[Fact(Skip = ...)]</c> takes a compile-time constant and
    /// cannot consult the environment, and no dynamic-skip package is
    /// referenced here — adding one for an infrastructure gate with no runtime
    /// tests yet would be a dependency bought ahead of its need.
    /// </para>
    /// <para>
    /// So a runtime test guards itself:
    /// </para>
    /// <code>
    /// [Fact]
    /// [Trait("Category", DesktopSession.RequiresDesktopTrait)]
    /// public void Enumerates_the_primary_monitor()
    /// {
    ///     if (!DesktopSession.ShouldRun(out string reason))
    ///     {
    ///         Assert.Skip(reason);   // xUnit v3; until then, see below
    ///     }
    ///     ...
    /// }
    /// </code>
    /// <para>
    /// On xUnit 2.9 there is no <c>Assert.Skip</c>, so the guard returns early
    /// after recording the reason through the test's output helper. The
    /// test then reports as passed-without-assertions, which is <b>not</b> good
    /// enough on its own — which is exactly why the trait exists and why a
    /// headless run is expected to filter these out rather than run them. When
    /// the repository moves to xUnit v3, the body of this helper becomes a
    /// one-line <c>Assert.Skip</c> and the trait stays as the filter.
    /// </para>
    /// </remarks>
    /// <param name="reason">Why the test cannot run, when it cannot.</param>
    public static bool ShouldRun(out string reason)
    {
        if (IsInteractive)
        {
            reason = string.Empty;
            return true;
        }

        reason = SkipReason;
        return false;
    }
}
