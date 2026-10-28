using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace BetterAudio.UI.Flyout;

// Interpolate only the presentation value: intermediate animation frames never write audio.
public sealed class AnimatedVolumeSlider : Slider
{
    public static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(nameof(DisplayValue), typeof(double), typeof(AnimatedVolumeSlider), new PropertyMetadata(0d));
    public double DisplayValue { get => (double)GetValue(DisplayValueProperty); set => SetValue(DisplayValueProperty, value); }
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        var current = DisplayValue;
        BeginAnimation(DisplayValueProperty, null);
        DisplayValue = newValue;
        if (IsLoaded && !IsMouseCaptureWithin && SystemParameters.ClientAreaAnimation)
            BeginAnimation(DisplayValueProperty, new DoubleAnimation(current, newValue, TimeSpan.FromMilliseconds(110))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
