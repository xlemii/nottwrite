using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nottwrite.UI;

public partial class MainWindow
{
    private void ToggleShortcuts()
    {
        if (ShortcutsOverlay.Visibility == Visibility.Visible)
        {
            ShortcutsOverlay.Visibility = Visibility.Collapsed;
            return;
        }
        BuildShortcuts();
        ShortcutsOverlay.Visibility = Visibility.Visible;
        // focus inside so TabNavigation=Cycle traps Tab within the dialog
        Dispatcher.InvokeAsync(() => ShortcutsCloseBtn.Focus(),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseShortcuts() => ShortcutsOverlay.Visibility = Visibility.Collapsed;
    private void ShortcutsBackdrop_Click(object sender, MouseButtonEventArgs e) => CloseShortcuts();
    private void ShortcutsCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void ShortcutsClose_Click(object sender, RoutedEventArgs e) => CloseShortcuts();

    // Rebuild each time so labels follow language + remapped hotkeys.
    private void BuildShortcuts()
    {
        ShortcutsPanel.Children.Clear();
        ShortcutsTitle.Text = T("Keyboard shortcuts");

        void Section(string title)
        {
            ShortcutsPanel.Children.Add(new TextBlock
            {
                Text = T(title), FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("SecondaryText"),
                Margin = new Thickness(0, 14, 0, 6),
            });
        }
        void Row(string action, string keys)
        {
            var dock = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var kbd = new Border
            {
                Background = GetBrush("Surface2Bg"), CornerRadius = new CornerRadius(4),
                BorderBrush = GetBrush("AppBorderBrush"), BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = keys, FontSize = 11, Foreground = GetBrush("PrimaryText") },
            };
            DockPanel.SetDock(kbd, Dock.Right);
            dock.Children.Add(kbd);
            dock.Children.Add(new TextBlock
            {
                Text = T(action), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                Foreground = GetBrush("PrimaryText"),
            });
            ShortcutsPanel.Children.Add(dock);
        }

        Section("GENERAL");
        Row("Command palette", "Ctrl+K");
        Row("Keyboard shortcuts", "F1");
        Row("Switch mode", _hk.Label("SwitchMode"));
        Row("Settings", "—");

        Section("EDIT");
        Row("Previous character", _hk.Label("NavLeft"));
        Row("Next character", _hk.Label("NavRight"));
        Row("Eraser", _hk.Label("ToggleEraser"));
        Row("Undo", _hk.Label("Undo"));
        Row("Redo", _hk.Label("Redo"));
        Row("Zoom canvas", "Ctrl + scroll");

        Section("TYPE & NOTES");
        Row("Save", "Ctrl+S");
        Row("Find in note", "Ctrl+F");
        Row("Bold", _hk.Label("Bold"));
        Row("Italic", _hk.Label("Italic"));
        Row("Underline", "Ctrl+U");
        Row("Voice input", _hk.Label("VoiceInput"));
        Row("Open note", "Enter");
    }
}
