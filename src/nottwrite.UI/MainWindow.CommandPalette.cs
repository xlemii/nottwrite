using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nottwrite.UI;

public partial class MainWindow
{
    private record Command(string Name, string Hint, string Shortcut, Action Run);

    private List<Command> _commands = new();
    private List<Command> _filtered = new();
    private int _cmdSelected;

    private void BuildCommands()
    {
        Command C(string name, string hint, string shortcut, Action run) => new(T(name), T(hint), shortcut, run);
        _commands = new()
        {
            C("Go to Notes",        "Personal library",        "Tab",    () => SwitchMode(AppMode.Notes)),
            C("Go to Type",         "Write on paper",          "Tab",    () => SwitchMode(AppMode.Type)),
            C("Go to Edit",         "Draw characters",         "Tab",    () => SwitchMode(AppMode.Edit)),
            C("New note",           "Create a notebook",       "",       () => { SwitchMode(AppMode.Notes); NewNote_Click(this, new RoutedEventArgs()); }),
            C("New folder",         "Create a category",       "",       () => { SwitchMode(AppMode.Notes); NewFolder_Click(this, new RoutedEventArgs()); }),
            C("Export font (.ttf)", "Build installable font",  "",       () => ExportFontButton_Click(this, new RoutedEventArgs())),
            C("Export as PNG",      "Save current page",       "",       () => ExportPngButton_Click(this, new RoutedEventArgs())),
            C("Export as PDF",      "Save current page",       "",       () => ExportPdfButton_Click(this, new RoutedEventArgs())),
            C("Export as SVG",      "Save current page",       "",       () => ExportSvgButton_Click(this, new RoutedEventArgs())),
            C("Import font / template", "Edit an existing font", "",     () => { SwitchMode(AppMode.Edit); ImportTemplate_Click(this, new RoutedEventArgs()); }),
            C("Settings",           "Hotkeys, themes, general","",       () => BtnSettings_Click(this, new RoutedEventArgs())),
            C("Keyboard shortcuts", "Show all shortcuts",      "F1",     () => ToggleShortcuts()),
            C("Bold",               "Toggle bold",             _hk.Label("Bold"),   () => { if (HasSelection) ApplyFormatToSelection(c => c with { Bold = !c.Bold }); else BoldToggle(); }),
            C("Italic",             "Toggle italic",           _hk.Label("Italic"), () => { if (HasSelection) ApplyFormatToSelection(c => c with { Italic = !c.Italic }); else ItalicToggle(); }),
            C("Undo",               "Undo last change",        _hk.Label("Undo"),   () => UndoCommand_Executed(this, null!)),
            C("Voice input",        "Dictate text",            _hk.Label("VoiceInput"), () => SpeechBtn_Click(this, new RoutedEventArgs())),
        };
    }

    private void ToggleCommandPalette()
    {
        if (CommandPaletteOverlay.Visibility == Visibility.Visible) { CloseCommandPalette(); return; }
        if (_commands.Count == 0) BuildCommands();
        CommandSearchBox.Text = "";
        FilterCommands("");
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandSearchBox.Focus();
    }

    private void CloseCommandPalette() => CommandPaletteOverlay.Visibility = Visibility.Collapsed;

    private void CommandSearch_TextChanged(object sender, TextChangedEventArgs e)
        => FilterCommands(CommandSearchBox.Text);

    private void FilterCommands(string q)
    {
        q = q.Trim();
        _filtered = string.IsNullOrEmpty(q)
            ? _commands.ToList()
            : _commands.Where(c => c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                || c.Hint.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _cmdSelected = 0;
        RenderCommandList();
    }

    private void RenderCommandList()
    {
        CommandListPanel.Children.Clear();
        for (int i = 0; i < _filtered.Count; i++)
        {
            var c = _filtered[i];
            bool sel = i == _cmdSelected;

            var name = new TextBlock { Text = c.Name, FontSize = 13,
                Foreground = GetBrush("PrimaryText"), VerticalAlignment = VerticalAlignment.Center };
            var hint = new TextBlock { Text = c.Hint, FontSize = 11,
                Foreground = GetBrush("SecondaryText"), Margin = new Thickness(0, 1, 0, 0) };
            var text = new StackPanel();
            text.Children.Add(name);
            text.Children.Add(hint);

            var dock = new DockPanel();
            if (!string.IsNullOrEmpty(c.Shortcut))
            {
                var kbd = new Border
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = GetBrush("Surface2Bg"), CornerRadius = new CornerRadius(4),
                    BorderBrush = GetBrush("AppBorderBrush"), BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 2, 7, 2),
                    Child = new TextBlock { Text = c.Shortcut, FontSize = 10,
                        Foreground = GetBrush("SecondaryText") },
                };
                DockPanel.SetDock(kbd, Dock.Right);
                dock.Children.Add(kbd);
            }
            dock.Children.Add(text);

            int idx = i;
            var row = new Border
            {
                CornerRadius = new CornerRadius(7), Padding = new Thickness(11, 8, 11, 8),
                Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand,
                Background = sel ? GetBrush("NavActiveBg") : new SolidColorBrush(Colors.Transparent),
                Child = dock,
            };
            row.MouseEnter += (_, _) => { _cmdSelected = idx; RenderCommandList(); };
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; RunSelectedCommand(idx); };
            CommandListPanel.Children.Add(row);
        }
    }

    private void HandleCommandPaletteKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: CloseCommandPalette(); e.Handled = true; break;
            case Key.Down:   _cmdSelected = Math.Min(_cmdSelected + 1, _filtered.Count - 1); RenderCommandList(); e.Handled = true; break;
            case Key.Up:     _cmdSelected = Math.Max(_cmdSelected - 1, 0); RenderCommandList(); e.Handled = true; break;
            case Key.Enter:  RunSelectedCommand(_cmdSelected); e.Handled = true; break;
        }
    }

    private void RunSelectedCommand(int idx)
    {
        if (idx < 0 || idx >= _filtered.Count) return;
        var cmd = _filtered[idx];
        CloseCommandPalette();
        Dispatcher.InvokeAsync(cmd.Run);   // run after close so focus is clean
    }

    private void CommandPalette_BackdropClick(object sender, MouseButtonEventArgs e) => CloseCommandPalette();
    private void CommandPalette_CardClick(object sender, MouseButtonEventArgs e) => e.Handled = true; // swallow
}
