using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nottwrite.UI;

public partial class MainWindow
{
    // Selectable character categories for the font's alphabet.
    private static readonly (string Key, string Label, string Chars)[] CharCategories =
    {
        ("upper",    "A–Z",          "ABCDEFGHIJKLMNOPQRSTUVWXYZ"),
        ("lower",    "a–z",          "abcdefghijklmnopqrstuvwxyz"),
        ("digits",   "0–9",          "0123456789"),
        ("punct",    "Punctuation",  ".,!?:;'\"-"),
        ("brackets", "Brackets <>",  "()[]{}<>"),
        ("symbols",  "Symbols",      "@#&*/+=%$^~`|\\_"),
        ("currency", "Currency",     "$€£¥"),
        ("polish",   "Polish",       "ĄĘÓŚŹŻĆŃŁąęóśźżćńł"),
    };

    // default = the original built-in set
    private HashSet<string> _enabledCats = new() { "upper", "lower", "digits", "punct", "polish" };

    private string BuildCharSet()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (key, _, chars) in CharCategories)
            if (_enabledCats.Contains(key)) sb.Append(chars);
        foreach (var c in _customChars) sb.Append(c);
        return sb.ToString();
    }

    // preferences serialisation (called from settings load/save)
    private string EnabledCatsCsv => string.Join(",", _enabledCats);
    private void SetEnabledCatsCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return;
        var valid = CharCategories.Select(c => c.Key).ToHashSet();
        var set = csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim()).Where(valid.Contains).ToHashSet();
        if (set.Count > 0) _enabledCats = set;
    }

    private void ToggleCharCategory(string key)
    {
        if (!_enabledCats.Remove(key)) _enabledCats.Add(key);
        if (_enabledCats.Count == 0) _enabledCats.Add("upper");   // never empty
        SaveSettings();
        RefreshCharCategories();
        CreateCharacterGrid();
        UpdateAlphabetGridHeight();
        AlphabetEditCanvas?.InvalidateVisual();
        FontPreviewCanvas?.InvalidateVisual();
        UpdateAlphabetProgress();
    }

    private void CharSetBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshCharCategories();
        CharSetPopup.IsOpen = true;
    }

    private void RefreshCharCategories()
    {
        // live count on the button
        if (CharSetCount != null)
            CharSetCount.Text = $"· {_enabledCats.Count}/{CharCategories.Length}";

        if (CharCatPanel == null) return;
        CharCatPanel.Children.Clear();

        foreach (var (key, label, chars) in CharCategories)
        {
            bool on = _enabledCats.Contains(key);

            // toggle indicator (filled accent + check when on)
            var box = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(5),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                Background = on ? GetBrush("AccentBrush") : new SolidColorBrush(Colors.Transparent),
                BorderBrush = on ? GetBrush("AccentBrush") : GetBrush("AppBorderBrush"),
                BorderThickness = new Thickness(1.5),
                Child = new TextBlock
                {
                    Text = on ? "✓" : "", FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var name = new TextBlock { Text = label, FontSize = 12.5,
                Foreground = GetBrush("PrimaryText"), VerticalAlignment = VerticalAlignment.Center };
            string sample = chars.Length > 14 ? chars[..14] + "…" : chars;
            var ex = new TextBlock { Text = sample, FontSize = 11, FontFamily = new FontFamily("Consolas"),
                Foreground = GetBrush("SecondaryText"), Opacity = on ? 0.85 : 0.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(name);
            text.Children.Add(ex);

            var rowGrid = new StackPanel { Orientation = Orientation.Horizontal };
            rowGrid.Children.Add(box);
            rowGrid.Children.Add(text);

            var row = new Border
            {
                CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 7, 12, 7),
                Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Colors.Transparent), Child = rowGrid,
            };
            string k = key;
            row.MouseEnter += (_, _) => row.Background = GetBrush("NavActiveBg");
            row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Colors.Transparent);
            row.MouseLeftButtonUp += (_, _) => ToggleCharCategory(k);
            CharCatPanel.Children.Add(row);
        }
    }
}
