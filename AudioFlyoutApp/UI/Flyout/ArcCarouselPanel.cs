using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BetterAudio.UI.Flyout;

public readonly record struct ArcPose(double X, double Y, double Scale, double Opacity, double Rotation, double Depth);

public static class ArcLayout
{
    public static ArcPose Calculate(int index, double position, int count, double width)
    {
        if (count < 1) return new(0, 0, 1, 0, 0, 0);
        var distance = index - position;
        distance -= Math.Floor((distance + count / 2.0) / count) * count;
        var depth = Math.Abs(distance);
        var x = Math.Sin(Math.Min(depth, 2.5) * .7) * Math.Min(172, width * .49) * Math.Sign(distance);
        var scale = Math.Max(.34, 1 - .35 * depth);
        var opacity = depth <= 1 ? 1 - .6 * depth : Math.Max(0, .4 * (1 - (depth - 1) / .8));
        return new(x, 40 * Math.Pow(depth, 1.05), scale, opacity, distance * 13, depth);
    }
}

// Animate one continuous wheel position; every chamber follows the same arc.
public sealed class ArcCarouselPanel : Panel
{
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(ArcCarouselPanel), new FrameworkPropertyMetadata(-1, SelectionChanged));
    private static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(double), typeof(ArcCarouselPanel), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsArrange));
    internal static readonly DependencyProperty HoverProgressProperty = DependencyProperty.RegisterAttached("HoverProgress", typeof(double), typeof(ArcCarouselPanel), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsParentArrange));
    internal static AnimationClock? AnimateHover(UIElement child, bool hovered)
    {
        var current = (double)child.GetValue(HoverProgressProperty);
        var target = hovered ? 1d : 0d;
        child.BeginAnimation(HoverProgressProperty, null);
        child.SetValue(HoverProgressProperty, current);
        if (SystemParameters.ClientAreaAnimation)
        {
            var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var clock = (AnimationClock)((Timeline)animation).CreateClock(true);
            child.ApplyAnimationClock(HoverProgressProperty, clock);
            return clock;
        }
        child.SetValue(HoverProgressProperty, target);
        return null;
    }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    private double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    private int _count;
    private int _lastSelection = -1;
    private bool _initialized;
    private static void SelectionChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var panel = (ArcCarouselPanel)owner;
        // Touching InternalChildren during template construction starts item generation
        // before ItemsPresenter has validated its empty items host.
        if (panel.IsLoaded) panel.MoveToSelection();
        else panel.InvalidateMeasure();
    }
    private void MoveToSelection()
    {
        var count = InternalChildren.Count;
        if (count < 1 || SelectedIndex < 0) return;
        var current = Position;
        var target = SelectedIndex + Math.Round((current - SelectedIndex) / count) * count;
        if (_initialized && _count == count && IsVisible && SystemParameters.ClientAreaAnimation)
            BeginAnimation(PositionProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        else BeginAnimation(PositionProperty, null);
        Position = target;
        _count = count;
        _lastSelection = SelectedIndex;
        _initialized = true;
        InvalidateArrange();
    }
    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren) child.Measure(new Size(96, 100));
        if (_count != InternalChildren.Count || _lastSelection != SelectedIndex) MoveToSelection();
        return new Size(double.IsInfinity(available.Width) ? 352 : available.Width, 148);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var pose = ArcLayout.Calculate(i, Position, InternalChildren.Count, finalSize.Width);
            var transforms = child.RenderTransform as TransformGroup;
            if (transforms is null)
            {
                transforms = new TransformGroup();
                transforms.Children.Add(new ScaleTransform());
                transforms.Children.Add(new RotateTransform());
                transforms.Children.Add(new TranslateTransform());
                child.RenderTransformOrigin = new Point(.5, .5);
                child.RenderTransform = transforms;
                child.MouseEnter += (_, _) => AnimateHover(child, true);
                child.MouseLeave += (_, _) => AnimateHover(child, false);
            }
            ((ScaleTransform)transforms.Children[0]).ScaleX = pose.Scale;
            ((ScaleTransform)transforms.Children[0]).ScaleY = pose.Scale;
            ((RotateTransform)transforms.Children[1]).Angle = pose.Rotation;
            ((TranslateTransform)transforms.Children[2]).X = pose.X;
            var hover = (double)child.GetValue(HoverProgressProperty) * Math.Clamp(pose.Depth / .3, 0, 1);
            ((TranslateTransform)transforms.Children[2]).Y = pose.Y - 6 * hover;
            child.Opacity = pose.Opacity + Math.Max(0, .8 - pose.Opacity) * hover;
            child.IsHitTestVisible = pose.Opacity > .15;
            SetZIndex(child, 100 - (int)(pose.Depth * 20));
            child.Arrange(new Rect((finalSize.Width - 96) / 2, 10, 96, 100));
        }
        return finalSize;
    }
}
