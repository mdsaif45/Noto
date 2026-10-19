using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Noto.Platform.Windows;
using Windows.UI.ViewManagement;

namespace Noto;

/// <summary>
/// The workspace's motion (Slice 1.3): the drawer's content entering and leaving, and a surface entering. Runs on the
/// compositor; owns no state of its own beyond what it is animating.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window never moves.</b> Only the content element's visual is translated and faded, inside the docked
/// rectangle, so no frame of the drawer can appear outside it. No timer, no per-frame code: each transition is two
/// keyframe animations, and the hide waits on their batch's completion.
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
    private readonly UIElement _content;
    private readonly UISettings _system = new();
    private readonly Motion? _motion;

    /// <param name="content">The drawer's content, inside the window's unmoving background.</param>
    public ContentMotion(UIElement content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _motion = Motion.Read();

        if (_motion is null)
        {
            Trace.WriteLine("Noto.Motion unavailable reason=resources");
        }
    }

    /// <summary>Whether a transition runs now: motion resources present, and Windows' animation effects on.</summary>
    public bool Enabled => _motion is not null && MotionPlan.Animate(_system.AnimationsEnabled);

    /// <summary>Before the window is shown: the content at its starting place, or at rest when there is no motion.</summary>
    public void PrepareEnter(DockEdge edge)
    {
        if (!Enabled)
        {
            Settle();
            return;
        }

        Visual visual = VisualOf(_content);
        visual.StopAnimation("Translation.X");
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Properties.InsertVector3("Translation", new Vector3((float)MotionPlan.EnterFrom(edge, _motion!.DrawerEnterDistance), 0, 0));
        visual.Opacity = 0;
    }

    /// <summary>
    /// The content slides away from the docked edge and fades in: after a show (from its starting place), or reversing a
    /// hide (from wherever it is).
    /// </summary>
    public void Enter(DockEdge edge, bool fromCurrent)
    {
        if (!Enabled)
        {
            Trace.WriteLine("Noto.Motion skipped kind=enter reason=animations-off");
            Settle();
            return;
        }

        float from = (float)MotionPlan.EnterFrom(edge, _motion!.DrawerEnterDistance);
        Run(_content, _motion.DrawerEnter, fromCurrent ? null : from, 0, fromCurrent ? null : 0f, 1, "enter", () => Trace.WriteLine("Noto.Motion done kind=enter"));
        Trace.WriteLine($"Noto.Motion start kind=enter ms={_motion.DrawerEnter.TotalMilliseconds:F0} from={(fromCurrent ? "current" : from.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
    }

    /// <summary>The content slides toward the docked edge and fades out; <paramref name="completed"/> runs when it has.</summary>
    /// <returns>Whether it is running; <see langword="false"/> when there is no motion, and <paramref name="completed"/> is not called.</returns>
    public bool Exit(DockEdge edge, Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        if (!Enabled)
        {
            return false;
        }

        float to = (float)MotionPlan.ExitTo(edge, _motion!.DrawerExitDistance);
        Run(_content, _motion.DrawerExit, null, to, null, 0, "exit", completed);
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
        Run(surface, _motion.Surface, from, 0, 0f, 1, "surface", static () => { });
        Trace.WriteLine($"Noto.Motion start kind=surface forward={forward} ms={_motion.Surface.TotalMilliseconds:F0}");
    }

    /// <summary>The content at rest, now: no animation running, in place, fully shown.</summary>
    public void Settle()
    {
        Visual visual = VisualOf(_content);
        visual.StopAnimation("Translation.X");
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Opacity = 1;
    }

    private void Run(UIElement element, TimeSpan duration, float? fromX, float toX, float? fromOpacity, float toOpacity, string kind, Action completed)
    {
        Visual visual = VisualOf(element);
        Compositor compositor = visual.Compositor;
        CubicBezierEasingFunction easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(_motion!.Easing.X1, _motion.Easing.Y1), new Vector2(_motion.Easing.X2, _motion.Easing.Y2));

        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Translation.X", Keyframes(compositor, duration, easing, fromX, toX));
        visual.StartAnimation(nameof(Visual.Opacity), Keyframes(compositor, duration, easing, fromOpacity, toOpacity));
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

    /// <summary>The motion roles resolved: WinUI's durations and curve by the keys Noto's tokens name, and Noto's distances.</summary>
    private sealed record Motion(
        TimeSpan DrawerEnter,
        TimeSpan DrawerExit,
        TimeSpan Surface,
        (float X1, float Y1, float X2, float Y2) Easing,
        double DrawerEnterDistance,
        double DrawerExitDistance,
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
                || Value("MotionDrawerEnterDistance") is not double enterDistance
                || Value("MotionDrawerExitDistance") is not double exitDistance
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
                    enterDistance,
                    exitDistance,
                    surfaceDistance);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                return null;
            }
        }
    }
}
