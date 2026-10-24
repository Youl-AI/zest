using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Zest.App.Wheel;

/// <summary>
/// Interruptible single-key animations. None of them sets <c>From</c>: each starts from the property's current
/// (possibly mid-animation) value, so a reversed highlight continues from where it is instead of jumping.
/// </summary>
public static class Motion
{
    /// <summary>Windows "Animation effects" is off: drop movement, keep color and opacity changes.</summary>
    public static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;

    public static void To(IAnimatable target, DependencyProperty property, double value, TimeSpan duration, KeySpline curve, Action? completed = null)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(value, KeyTime.FromTimeSpan(duration), curve));
        if (completed is not null) animation.Completed += (_, _) => completed();
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void To(IAnimatable target, DependencyProperty property, Color value, TimeSpan duration, KeySpline curve)
    {
        var animation = new ColorAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineColorKeyFrame(value, KeyTime.FromTimeSpan(duration), curve));
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Stops any animation on the property and sets it at once.</summary>
    public static void Set(IAnimatable target, DependencyProperty property, object value)
    {
        target.BeginAnimation(property, null);
        ((DependencyObject)target).SetValue(property, value);
    }
}
