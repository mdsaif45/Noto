using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Noto.Platform.Windows;
using Windows.UI.ViewManagement;

namespace Noto;

/// <summary>
/// The workspace's motion (Slice 1.3, F2): the drawer sliding in from its edge and back out, and a surface entering.
/// Runs on the compositor; owns no state of its own beyond what it is animating.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window never moves.</b> The whole drawer, background and all, is translated its full width inside the docked
/// rectangle, so no frame of it can appear outside that rectangle, on another monitor or anywhere else. The window is
/// see-through (<see cref="TransparentBackdrop"/>), so what the drawer has not yet covered is the desktop, never an
/// empty frame. No timer, no per-frame code: each transition is keyframe animations, and the hide waits on their
/// batch's completion.
/// </para>
/// <para>
/// <b>Reduced motion.</b> Windows' "Animation effects" setting is read at each transition (no subscription, no
/// polling); off, the final state is applied at once and a hide happens at once.
/// </para>
/// <para>
/// <b>Input is never held.</b> Nothing here touches focus, hit testing, enabled state or tab order: the user can type
/// and click through every transition. The hide's state and its decisions are <see cref="HideTransition"/>'s and
/// <see cref="WorkspaceToggle"/>'s; this only animates and reports completion.
/// </para>
/// </remarks>
internal sealed class ContentMotion
{
    private readonly FrameworkElement _drawer;
    private readonly UISettings _system = new();
    private readonly Motion? _motion;

    /// <param name="drawer">The whole drawer: its background and everything on it.</param>
    public ContentMotion(FrameworkElement drawer)
    {
        _drawer = drawer ?? throw new ArgumentNullException(nameof(drawer));
        _motion = Motion.Read();

        if (_motion is null)
        {
            Trace.WriteLine("Noto.Motion unavailable reason=resources");
        }
    }

    /// <summary>Whether a transition runs now: motion resources present, and Windows' animation effects on.</summary>
    public bool Enabled => _motion is not null && MotionPlan.Animate(_system.AnimationsEnabled);

    /// <summary>Before the window is shown: the drawer just past its edge, or at rest when there is no motion.</summary>
    public void PrepareEnter(DockEdge edge)
    {
        if (!Enabled)
        {
            Settle();
            return;
        }

        Visual visual = VisualOf(_drawer);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", new Vector3((float)MotionPlan.EnterFrom(edge, _drawer.ActualWidth), 0, 0));
    }

    /// <summary>
    /// The drawer slides in from the docked edge: after a show (from just past the edge), or reversing a hide (from
    /// wherever it is).
    /// </summary>
    public void Enter(DockEdge edge, bool fromCurrent)
    {
        if (!Enabled)
        {
            Trace.WriteLine("Noto.Motion skipped kind=enter reason=animations-off");
            Settle();
            return;
        }

        float from = (float)MotionPlan.EnterFrom(edge, _drawer.ActualWidth);
        Run(_drawer, _motion!.DrawerEnter, fromCurrent ? null : from, 0, fade: false, "enter", () => Trace.WriteLine("Noto.Motion done kind=enter"));
        Trace.WriteLine($"Noto.Motion start kind=enter ms={_motion.DrawerEnter.TotalMilliseconds:F0} from={(fromCurrent ? "current" : from.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
    }

    /// <summary>The drawer slides out past the docked edge; <paramref name="completed"/> runs when it has.</summary>
    /// <returns>Whether it is running; <see langword="false"/> when there is no motion, and <paramref name="completed"/> is not called.</returns>
    public bool Exit(DockEdge edge, Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        if (!Enabled)
        {
            return false;
        }

        float to = (float)MotionPlan.ExitTo(edge, _drawer.ActualWidth);
        Run(_drawer, _motion!.DrawerExit, null, to, fade: false, "exit", completed);
        Trace.WriteLine($"Noto.Motion start kind=exit ms={_motion.DrawerExit.TotalMilliseconds:F0} to={to.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return true;
    }

    /// <summary>A surface just made visible slides in: from the right going forward, from the left going back.</summary>
    public void EnterSurface(UIElement surface, bool forward)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!Enabled)
        {
            return;
        }

        float from = (float)MotionPlan.SurfaceFrom(forward, _motion!.SurfaceDistance);
        Run(surface, _motion.Surface, from, 0, fade: true, "surface", static () => { });
        Trace.WriteLine($"Noto.Motion start kind=surface forward={forward} ms={_motion.Surface.TotalMilliseconds:F0}");
    }

    /// <summary>The drawer at rest, now: no animation running, in place.</summary>
    public void Settle()
    {
        Visual visual = VisualOf(_drawer);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
    }

    /// <param name="fade">A surface also fades in; the drawer only slides, its background opaque throughout.</param>
    private void Run(UIElement element, TimeSpan duration, float? fromX, float toX, bool fade, string kind, Action completed)
    {
        Visual visual = VisualOf(element);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(_motion!.Easing.X1, _motion.Easing.Y1), new Vector2(_motion.Easing.X2, _motion.Easing.Y2));

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Translation.X", Keyframes(compositor, duration, easing, fromX, toX));
        if (fade)
        {
            visual.StartAnimation(nameof(Visual.Opacity), Keyframes(compositor, duration, easing, 0f, 1));
        }

        batch.End();

        // Raised on the UI thread, which owns the compositor; a replaced animation completes its batch too, and the
        // caller decides whether that completion still counts (HideTransition's generation).
        batch.Completed += (_, _) =>
        {
            try
            {
                completed();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                Trace.WriteLine($"Noto.Motion completion failed kind={kind} error={ex.GetType().Name}");
            }
        };
    }

    private static ScalarKeyFrameAnimation Keyframes(Compositor compositor, TimeSpan duration, CompositionEasingFunction easing, float? from, float to)
    {
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();

        if (from is float start)
        {
            animation.InsertKeyFrame(0f, start);
        }
        else
        {
            animation.InsertExpressionKeyFrame(0f, "this.StartingValue");
        }

        animation.InsertKeyFrame(1f, to, easing);
        animation.Duration = duration;
        return animation;
    }

    private static Visual VisualOf(UIElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        return ElementCompositionPreview.GetElementVisual(element);
    }

    /// <summary>
    /// The motion roles resolved: WinUI's durations and curve by the keys Noto's tokens name, and Noto's surface
    /// distance. The drawer's distance is its own width.
    /// </summary>
    private sealed record Motion(
        TimeSpan DrawerEnter,
        TimeSpan DrawerExit,
        TimeSpan Surface,
        (float X1, float Y1, float X2, float Y2) Easing,
        double SurfaceDistance)
    {
        public static Motion? Read()
        {
            ResourceDictionary resources = Application.Current.Resources;

            object? Value(string key) => resources.TryGetValue(key, out object value) ? value : null;
            string? WinUi(string role) => Value(role) is string key ? Value(key) as string : null;

            if (WinUi("MotionDrawerEnterDuration") is not string enter
                || WinUi("MotionDrawerExitDuration") is not string exit
                || WinUi("MotionSurfaceDuration") is not string surface
                || WinUi("MotionEasing") is not string easing
                || Value("MotionSurfaceDistance") is not double surfaceDistance)
            {
                return null;
            }

            try
            {
                return new Motion(
                    MotionPlan.ParseDuration(enter),
                    MotionPlan.ParseDuration(exit),
                    MotionPlan.ParseDuration(surface),
                    MotionPlan.ParseSpline(easing),
                    surfaceDistance);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                return null;
            }
        }
    }
}
