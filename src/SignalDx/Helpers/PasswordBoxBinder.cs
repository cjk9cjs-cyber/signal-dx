using System.Windows;
using System.Windows.Controls;

namespace SignalDx.Helpers;

/// <summary>Two-way bind <see cref="PasswordBox.Password"/> (not a DP) to a view-model string.</summary>
public static class PasswordBoxBinder
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinder),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxBinder),
            new PropertyMetadata(false, OnBindPasswordChanged));

    static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached(
            "Updating",
            typeof(bool),
            typeof(PasswordBoxBinder),
            new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject obj) =>
        (string)obj.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject obj, string value) =>
        obj.SetValue(BoundPasswordProperty, value);

    public static bool GetBindPassword(DependencyObject obj) =>
        (bool)obj.GetValue(BindPasswordProperty);

    public static void SetBindPassword(DependencyObject obj, bool value) =>
        obj.SetValue(BindPasswordProperty, value);

    static bool GetUpdating(DependencyObject obj) =>
        (bool)obj.GetValue(UpdatingProperty);

    static void SetUpdating(DependencyObject obj, bool value) =>
        obj.SetValue(UpdatingProperty, value);

    static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;

        box.PasswordChanged -= PasswordChanged;
        if ((bool)e.NewValue)
            box.PasswordChanged += PasswordChanged;
    }

    static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        if (GetUpdating(box)) return;

        string next = e.NewValue as string ?? "";
        if (box.Password != next)
            box.Password = next;
    }

    static void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;
        SetUpdating(box, true);
        SetBoundPassword(box, box.Password);
        SetUpdating(box, false);
    }
}
