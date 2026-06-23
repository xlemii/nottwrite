using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;

namespace HandwritingFontCreator.UI;

public partial class SettingsWindow : Window
{
    private readonly HotkeyManager _hk;
    private string? _capturingId;
    private readonly ObservableCollection<HotkeyRowVm> _rows = [];

    public SettingsWindow(HotkeyManager hk)
    {
        _hk = hk;
        InitializeComponent();
        Loaded += (_, _) => { SyncTheme(); BuildRows(); };
    }

    private void SyncTheme()
    {
        if (Owner is not MainWindow mw) return;
        var theme = MainWindow.Themes.FirstOrDefault(t => t.Id == mw._currentThemeId);
        if (theme == default) return;
        foreach (var (key, hex) in theme.Colors)
        {
            hex.TrimStart('#');
            if (hex.Length == 7 || hex.Length == 6)
            {
                var s = hex.TrimStart('#');
                if (s.Length == 6
                    && byte.TryParse(s[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)
                    && byte.TryParse(s[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)
                    && byte.TryParse(s[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
                    Resources[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }
    }

    private void BuildRows()
    {
        _rows.Clear();
        HotkeyPanel.Children.Clear();
        foreach (var def in HotkeyManager.Definitions)
        {
            var vm = new HotkeyRowVm(def.Id, def.Name, def.Description, _hk.Label(def.Id));
            _rows.Add(vm);
            HotkeyPanel.Children.Add(BuildRow(vm));
        }
    }

    private FrameworkElement BuildRow(HotkeyRowVm vm)
    {
        var border = new Border
        {
            Background = (SolidColorBrush)(Resources["CardBg"] ?? new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E))),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(8, 6, 8, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });

        // Name + description
        var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock
        {
            Text = vm.Name,
            FontSize = 12,
            Foreground = (SolidColorBrush)(Resources["PrimaryText"] ?? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)))
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = vm.Description,
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = (SolidColorBrush)(Resources["SecondaryText"] ?? new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)))
        });
        Grid.SetColumn(nameStack, 0);

        // Binding label
        var bindingLabel = new TextBlock
        {
            Name = $"lbl_{vm.Id}",
            Text = vm.CurrentLabel,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (SolidColorBrush)(Resources["AccentBrush"] ?? new SolidColorBrush(Color.FromRgb(0x8B, 0x7D, 0xC4)))
        };
        var bindingBorder = new Border
        {
            Background = (SolidColorBrush)(Resources["Surface2Bg"] ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))),
            BorderBrush = (SolidColorBrush)(Resources["AppBorderBrush"] ?? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 4, 0),
            Child = bindingLabel
        };
        Grid.SetColumn(bindingBorder, 1);

        // Change button
        var changeBtn = new Button
        {
            Content = "Change",
            Height = 26,
            Tag = (vm.Id, bindingLabel),
            Cursor = Cursors.Hand,
            Background = (SolidColorBrush)(Resources["ButtonBg"] ?? new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E))),
            Foreground = (SolidColorBrush)(Resources["PrimaryText"] ?? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))),
            BorderBrush = (SolidColorBrush)(Resources["AppBorderBrush"] ?? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44))),
            BorderThickness = new Thickness(1),
            FontSize = 11
        };
        changeBtn.Template = MakeButtonTemplate();
        changeBtn.Click += ChangeHotkey_Click;
        Grid.SetColumn(changeBtn, 2);

        grid.Children.Add(nameStack);
        grid.Children.Add(bindingBorder);
        grid.Children.Add(changeBtn);
        border.Child = grid;
        return border;
    }

    private static ControlTemplate MakeButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty,
            new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.SetBinding(Border.PaddingProperty,
            new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        template.VisualTree = border;
        return template;
    }

    private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var (id, lbl) = ((string, TextBlock))btn.Tag;

        _capturingId = id;
        StatusLabel.Text = $"Press a key for '{HotkeyManager.GetDef(id).Name}'...";
        lbl.Text = "...";
        btn.IsEnabled = false;

        PreviewKeyDown += Capture;

        void Capture(object _, KeyEventArgs ke)
        {
            ke.Handled = true;
            PreviewKeyDown -= Capture;
            btn.IsEnabled = true;

            var key = ke.Key == Key.System ? ke.SystemKey : ke.Key;
            if (key == Key.Escape)
            {
                lbl.Text = _hk.Label(id);
                StatusLabel.Text = "Cancelled.";
                _capturingId = null;
                return;
            }

            var mods = Keyboard.Modifiers;
            _hk.Set(id, key, mods);
            lbl.Text = _hk.Label(id);
            StatusLabel.Text = $"Saved: {lbl.Text}";
            _capturingId = null;

            Dispatcher.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1500);
                if (StatusLabel.Text.StartsWith("Saved"))
                    StatusLabel.Text = "";
            });
        }
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _hk.ResetAll();
        BuildRows();
        StatusLabel.Text = "All hotkeys reset to defaults.";
    }

    // ─── Tab switching ────────────────────────────────────────────
    private void TabHotkeys_Click(object sender, RoutedEventArgs e) => SetTab("hotkeys");
    private void TabThemes_Click(object sender, RoutedEventArgs e)  => SetTab("themes");
    private void TabGeneral_Click(object sender, RoutedEventArgs e) => SetTab("general");

    private void SetTab(string tab)
    {
        HotkeysPanel.Visibility = tab == "hotkeys" ? Visibility.Visible : Visibility.Collapsed;
        ThemesPanel.Visibility  = tab == "themes"  ? Visibility.Visible : Visibility.Collapsed;
        GeneralPanel.Visibility = tab == "general" ? Visibility.Visible : Visibility.Collapsed;

        SetTabStyle(TabHotkeysBtn, tab == "hotkeys");
        SetTabStyle(TabThemesBtn,  tab == "themes");
        SetTabStyle(TabGeneralBtn, tab == "general");

        if (tab == "themes" && ThemeList.Children.Count == 0)
            BuildThemes();
        if (tab == "general")
            SyncGeneralPanel();
    }

    private void SyncGeneralPanel()
    {
        if (Owner is not MainWindow mw) return;
        TiltCheckBox.IsChecked       = mw.TiltEnabled;
        AutoSaveCheckBox.IsChecked   = mw.AutoSaveEnabled;
        // select matching interval in combo
        foreach (ComboBoxItem item in AutoSaveIntervalCombo.Items)
        {
            if (item.Tag is string tagStr && int.TryParse(tagStr, out int mins) && mins == mw.AutoSaveMinutes)
            {
                AutoSaveIntervalCombo.SelectedItem = item;
                break;
            }
        }
        AutoSaveIntervalCombo.IsEnabled = mw.AutoSaveEnabled;
    }

    private void TiltCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (Owner is not MainWindow mw) return;
        mw.TiltEnabled = TiltCheckBox.IsChecked == true;
        mw.SaveSettings();
        mw.RefreshNotesGrid();
    }

    private void AutoSaveCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (Owner is not MainWindow mw) return;
        mw.AutoSaveEnabled = AutoSaveCheckBox.IsChecked == true;
        AutoSaveIntervalCombo.IsEnabled = mw.AutoSaveEnabled;
        mw.SaveSettings();
        mw.StartAutoSaveTimer();
    }

    private void AutoSaveInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Owner is not MainWindow mw) return;
        if (AutoSaveIntervalCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tagStr2
            && int.TryParse(tagStr2, out int mins))
        {
            mw.AutoSaveMinutes = mins;
            mw.SaveSettings();
            mw.StartAutoSaveTimer();
        }
    }

    private static void SetTabStyle(Button btn, bool active)
    {
        btn.Background   = active
            ? new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24))
            : new SolidColorBrush(Colors.Transparent);
        btn.BorderBrush  = active
            ? new SolidColorBrush(Color.FromRgb(0x8B, 0x7D, 0xC4))
            : new SolidColorBrush(Colors.Transparent);
        btn.Foreground   = active
            ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
            : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    }

    // ─── Theme picker ─────────────────────────────────────────────
    private void BuildThemes()
    {
        ThemeList.Children.Clear();
        var mainWin = Owner as MainWindow;
        string active = mainWin?._currentThemeId ?? "dark";

        foreach (var (id, name, colors) in MainWindow.Themes)
        {
            bool isActive = id == active;
            var card = BuildThemeCard(id, name, colors, isActive);
            ThemeList.Children.Add(card);
        }
    }

    private FrameworkElement BuildThemeCard(string id, string name, Dictionary<string, string> colors, bool isActive)
    {
        // Colors from THIS card's theme palette (mini-preview)
        string bg    = colors.GetValueOrDefault("AppBg",      "#1E1E1E");
        string panel = colors.GetValueOrDefault("PanelBg",    "#282828");
        string acc   = colors.GetValueOrDefault("AccentBrush","#8B7DC4");
        string txt   = colors.GetValueOrDefault("PrimaryText","#F0F0F0");

        // Colors for the label strip — use current active theme so it blends
        var mwColors = (Owner as MainWindow) is { } mw2
            ? MainWindow.Themes.FirstOrDefault(t => t.Id == mw2._currentThemeId).Colors
            : null;
        string labelBg  = mwColors?.GetValueOrDefault("CardBg",      "#2E2E2E") ?? "#2E2E2E";
        string labelTxt = mwColors?.GetValueOrDefault("PrimaryText",  "#F0F0F0") ?? "#F0F0F0";
        string borderCol = mwColors?.GetValueOrDefault("AppBorderBrush","#444444") ?? "#444444";

        var outer = new Border
        {
            CornerRadius    = new CornerRadius(8),
            BorderThickness = new Thickness(isActive ? 2 : 1),
            BorderBrush     = isActive
                ? new SolidColorBrush(ParseHex(acc))
                : new SolidColorBrush(ParseHex(borderCol)),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand,
            Tag    = id
        };

        // mini preview
        var preview = new Border
        {
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Height       = 60,
            Background   = new SolidColorBrush(ParseHex(bg))
        };
        var previewGrid = new Grid();
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border
        {
            Background   = new SolidColorBrush(ParseHex(panel)),
            CornerRadius = new CornerRadius(6, 0, 0, 0)
        };
        Grid.SetColumn(sidebar, 0);

        var accentDot = new Border
        {
            Width = 14, Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(ParseHex(acc)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        sidebar.Child = accentDot;

        var lines = new StackPanel { Margin = new Thickness(12, 14, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < 3; i++)
            lines.Children.Add(new Border {
                Height = 3, CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 0, 6),
                Width  = i == 1 ? double.NaN : (i == 0 ? 90 : 60),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(ParseHex(txt)) { Opacity = 0.25 + i * 0.18 }
            });
        Grid.SetColumn(lines, 1);

        previewGrid.Children.Add(sidebar);
        previewGrid.Children.Add(lines);
        preview.Child = previewGrid;

        var labelBorder = new Border
        {
            Padding    = new Thickness(12, 7, 12, 7),
            Background = new SolidColorBrush(ParseHex(labelBg)),
            CornerRadius = new CornerRadius(0, 0, 6, 6)
        };
        var labelGrid = new Grid();
        labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        labelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock
        {
            Text       = name,
            FontSize   = 12,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(ParseHex(labelTxt))
        };

        var checkDot = new Border
        {
            Width = 8, Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(ParseHex(acc)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Visibility = isActive ? Visibility.Visible : Visibility.Collapsed
        };

        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(checkDot,  1);
        labelGrid.Children.Add(labelText);
        labelGrid.Children.Add(checkDot);
        labelBorder.Child = labelGrid;

        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(labelBorder);
        outer.Child = stack;

        outer.MouseLeftButtonDown += (_, _) =>
        {
            if (Owner is MainWindow mw)
            {
                mw.ApplyTheme(id);
                ThemeList.Children.Clear();
                foreach (var (tid, tname, tcolors) in MainWindow.Themes)
                    ThemeList.Children.Add(BuildThemeCard(tid, tname, tcolors, tid == id));
            }
        };

        return outer;
    }

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex[2..4], 16);
            byte b = Convert.ToByte(hex[4..6], 16);
            return Color.FromRgb(r, g, b);
        }
        return Colors.Gray;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}

public record HotkeyRowVm(string Id, string Name, string Description, string CurrentLabel);
