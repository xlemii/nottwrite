using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Windows.Media;

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
        Loaded += (_, _) => BuildRows();
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
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
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
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
        });
        nameStack.Children.Add(new TextBlock
        {
            Text = vm.Description,
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66))
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
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x7D, 0xC4))
        };
        var bindingBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
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
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
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

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}

public record HotkeyRowVm(string Id, string Name, string Description, string CurrentLabel);
