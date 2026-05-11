using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DungeonMasterCortex.Services;

public static class TextBoxSelectAllBehavior
{
    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllOnFocus",
            typeof(bool),
            typeof(TextBoxSelectAllBehavior),
            new PropertyMetadata(false, OnSelectAllOnFocusChanged));

    public static bool GetSelectAllOnFocus(DependencyObject obj)
        => (bool)obj.GetValue(SelectAllOnFocusProperty);

    public static void SetSelectAllOnFocus(DependencyObject obj, bool value)
        => obj.SetValue(SelectAllOnFocusProperty, value);

    private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
            return;

        if ((bool)e.NewValue)
        {
            textBox.GotKeyboardFocus += TextBox_GotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
        }
        else
        {
            textBox.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown -= TextBox_PreviewMouseLeftButtonDown;
        }
    }

    private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.SelectAll();
    }

    private static void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (!textBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            textBox.Focus();
        }
    }
}
