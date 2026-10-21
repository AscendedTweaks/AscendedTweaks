using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TweakPilot.App.Services;

/// <summary>
/// Barrierefreiheit (Abschnitt 8.6): Knoepfe in Steuerelement-Vorlagen (zum Beispiel der Kopf eines Expanders) haben keinen
/// eigenen Namen. <c>A11y.NameParts="True"</c> gibt allen fokussierbaren Knoepfen in der Vorlage den Namen des Steuerelements.
/// </summary>
public static class A11y
{
    public static readonly DependencyProperty NamePartsProperty = DependencyProperty.RegisterAttached(
        "NameParts", typeof(bool), typeof(A11y), new PropertyMetadata(false, OnNamePartsChanged));

    public static bool GetNameParts(DependencyObject element) => (bool)element.GetValue(NamePartsProperty);

    public static void SetNameParts(DependencyObject element, bool value) => element.SetValue(NamePartsProperty, value);

    private static void OnNamePartsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is true)
        {
            element.Loaded += (_, _) => NameParts(element);
        }
    }

    private static void NameParts(FrameworkElement owner)
    {
        var name = AutomationProperties.GetName(owner);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var stack = new Stack<DependencyObject>();
        stack.Push(owner);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is ButtonBase { Focusable: true } button && string.IsNullOrEmpty(AutomationProperties.GetName(button)))
                {
                    AutomationProperties.SetName(button, name);
                }

                stack.Push(child);
            }
        }
    }
}
