using System.Windows;

namespace NexusClick.App;

/// <summary>Shared switch for decorative effects; inherited by controls inside the window.</summary>
public static class SpaceMotion
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SpaceMotion),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
}
