using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DungeonMasterCortex.Views;

internal static class DescriptionPopupService
{
    private static Window? _activePopup;

    private static void ClosePopupSafely(Window? window)
    {
        if (window is null)
            return;

        try
        {
            if (window.IsLoaded)
                window.Close();
        }
        catch
        {
            // Swallow close races from rapid repeated clicks.
        }
    }

    public static void Show(Window? owner, string title, string text)
    {
        // Keep only one popup alive at a time to avoid stacked focus/click handlers.
        if (_activePopup is not null)
            ClosePopupSafely(_activePopup);

        string safeTitle = string.IsNullOrWhiteSpace(title) ? "Description" : title;
        string safeText = string.IsNullOrWhiteSpace(text) ? "(no description available)" : text;
        IInputElement? restoreFocusTarget = owner is null
            ? Keyboard.FocusedElement
            : FocusManager.GetFocusedElement(owner) ?? Keyboard.FocusedElement;

        var window = new Window
        {
            Title = safeTitle,
            Width = 560,
            Height = 340,
            WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Background = Brushes.Black,
            Foreground = Brushes.White,
            Topmost = true,
        };

        var border = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C4A468")),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1610")),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = safeText,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDE2C7")),
                }
            }
        };

        window.Content = border;
        _activePopup = window;

        // Click anywhere inside the popup to close it.
        window.PreviewMouseDown += (_, _) => window.Close();
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space)
                window.Close();
        };

        MouseButtonEventHandler? ownerClickCloser = null;
        if (owner is not null)
        {
            // Close the popup on the next owner click and swallow that click so it
            // doesn't activate accidental buttons/navigation under the pointer.
            ownerClickCloser = (_, me) =>
            {
                // Allow right-click to continue so users can immediately open another description.
                if (me.ChangedButton == MouseButton.Left)
                    me.Handled = true;

                ClosePopupSafely(window);
            };
            owner.PreviewMouseDown += ownerClickCloser;
        }

        // Also dismiss if focus goes elsewhere (alt-tab, click another app, etc.).
        window.Deactivated += (_, _) => ClosePopupSafely(window);

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activePopup, window))
                _activePopup = null;

            if (owner is not null && ownerClickCloser is not null)
                owner.PreviewMouseDown -= ownerClickCloser;

            if (owner is null)
                return;

            owner.Dispatcher.BeginInvoke(() =>
            {
                if (!owner.IsVisible)
                    return;

                owner.Activate();
                if (restoreFocusTarget is not null)
                    Keyboard.Focus(restoreFocusTarget);
                else if (owner.Content is IInputElement ownerContent)
                    Keyboard.Focus(ownerContent);
            }, DispatcherPriority.Input);
        };

        try
        {
            window.Show();
        }
        catch
        {
            if (ReferenceEquals(_activePopup, window))
                _activePopup = null;
        }
    }
}
