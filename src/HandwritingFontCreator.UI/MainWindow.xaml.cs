using HandwritingFontCreator.Core.Models;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


namespace HandwritingFontCreator.UI;

public partial class MainWindow : Window
{
    private bool _isDrawing;
    private Polyline? _currentLine;

    private readonly List<List<Point>> _strokes = [];
    private List<Point>? _currentStroke;
    private readonly Stack<List<Point>> _redoStack = [];

    private string CurrentTemplate = "Default";
    private string CurrentCharacter = "A";
    private Button? _selectedCharacterButton;
    private bool _hasUnsavedChanges;
    private bool _isDarkTheme = true;
    private int _lineSpacing      = 100;
    private double _lineThickness = 1.0;
    private double _fontSize      = 180.0;
    private int  _currentVariant  = 1;
    private bool _showGhost       = true;
    private double _taperAmount     = 3.5;
    private double _strokeWidth     = 3.5;
    private double _letterSpacing   = 0.0;
    private double _genStrokeWidth  = 3.5;

    private static readonly string BaseChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "abcdefghijklmnopqrstuvwxyz" +
        "0123456789" +
        ".,!?():-;'@#&";

    private readonly List<char> _customChars = [];

    private string AllChars => BaseChars + new string([.. _customChars]);

    private static string CustomCharsFilePath =>
        System.IO.Path.Combine(
            @"C:\Users\matys\Desktop\projects\nottwrite\templates",
            "custom_chars.txt");

    private void LoadCustomChars()
    {
        _customChars.Clear();
        if (!File.Exists(CustomCharsFilePath)) return;
        foreach (char c in File.ReadAllText(CustomCharsFilePath))
            if (!BaseChars.Contains(c) && !_customChars.Contains(c))
                _customChars.Add(c);
    }

    private void SaveCustomChars() =>
        File.WriteAllText(CustomCharsFilePath, new string([.. _customChars]));

    // ─── Brush helpers ───────────────────────────────────────────
    private Brush GetBrush(string key) => (Brush)FindResource(key);

    private Brush StrokeBrush       => GetBrush("StrokeBrush");
    private Brush LoadedStrokeBrush => GetBrush("LoadedStrokeBrush");
    private Brush NotebookLineBrush => GetBrush("NotebookLineBrush");

    // ─── Theme ───────────────────────────────────────────────────
    private void ApplyTheme(bool dark)
    {
        var r = Resources;

        if (dark)
        {
            r["AppBg"]             = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            r["PanelBg"]           = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
            r["Surface2Bg"]        = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
            r["AccentBrush"]       = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
            r["PrimaryText"]       = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
            r["SecondaryText"]     = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85));
            r["AppBorderBrush"]    = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
            r["ButtonBg"]          = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C));
            r["CanvasBg"]          = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            r["CharExistsBg"]      = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x1E));
            r["CharMissingBg"]     = new SolidColorBrush(Color.FromRgb(0x3A, 0x1E, 0x1E));
            r["CharExistsFg"]      = new SolidColorBrush(Color.FromRgb(0x6D, 0xB9, 0x6D));
            r["CharMissingFg"]     = new SolidColorBrush(Color.FromRgb(0xC9, 0x6C, 0x6C));
            r["StrokeBrush"]       = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            r["LoadedStrokeBrush"] = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
            r["NotebookLineBrush"] = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
        }
        else
        {
            r["AppBg"]             = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            r["PanelBg"]           = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            r["Surface2Bg"]        = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA));
            r["AccentBrush"]       = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
            r["PrimaryText"]       = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            r["SecondaryText"]     = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
            r["AppBorderBrush"]    = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
            r["ButtonBg"]          = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            r["CanvasBg"]          = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            r["CharExistsBg"]      = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9));
            r["CharMissingBg"]     = new SolidColorBrush(Color.FromRgb(0xF8, 0xD7, 0xD7));
            r["CharExistsFg"]      = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
            r["CharMissingFg"]     = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            r["StrokeBrush"]       = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            r["LoadedStrokeBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
            r["NotebookLineBrush"] = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        }

        RedrawCurrentStrokes();
        CreateCharacterGrid();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        ThemeToggleButton.Content = _isDarkTheme ? "☀  Light Mode" : "🌙  Dark Mode";
        ApplyTheme(_isDarkTheme);
    }

    private void VarBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (!int.TryParse(btn.Tag?.ToString(), out int v)) return;
        if (_hasUnsavedChanges) SaveCurrentCharacterSilently();
        _currentVariant = v;
        UpdateVariantButtons();
        LoadCurrentCharacter();
    }

    private void UpdateVariantButtons()
    {
        var accent = GetBrush("AccentBrush");
        var normal = GetBrush("ButtonBg");
        Var1Btn.Background = _currentVariant == 1 ? accent : normal;
        Var2Btn.Background = _currentVariant == 2 ? accent : normal;
        Var3Btn.Background = _currentVariant == 3 ? accent : normal;
    }

    private void GhostToggle_Click(object sender, RoutedEventArgs e)
    {
        _showGhost = !_showGhost;
        GhostToggleBtn.Background = _showGhost ? GetBrush("AccentBrush") : GetBrush("ButtonBg");
        RedrawCurrentStrokes();
    }

    private void DrawOptionsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool visible = DrawOptionsPanel.Visibility == Visibility.Visible;
        DrawOptionsPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        DrawOptionsToggleBtn.Background = visible ? GetBrush("ButtonBg") : GetBrush("AccentBrush");
    }

    // ─── Custom char add ─────────────────────────────────────────
    // + button just focuses the TextBox so user can type directly
    private void AddCustomChar_Click(object sender, RoutedEventArgs e) =>
        CustomCharInput.Focus();

    private void TaperSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _taperAmount = TaperSlider.Value;
        TaperValueLabel.Text = _taperAmount.ToString("0.#");
        RedrawCurrentStrokes();
    }

    private void StrokeWidthSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _strokeWidth = StrokeWidthSlider.Value;
        StrokeWidthLabel.Text = _strokeWidth.ToString("0.#");
        RedrawCurrentStrokes();
    }

    private void ApplyStrokeToLetter_Click(object sender, RoutedEventArgs e) =>
        RedrawCurrentStrokes();

    private void ApplyStrokeToTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                $"Apply taper={_taperAmount:0.#} width={_strokeWidth:0.#} to all letters in '{CurrentTemplate}'?\n\n" +
                "This only affects the preview — settings are not saved to .json files.",
                "Confirm", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            RefreshDisplay();
    }

    private void LetterSpacingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _letterSpacing = LetterSpacingSlider.Value;
        LetterSpacingLabel.Text = _letterSpacing.ToString("+0;-0;0") + "px";
        RefreshGeneratedText();
    }

    private void GenStrokeWidthSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _genStrokeWidth = GenStrokeWidthSlider.Value;
        GenStrokeWidthLabel.Text = _genStrokeWidth.ToString("0.#");
        RefreshGeneratedText();
    }

    private void CustomCharInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_resourcesReady) return;
        string txt = CustomCharInput.Text;
        if (string.IsNullOrEmpty(txt)) return;
        char c = txt[0];
        CustomCharInput.Text = "";

        // If char is already in AllChars, just navigate to it
        if (!AllChars.Contains(c))
        {
            _customChars.Add(c);
            SaveCustomChars();
            CreateCharacterGrid();
        }

        // Find button whose primary or secondary matches c
        SelectCharInGrid(c);
    }

    private void SelectCharInGrid(char c)
    {
        char upper = char.ToUpper(c);
        char lower = char.ToLower(c);

        foreach (Button btn in CharacterGrid.Children.OfType<Button>())
        {
            if (btn.Tag is not (char primary, char secondary)) continue;
            if (primary == c || secondary == c || primary == upper || primary == lower)
            {
                // Set CurrentCharacter to exact c before raising click
                if (_hasUnsavedChanges) SaveCurrentCharacterSilently();

                if (_selectedCharacterButton != null && _selectedCharacterButton != btn)
                {
                    var (op, os) = ((char, char))_selectedCharacterButton.Tag;
                    bool oe = CharacterExists(op.ToString()) || (os != '\0' && CharacterExists(os.ToString()));
                    _selectedCharacterButton.BorderThickness = new Thickness(1);
                    _selectedCharacterButton.BorderBrush     = GetBrush("AppBorderBrush");
                    _selectedCharacterButton.Background      = oe ? GetBrush("CharExistsBg") : GetBrush("CharMissingBg");
                }

                _selectedCharacterButton = btn;
                btn.BorderThickness = new Thickness(2);
                btn.BorderBrush     = GetBrush("AccentBrush");
                CurrentCharacter    = c.ToString();
                CurrentCharacterText.Text = CurrentCharacter;
                LoadCurrentCharacter();
                break;
            }
        }
    }

    // ─── Init ────────────────────────────────────────────────────
    private string GetTemplateFolder()
    {
        string folder =
            System.IO.Path.Combine(
                @"C:\Users\matys\Desktop\projects\nottwrite\templates",
                CurrentTemplate);

        Directory.CreateDirectory(folder);
        return folder;
    }

    public MainWindow()
    {
        InitializeComponent();

        Resources["StrokeBrush"]       = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        Resources["LoadedStrokeBrush"] = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
        Resources["NotebookLineBrush"] = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
        _resourcesReady = true;

        LoadCustomChars();
        RefreshTemplateComboBox("Default");

        Loaded += (_, _) =>
        {
            DrawNotebookLines();
            FontSizeLabel.Text = $"{_fontSize:0}px";
            UpdateVariantButtons();
            GhostToggleBtn.Background = GetBrush("AccentBrush");
            TaperValueLabel.Text       = _taperAmount.ToString("0.#");
            StrokeWidthLabel.Text      = _strokeWidth.ToString("0.#");
            LetterSpacingLabel.Text    = _letterSpacing.ToString("+0;-0;0") + "px";
            GenStrokeWidthLabel.Text   = _genStrokeWidth.ToString("0.#");
            SpacingLabel.Text          = $"{_lineSpacing}px";
            ThicknessLabel.Text        = $"{_lineThickness:0.#}px";
        };
        DrawingCanvas.SizeChanged += (_, _) => RedrawCurrentStrokes();

        CreateCharacterGrid();
        CurrentCharacter = "A";
        CurrentCharacterText.Text = "A";
    }

    // ─── Character grid ──────────────────────────────────────────
    private bool CharacterExists(string character) =>
        File.Exists(GetCharacterFilePath(character[0]));

    private void UpdateProgress()
    {
        int total     = AllChars.Length;
        int completed = AllChars.Count(c => File.Exists(GetCharacterFilePath(c, 1)));
        double percent = (double)completed / total * 100;
        AlphabetProgressBar.Value = percent;
        AlphabetProgressText.Text = $"{completed}/{total} ({percent:0}%)";
    }

    // Each button Tag = (primary char, secondary char or '\0')
    // Content is a Grid showing primary large + secondary small
    private void CreateCharacterGrid()
    {
        CharacterGrid.Children.Clear();

        // Build merged groups: letter pairs (A/a), standalone otherwise
        var groups = new List<(char primary, char secondary)>();
        for (char c = 'A'; c <= 'Z'; c++)
            groups.Add((c, char.ToLower(c)));
        foreach (char c in BaseChars.Where(ch => !char.IsLetter(ch)))
            groups.Add((c, '\0'));
        foreach (char c in _customChars.Where(ch => !char.IsLetter(ch)))
            groups.Add((c, '\0'));
        // Custom letter chars that aren't A-Z
        foreach (char c in _customChars.Where(ch => char.IsLetter(ch) && (ch < 'A' || ch > 'Z') && (ch < 'a' || ch > 'z')))
        {
            char upper = char.ToUpper(c);
            char lower = char.ToLower(c);
            if (upper != lower)
                groups.Add((upper, lower));
            else
                groups.Add((c, '\0'));
        }

        var existsBg = GetBrush("CharExistsBg");
        var missingBg = GetBrush("CharMissingBg");
        var existsFg  = GetBrush("CharExistsFg");
        var missingFg = GetBrush("CharMissingFg");
        var borderBr  = GetBrush("AppBorderBrush");
        var accentBr  = GetBrush("AccentBrush");
        var secondaryTxt = GetBrush("SecondaryText");

        foreach (var (primary, secondary) in groups)
        {
            bool primaryExists   = CharacterExists(primary.ToString());
            bool secondaryExists = secondary != '\0' && CharacterExists(secondary.ToString());
            bool anyExists       = primaryExists || secondaryExists;

            // Build button content: large primary + small secondary
            var content = new Grid();
            content.Children.Add(new TextBlock
            {
                Text               = primary.ToString(),
                FontSize           = secondary != '\0' ? 17 : 20,
                FontWeight         = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment  = VerticalAlignment.Center,
                Foreground         = primaryExists ? existsFg : missingFg,
            });
            if (secondary != '\0')
            {
                content.Children.Add(new TextBlock
                {
                    Text               = secondary.ToString(),
                    FontSize           = 10,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment  = VerticalAlignment.Bottom,
                    Margin             = new Thickness(0, 0, 3, 3),
                    Foreground         = secondaryExists ? existsFg : secondaryTxt,
                    Opacity            = 0.75,
                });
            }

            var button = new Button
            {
                Content         = content,
                Height          = 52,
                Margin          = new Thickness(3),
                Tag             = (primary, secondary),
                Background      = anyExists ? existsBg : missingBg,
                BorderThickness = new Thickness(1),
                BorderBrush     = borderBr,
            };

            button.Click += CharacterButton_Click;
            CharacterGrid.Children.Add(button);
        }

        UpdateProgress();
    }

    private void CharacterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (_hasUnsavedChanges) SaveCurrentCharacterSilently();

        var (primary, secondary) = ((char, char))button.Tag;

        // If clicking an already-selected button with a secondary char → toggle case
        if (button == _selectedCharacterButton && secondary != '\0')
        {
            bool isCurrentlyPrimary = CurrentCharacter == primary.ToString();
            CurrentCharacter = isCurrentlyPrimary ? secondary.ToString() : primary.ToString();
        }
        else
        {
            // Deselect old — restore its display to primary
            if (_selectedCharacterButton != null)
            {
                var (op, os) = ((char, char))_selectedCharacterButton.Tag;
                bool oe = CharacterExists(op.ToString()) || (os != '\0' && CharacterExists(os.ToString()));
                _selectedCharacterButton.BorderThickness = new Thickness(1);
                _selectedCharacterButton.BorderBrush     = GetBrush("AppBorderBrush");
                _selectedCharacterButton.Background      = oe ? GetBrush("CharExistsBg") : GetBrush("CharMissingBg");
                RefreshButtonDisplay(_selectedCharacterButton, op, os, showPrimary: true);
            }

            _selectedCharacterButton = button;
            button.BorderThickness   = new Thickness(2);
            button.BorderBrush       = GetBrush("AccentBrush");
            CurrentCharacter         = primary.ToString();
        }

        // Update the big label on the button to reflect current case
        RefreshButtonDisplay(button, primary, secondary, showPrimary: CurrentCharacter == primary.ToString());
        CurrentCharacterText.Text = CurrentCharacter;
        LoadCurrentCharacter();
    }

    private void RefreshButtonDisplay(Button btn, char primary, char secondary, bool showPrimary)
    {
        if (btn.Content is not Grid grid) return;
        char shown = showPrimary ? primary : secondary;
        char other = showPrimary ? secondary : primary;

        // Big label = shown char
        if (grid.Children.Count > 0 && grid.Children[0] is TextBlock big)
        {
            big.Text       = shown.ToString();
            bool exists    = CharacterExists(shown.ToString());
            big.Foreground = exists ? GetBrush("CharExistsFg") : GetBrush("CharMissingFg");
        }

        // Small label = other char (or same secondary slot, just update colour)
        if (grid.Children.Count > 1 && grid.Children[1] is TextBlock small && secondary != '\0')
        {
            small.Text       = other.ToString();
            bool exists      = CharacterExists(other.ToString());
            small.Foreground = exists ? GetBrush("CharExistsFg") : GetBrush("SecondaryText");
        }

        // Background reflects whether the shown char exists
        bool shownExists = CharacterExists(shown.ToString());
        bool otherExists = secondary != '\0' && CharacterExists(other.ToString());
        btn.Background = (shownExists || otherExists) ? GetBrush("CharExistsBg") : GetBrush("CharMissingBg");
    }

    private void LoadCurrentCharacter()
    {
        string filePath = GetCharacterFilePath(CurrentCharacter[0]);

        DrawingCanvas.Children.Clear();
        DrawNotebookLines();
        _strokes.Clear();

        if (!File.Exists(filePath))
            return;

        StrokeData? letter = JsonSerializer.Deserialize<StrokeData>(
            File.ReadAllText(filePath));
        if (letter == null)
            return;

        // Center character in drawing canvas
        double cw = DrawingCanvas.ActualWidth  > 0 ? DrawingCanvas.ActualWidth  : 260;
        double ch = DrawingCanvas.ActualHeight > 0 ? DrawingCanvas.ActualHeight : 260;
        double dx = (cw - letter.Width)  / 2;
        double dy = (ch - letter.Height) / 2;

        if (_showGhost) DrawGhostLetter();

        var brush = LoadedStrokeBrush;
        foreach (var stroke in letter.Strokes)
        {
            var pts = stroke.Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToList();
            _strokes.Add(pts);
            DrawingCanvas.Children.Add(_taperAmount > 0
                ? BuildTaperedPath(pts, brush, _strokeWidth + _taperAmount)
                : (UIElement)new Polyline
                    {
                        Stroke = brush, StrokeThickness = _strokeWidth,
                        StrokeLineJoin = PenLineJoin.Round,
                        Points = new PointCollection(pts)
                    });
        }

        _hasUnsavedChanges = false;
    }

    // ─── File paths ──────────────────────────────────────────────
    private static string CharSafeName(char c) => c switch
    {
        '.' => "period",   ',' => "comma",   '!' => "exclaim",
        '?' => "question", '(' => "lparen",  ')' => "rparen",
        ':' => "colon",    ';' => "semicol",  '\'' => "apostrophe",
        '@' => "at",       '#' => "hash",     '&' => "ampersand",
        '-' => "hyphen",
        _ => c.ToString()
    };

    private string GetSelectedFilePath() =>
        GetCharacterFilePath(CurrentCharacter[0], _currentVariant);

    private string GetCharacterFilePath(char character, int variant = 0)
    {
        int v = variant > 0 ? variant : _currentVariant;
        string prefix = char.IsDigit(character) ? "digit"
                      : char.IsUpper(character) ? "upper"
                      : char.IsLower(character) ? "lower"
                      : "special";
        string name   = CharSafeName(character);
        string suffix = v > 1 ? $"_v{v}" : "";
        return System.IO.Path.Combine(GetTemplateFolder(), $"{prefix}_{name}{suffix}.json");
    }

    private List<string> GetAllVariantPaths(char character)
    {
        var paths = new List<string>();
        for (int v = 1; v <= 3; v++)
        {
            string p = GetCharacterFilePath(character, v);
            if (File.Exists(p)) paths.Add(p);
        }
        return paths;
    }

    // ─── Save ────────────────────────────────────────────────────
    private string BuildJson()
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var stroke in _strokes)
            foreach (var p in stroke)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }

        var letter = new StrokeData
        {
            Width    = (maxX - minX) + 20,
            Height   = (maxY - minY) + 20,
            Baseline = maxY - minY
        };

        foreach (var originalStroke in _strokes)
        {
            var smoothed = SmoothStroke(originalStroke);
            var stroke   = new Stroke();
            foreach (var p in smoothed)
                stroke.Points.Add(new PointData { X = p.X - minX, Y = p.Y - minY });
            letter.Strokes.Add(stroke);
        }

        return JsonSerializer.Serialize(letter, new JsonSerializerOptions { WriteIndented = true });
    }

    private void SaveCurrentCharacterSilently()
    {
        if (_strokes.Count == 0)
            return;

        File.WriteAllText(GetSelectedFilePath(), BuildJson());
        _hasUnsavedChanges = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0)
        {
            MessageBox.Show("Draw a letter first");
            return;
        }

        Directory.CreateDirectory(GetTemplateFolder());
        string filePath = GetSelectedFilePath();
        File.WriteAllText(filePath, BuildJson());
        MessageBox.Show($"Saved {System.IO.Path.GetFileName(filePath)}");

        CreateCharacterGrid();
        _hasUnsavedChanges = false;
    }

    // ─── Load ────────────────────────────────────────────────────
    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        string filePath = GetSelectedFilePath();
        if (!File.Exists(filePath))
        {
            MessageBox.Show($"Not found: {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        StrokeData? letter = JsonSerializer.Deserialize<StrokeData>(
            File.ReadAllText(filePath));
        if (letter == null)
            return;

        DrawingCanvas.Children.Clear();
        DrawNotebookLines();

        var brush = LoadedStrokeBrush;
        foreach (var stroke in letter.Strokes)
        {
            var line = new Polyline { Stroke = brush, StrokeThickness = 2.5 };
            foreach (var point in stroke.Points)
                line.Points.Add(new Point(point.X, point.Y));
            DrawingCanvas.Children.Add(line);
        }
    }

    // ─── Drawing ─────────────────────────────────────────────────
    private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _redoStack.Clear();
        _hasUnsavedChanges = true;
        _isDrawing = true;
        if (_strokes.Count >= 50) _strokes.RemoveAt(0); // undo limit

        _currentLine = new Polyline { Stroke = StrokeBrush, StrokeThickness = 3 };

        Point point = e.GetPosition(DrawingCanvas);
        _currentStroke = [point];
        _strokes.Add(_currentStroke);
        _currentLine.Points.Add(point);
        DrawingCanvas.Children.Add(_currentLine);
        DrawingCanvas.CaptureMouse();
    }

    private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentLine == null)
            return;

        Point point = e.GetPosition(DrawingCanvas);
        _currentStroke?.Add(point);
        _currentLine.Points.Add(point);
    }

    private void DrawingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDrawing = false;
        DrawingCanvas.ReleaseMouseCapture();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _strokes.Clear();
        _redoStack.Clear();
        DrawingCanvas.Children.Clear();
        DrawNotebookLines();
        if (_showGhost) DrawGhostLetter();
    }

    // ─── Undo / Redo ─────────────────────────────────────────────
    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0)
            return;

        var last = _strokes[^1];
        _strokes.RemoveAt(_strokes.Count - 1);
        _redoStack.Push(last);
        RedrawCurrentStrokes();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0)
            return;

        _strokes.Add(_redoStack.Pop());
        RedrawCurrentStrokes();
    }

    private void RedrawCurrentStrokes()
    {
        DrawingCanvas.Children.Clear();
        DrawNotebookLines();
        if (_showGhost) DrawGhostLetter();

        var brush = StrokeBrush;
        foreach (var stroke in _strokes)
            DrawingCanvas.Children.Add(_taperAmount > 0
                ? BuildTaperedPath(stroke, brush, _strokeWidth + _taperAmount)
                : (UIElement)new Polyline
                    {
                        Stroke = brush, StrokeThickness = _strokeWidth,
                        StrokeLineJoin = PenLineJoin.Round,
                        Points = new PointCollection(stroke)
                    });
    }

    // ─── Ghost template ──────────────────────────────────────────
    private void DrawGhostLetter()
    {
        if (string.IsNullOrEmpty(CurrentCharacter)) return;
        double cw = DrawingCanvas.ActualWidth  > 0 ? DrawingCanvas.ActualWidth  : 260;
        double ch = DrawingCanvas.ActualHeight > 0 ? DrawingCanvas.ActualHeight : 260;
        double fs = ch * 0.72;

        var ft = new System.Windows.Media.FormattedText(
            CurrentCharacter,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fs, Brushes.Transparent,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var geo = ft.BuildGeometry(new Point((cw - ft.Width) / 2, (ch - ft.Height) / 2));
        DrawingCanvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data            = geo,
            Fill            = new SolidColorBrush(Color.FromArgb(18, 100, 149, 237)),
            Stroke          = new SolidColorBrush(Color.FromArgb(35, 100, 149, 237)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false
        });
    }

    // ─── Tapered stroke path ─────────────────────────────────────
    private static UIElement BuildTaperedPath(IList<Point> pts, Brush stroke, double maxW)
    {
        if (pts.Count < 2)
        {
            return new Ellipse
            {
                Width = maxW, Height = maxW,
                Fill  = stroke,
                Margin = new Thickness(pts.Count > 0 ? pts[0].X - maxW/2 : 0,
                                       pts.Count > 0 ? pts[0].Y - maxW/2 : 0, 0, 0)
            };
        }

        int n = pts.Count;
        var left  = new Point[n];
        var right = new Point[n];

        for (int i = 0; i < n; i++)
        {
            var prev = pts[Math.Max(0, i - 1)];
            var next = pts[Math.Min(n - 1, i + 1)];
            double dx = next.X - prev.X, dy = next.Y - prev.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) { left[i] = right[i] = pts[i]; continue; }
            double px = -dy / len, py = dx / len;
            double t = (double)i / (n - 1);
            double w = maxW * Math.Sin(Math.PI * t) * 0.5;
            left[i]  = new Point(pts[i].X + px * w, pts[i].Y + py * w);
            right[i] = new Point(pts[i].X - px * w, pts[i].Y - py * w);
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(left[0], isFilled: true, isClosed: true);
            for (int i = 1; i < n; i++) ctx.LineTo(left[i],  isStroked: false, isSmoothJoin: true);
            for (int i = n - 1; i >= 0; i--) ctx.LineTo(right[i], isStroked: false, isSmoothJoin: true);
        }
        geo.Freeze();
        return new System.Windows.Shapes.Path { Data = geo, Fill = stroke, IsHitTestVisible = false };
    }

    // ─── Notebook lines ──────────────────────────────────────────
    private void DrawNotebookLines()
    {
        DrawLinesOnCanvas(DrawingCanvas,
            DrawingCanvas.ActualWidth  > 0 ? DrawingCanvas.ActualWidth  : 2000,
            DrawingCanvas.ActualHeight > 0 ? DrawingCanvas.ActualHeight : 2000);
    }

    private void DrawDisplayLines(double width, double height)
    {
        DrawLinesOnCanvas(DisplayCanvas, width, height);
    }

    private void DrawLinesOnCanvas(Canvas canvas, double width, double height)
    {
        var brush   = NotebookLineBrush;
        int spacing = Math.Max(10, _lineSpacing);

        for (int y = spacing; y <= (int)height + spacing; y += spacing)
        {
            canvas.Children.Add(new Line
            {
                X1 = 0, X2 = width, Y1 = y, Y2 = y,
                Stroke          = brush,
                StrokeThickness = _lineThickness
            });
        }
    }

    private void FontSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DrawingCanvas == null) return;
        _fontSize = e.NewValue;
        FontSizeLabel.Text = $"{_fontSize:0}px";
        RefreshGeneratedText();
    }

    private void ExportMenuBtn_Click(object sender, RoutedEventArgs e) =>
        ExportPopup.IsOpen = !ExportPopup.IsOpen;

    private void SpacingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _lineSpacing = (int)e.NewValue;
        SpacingLabel.Text = $"{_lineSpacing}px";
        RedrawCurrentStrokes();
        RefreshDisplay();
    }

    private void ThicknessSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _lineThickness = e.NewValue;
        string fmt = _lineThickness % 1 == 0 ? $"{_lineThickness:0}px" : $"{_lineThickness:0.0}px";
        ThicknessLabel.Text = fmt;
        RedrawCurrentStrokes();
        RefreshDisplay();
    }

    // ─── Smoothing ───────────────────────────────────────────────
    private static List<Point> SmoothStroke(List<Point> points)
    {
        if (points.Count < 3)
            return points;

        var result = new List<Point>(points.Count) { points[0] };

        for (int i = 1; i < points.Count - 1; i++)
            result.Add(new Point(
                (points[i - 1].X + points[i].X + points[i + 1].X) / 3.0,
                (points[i - 1].Y + points[i].Y + points[i + 1].Y) / 3.0));

        result.Add(points[^1]);
        return result;
    }

    // ─── Generate / Preview ──────────────────────────────────────
    private bool _resourcesReady = false;
    private enum DisplayMode { None, Text, Alphabet }
    private DisplayMode _displayMode = DisplayMode.None;

    private void RefreshDisplay()
    {
        if (!_resourcesReady) return;
        if (_displayMode == DisplayMode.Text)     RefreshGeneratedText();
        else if (_displayMode == DisplayMode.Alphabet) DrawAlphabetPreview();
    }

    private void RefreshGeneratedText()
    {
        if (!_resourcesReady || DisplayCanvas == null || GenerateRichBox == null) return;
        _displayMode = DisplayMode.Text;
        DisplayCanvas.Children.Clear();
        double canvasW = DisplayCanvas.ActualWidth  > 0 ? DisplayCanvas.ActualWidth  : 1200;
        double canvasH = DisplayCanvas.ActualHeight > 0 ? DisplayCanvas.ActualHeight : 800;
        DisplayCanvas.Width  = canvasW;
        DisplayCanvas.Height = canvasH;
        DrawDisplayLines(canvasW, canvasH);
        DrawGeneratedText(DisplayCanvas);
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e) => RefreshGeneratedText();

    private void GenerateRichBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshGeneratedText();

    // ─── Formatting handlers ─────────────────────────────────────
    private void BoldBtn_Click(object sender, RoutedEventArgs e)
    {
        var sel = GenerateRichBox.Selection;
        var cur = sel.GetPropertyValue(TextElement.FontWeightProperty);
        bool isBold = cur != DependencyProperty.UnsetValue && (FontWeight)cur == FontWeights.Bold;
        sel.ApplyPropertyValue(TextElement.FontWeightProperty, isBold ? FontWeights.Normal : FontWeights.Bold);
        GenerateRichBox.Focus();
    }

    private void ItalicBtn_Click(object sender, RoutedEventArgs e)
    {
        var sel = GenerateRichBox.Selection;
        var cur = sel.GetPropertyValue(TextElement.FontStyleProperty);
        bool isItalic = cur != DependencyProperty.UnsetValue && (FontStyle)cur == FontStyles.Italic;
        sel.ApplyPropertyValue(TextElement.FontStyleProperty, isItalic ? FontStyles.Normal : FontStyles.Italic);
        GenerateRichBox.Focus();
    }

    private static readonly Dictionary<string, Brush> _colorMap = new()
    {
        ["Default"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
        ["Red"]     = new SolidColorBrush(Color.FromRgb(0xE0, 0x55, 0x55)),
        ["Green"]   = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
        ["Blue"]    = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)),
        ["Yellow"]  = new SolidColorBrush(Color.FromRgb(0xE5, 0xC0, 0x7B)),
        ["Orange"]  = new SolidColorBrush(Color.FromRgb(0xE0, 0x84, 0x5A)),
        ["Purple"]  = new SolidColorBrush(Color.FromRgb(0xC7, 0x92, 0xEA)),
    };

    private void ColorBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string key = btn.Tag?.ToString() ?? "Default";
        if (_colorMap.TryGetValue(key, out var brush))
            GenerateRichBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        GenerateRichBox.Focus();
    }

    private void PreviewAlphabetButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMode = DisplayMode.Alphabet;
        DrawAlphabetPreview();
    }

    private void DrawAlphabetPreview()
    {
        if (!_resourcesReady || DisplayCanvas == null) return;
        DisplayCanvas.Children.Clear();

        double canvasW        = DisplayCanvas.ActualWidth > 0 ? DisplayCanvas.ActualWidth : 1200;
        const double targetH  = 100;
        const double rowGap   = 30;
        double rowHeight      = targetH + rowGap;
        double offsetX = 20, offsetY = 20;

        var brush = LoadedStrokeBrush;
        foreach (char c in AllChars)
        {
            string filePath = GetCharacterFilePath(c, 1);
            if (!File.Exists(filePath)) continue;

            StrokeData? letter = JsonSerializer.Deserialize<StrokeData>(
                File.ReadAllText(filePath));
            if (letter == null) continue;

            double scale = letter.Height > 0 ? targetH / letter.Height : 1;
            double charW = letter.Width * scale;

            if (offsetX + charW > canvasW - 10) { offsetX = 20; offsetY += rowHeight; }

            foreach (var stroke in letter.Strokes)
            {
                var line = new Polyline { Stroke = brush, StrokeThickness = 2 };
                foreach (var p in stroke.Points)
                    line.Points.Add(new Point(offsetX + p.X * scale, offsetY + p.Y * scale));
                DisplayCanvas.Children.Add(line);
            }

            offsetX += charW + 15;
        }

        double totalH = offsetY + rowHeight + 20;
        DisplayCanvas.Width  = canvasW;
        DisplayCanvas.Height = totalH;
        DrawDisplayLines(canvasW, totalH);
    }

    // Measures the pixel width a word would take at current font size
    private double MeasureWordWidth(string word, bool bold)
    {
        double total = 0;
        double gap   = _fontSize * 0.06;
        foreach (char c in word)
        {
            string fp = GetCharacterFilePath(c, 1);
            if (!File.Exists(fp)) { total += _fontSize * 0.4; continue; }
            try
            {
                var sd = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(fp));
                if (sd == null || sd.Height <= 0) { total += _fontSize * 0.4; continue; }
                double scale = _fontSize / sd.Height;
                total += sd.Width * scale + gap;
            }
            catch { total += _fontSize * 0.4; }
        }
        return total;
    }

    private void DrawGeneratedText(Canvas canvas)
    {
        double offsetX      = 20, offsetY = 0;
        double targetHeight = _fontSize;
        double lineHeight   = _fontSize * 1.6;
        double baselineY    = _fontSize * 1.2;
        double canvasW      = canvas.Width > 0 ? canvas.Width
                            : canvas.ActualWidth > 0 ? canvas.ActualWidth : 1200;
        double margin       = 20;

        void RenderCharAt(char character, Brush stroke, bool bold, bool italic)
        {
            var variants = GetAllVariantPaths(character);
            if (variants.Count == 0) { offsetX += targetHeight * 0.4; return; }
            string fp2 = variants[Random.Shared.Next(variants.Count)];

            StrokeData? letter = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(fp2));
            if (letter == null) return;

            double scale          = letter.Height > 0 ? targetHeight / letter.Height : 1;
            double charW          = letter.Width * scale;
            double baselineOffset = baselineY + offsetY - letter.Baseline * scale;
            double thickness      = bold ? _genStrokeWidth * 1.6 : _genStrokeWidth;

            foreach (var s in letter.Strokes)
            {
                var pts = s.Points.Select(p =>
                {
                    double px = offsetX + p.X * scale;
                    double py = baselineOffset + p.Y * scale;
                    if (italic) px += (baselineOffset + letter.Baseline * scale - py) * 0.28;
                    return new Point(px, py);
                }).ToList();

                canvas.Children.Add(_taperAmount > 0
                    ? BuildTaperedPath(pts, stroke, thickness + _taperAmount)
                    : (UIElement)new Polyline
                        {
                            Stroke = stroke, StrokeThickness = thickness,
                            StrokeLineJoin = PenLineJoin.Round,
                            StrokeStartLineCap = PenLineCap.Round,
                            StrokeEndLineCap = PenLineCap.Round,
                            Points = new PointCollection(pts)
                        });
            }
            offsetX += charW + targetHeight * 0.06 + _letterSpacing;
        }

        // Collect runs with formatting tags to enable word-wrap by word
        var runs = new List<(string text, Brush fg, bool bold, bool italic)>();
        var doc = GenerateRichBox.Document;
        bool firstPara = true;
        foreach (var block in doc.Blocks)
        {
            if (!firstPara) runs.Add(("\n", StrokeBrush, false, false));
            firstPara = false;
            if (block is not Paragraph para) continue;
            foreach (var inline in para.Inlines)
            {
                if (inline is not Run run) continue;
                bool isBold   = run.FontWeight == FontWeights.Bold;
                bool isItalic = run.FontStyle  == FontStyles.Italic;
                Brush fg = (run.Foreground is SolidColorBrush scb && scb.Color.A > 0)
                           ? run.Foreground : StrokeBrush;
                runs.Add((run.Text, fg, isBold, isItalic));
            }
        }

        // Split into word-tokens preserving spaces/newlines, then render word by word
        var tokens = new List<(string word, Brush fg, bool bold, bool italic, bool isSpace, bool isNewLine)>();
        foreach (var (text, fg, bold, italic) in runs)
        {
            foreach (char ch in text)
            {
                if (ch == '\r') continue;
                if (ch == '\n') { tokens.Add(("", fg, bold, italic, false, true)); continue; }
                if (ch == ' ')  { tokens.Add((" ", fg, bold, italic, true,  false)); continue; }
                // Accumulate word chars into last token or create new word token
                if (tokens.Count > 0 && !tokens[^1].isSpace && !tokens[^1].isNewLine
                    && tokens[^1].fg == fg && tokens[^1].bold == bold && tokens[^1].italic == italic)
                {
                    var last = tokens[^1];
                    tokens[^1] = (last.word + ch, fg, bold, italic, false, false);
                }
                else
                {
                    tokens.Add((ch.ToString(), fg, bold, italic, false, false));
                }
            }
        }

        double spaceW = targetHeight * 0.38;

        foreach (var token in tokens)
        {
            if (token.isNewLine) { offsetX = margin; offsetY += lineHeight; continue; }
            if (token.isSpace)
            {
                if (offsetX > margin) offsetX += spaceW;
                continue;
            }

            // Measure entire word to decide wrap
            double wordW = MeasureWordWidth(token.word, token.bold);
            if (offsetX + wordW > canvasW - margin && offsetX > margin)
            { offsetX = margin; offsetY += lineHeight; }

            foreach (char c in token.word)
                RenderCharAt(c, token.fg, token.bold, token.italic);
        }

        if (canvas == DisplayCanvas)
        {
            double neededH = offsetY + lineHeight + 20;
            if (neededH > canvas.Height) canvas.Height = neededH;
        }
    }

    // ─── Export ──────────────────────────────────────────────────
    private void ExportPngButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory("exports");

        var exportCanvas = new Canvas
        {
            Width      = 3000,
            Height     = 800,
            Background = Brushes.White
        };

        DrawGeneratedText(exportCanvas);
        exportCanvas.Measure(new Size(exportCanvas.Width, exportCanvas.Height));
        exportCanvas.Arrange(new Rect(0, 0, exportCanvas.Width, exportCanvas.Height));

        var bitmap = new RenderTargetBitmap(
            (int)exportCanvas.Width, (int)exportCanvas.Height,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(exportCanvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        string filePath = System.IO.Path.Combine(
            "exports", $"{DateTime.Now:yyyyMMdd_HHmmss}.png");

        using var stream = File.Create(filePath);
        encoder.Save(stream);

        MessageBox.Show($"Saved PNG:\n{System.IO.Path.GetFullPath(filePath)}");
    }

    // ─── SVG Export ──────────────────────────────────────────────
    private void ExportSvgButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export SVG",
            FileName   = $"{CurrentTemplate}_{DateTime.Now:yyyyMMdd_HHmmss}.svg",
            DefaultExt = ".svg",
            Filter     = "SVG File (*.svg)|*.svg"
        };
        if (dlg.ShowDialog() != true) return;

        string svg = BuildSpecimenSvg();
        File.WriteAllText(dlg.FileName, svg, System.Text.Encoding.UTF8);
        MessageBox.Show($"Saved SVG:\n{dlg.FileName}");
    }

    private string BuildSpecimenSvg()
    {
        // Layout constants
        const double cellW      = 160;
        const double cellH      = 180;
        const double charHeight = 120;
        const double padding    = 20;
        const int    cols       = 10;
        const double labelH     = 20;

        var chars = AllChars.Where(c => File.Exists(GetCharacterFilePath(c, 1))).ToList();
        int rows  = (int)Math.Ceiling(chars.Count / (double)cols);

        double svgW = cols * cellW + padding * 2;
        double svgH = rows * cellH + padding * 2 + 40; // 40 = header

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
                      $"viewBox=\"0 0 {svgW:F1} {svgH:F1}\" " +
                      $"width=\"{svgW:F1}\" height=\"{svgH:F1}\">");
        sb.AppendLine($"  <title>{CurrentTemplate} — Handwriting Font Specimen</title>");
        sb.AppendLine($"  <rect width=\"{svgW:F1}\" height=\"{svgH:F1}\" fill=\"#1a1a1a\"/>");

        // Header
        sb.AppendLine($"  <text x=\"{padding}\" y=\"{padding + 24}\" " +
                      $"font-family=\"monospace\" font-size=\"18\" font-weight=\"bold\" fill=\"#569CD6\">" +
                      $"{EscapeXml(CurrentTemplate)}</text>");
        sb.AppendLine($"  <text x=\"{svgW - padding}\" y=\"{padding + 24}\" " +
                      $"font-family=\"monospace\" font-size=\"11\" fill=\"#666\" text-anchor=\"end\">" +
                      $"{chars.Count} characters · exported {DateTime.Now:yyyy-MM-dd}</text>");

        double startY = padding + 40;

        for (int i = 0; i < chars.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            double cellX = padding + col * cellW;
            double cellY = startY + row * cellH;

            char c = chars[i];
            string? paths = CharToSvgPaths(c, cellX, cellY + labelH, cellW, charHeight);
            if (paths == null) continue;

            // Cell background
            sb.AppendLine($"  <rect x=\"{cellX:F1}\" y=\"{cellY:F1}\" " +
                          $"width=\"{cellW:F1}\" height=\"{cellH:F1}\" " +
                          $"fill=\"#252526\" rx=\"4\"/>");

            // Char label
            sb.AppendLine($"  <text x=\"{cellX + 8:F1}\" y=\"{cellY + 15:F1}\" " +
                          $"font-family=\"monospace\" font-size=\"11\" fill=\"#569CD6\">" +
                          $"{EscapeXml(c.ToString())}</text>");

            // Baseline guide
            double baselineY = cellY + labelH + charHeight * 0.82;
            sb.AppendLine($"  <line x1=\"{cellX + 8:F1}\" y1=\"{baselineY:F1}\" " +
                          $"x2=\"{cellX + cellW - 8:F1}\" y2=\"{baselineY:F1}\" " +
                          $"stroke=\"#333\" stroke-width=\"0.5\"/>");

            sb.AppendLine(paths);
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private string? CharToSvgPaths(char c, double cellX, double cellY, double cellW, double cellH)
    {
        string fp = GetCharacterFilePath(c, 1);
        if (!File.Exists(fp)) return null;
        StrokeData? sd;
        try { sd = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(fp)); }
        catch { return null; }
        if (sd == null || sd.Strokes.Count == 0) return null;

        double scale     = sd.Height > 0 ? cellH / sd.Height : 1.0;
        double charW     = sd.Width * scale;
        double ox        = cellX + (cellW - charW) / 2;
        double oy        = cellY + cellH - sd.Baseline * scale;

        double maxW  = _genStrokeWidth;
        double taper = _taperAmount;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"  <g>");

        foreach (var stroke in sd.Strokes)
        {
            var pts = stroke.Points
                .Select(p => new Point(ox + p.X * scale, oy + (p.Y - sd.Height) * scale + cellH))
                .ToList();

            if (pts.Count == 0) continue;

            if (taper > 0 && pts.Count >= 2)
            {
                // Tapered filled shape
                int n = pts.Count;
                var left  = new Point[n];
                var right = new Point[n];
                for (int i = 0; i < n; i++)
                {
                    var prev = pts[Math.Max(0, i - 1)];
                    var next = pts[Math.Min(n - 1, i + 1)];
                    double dx = next.X - prev.X, dy = next.Y - prev.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 0.001) { left[i] = right[i] = pts[i]; continue; }
                    double px = -dy / len, py = dx / len;
                    double t = (double)i / (n - 1);
                    double w = (maxW + taper) * Math.Sin(Math.PI * t) * 0.5;
                    left[i]  = new Point(pts[i].X + px * w, pts[i].Y + py * w);
                    right[i] = new Point(pts[i].X - px * w, pts[i].Y - py * w);
                }

                var d = new System.Text.StringBuilder();
                d.Append($"M {left[0].X:F2},{left[0].Y:F2}");
                for (int i = 1; i < n; i++)
                    d.Append($" L {left[i].X:F2},{left[i].Y:F2}");
                for (int i = n - 1; i >= 0; i--)
                    d.Append($" L {right[i].X:F2},{right[i].Y:F2}");
                d.Append(" Z");

                sb.AppendLine($"    <path d=\"{d}\" fill=\"#e8e8e8\"/>");
            }
            else
            {
                // Simple polyline stroke
                var ptStr = string.Join(" ", pts.Select(p => $"{p.X:F2},{p.Y:F2}"));
                sb.AppendLine($"    <polyline points=\"{ptStr}\" " +
                              $"fill=\"none\" stroke=\"#e8e8e8\" " +
                              $"stroke-width=\"{maxW:F1}\" " +
                              $"stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            }
        }

        sb.Append("  </g>");
        return sb.ToString();
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("'", "&apos;");

    // ─── Template ────────────────────────────────────────────────
    private static readonly string TemplatesRoot =
        System.IO.Path.Combine(
            @"C:\Users\matys\Desktop\projects\nottwrite\templates");

    private void RefreshTemplateComboBox(string selectName)
    {
        TemplateComboBox.SelectionChanged -= TemplateComboBox_SelectionChanged;
        TemplateComboBox.Items.Clear();

        // Built-ins first, then any extra folders on disk
        var builtIn = new[] { "Default", "School", "Fancy", "Messy" };
        IEnumerable<string?> onDisk = Directory.Exists(TemplatesRoot)
            ? Directory.GetDirectories(TemplatesRoot)
                       .Select(System.IO.Path.GetFileName)
                       .Where(n => n != null && !builtIn.Contains(n))
                       .OrderBy(n => n)
            : Enumerable.Empty<string?>();

        foreach (var n in builtIn.Concat(onDisk!))
            TemplateComboBox.Items.Add(n);

        int idx = TemplateComboBox.Items.IndexOf(selectName);
        TemplateComboBox.SelectedIndex = idx >= 0 ? idx : 0;
        TemplateComboBox.SelectionChanged += TemplateComboBox_SelectionChanged;
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CurrentTemplate = TemplateComboBox.SelectedItem?.ToString() ?? "Default";
        CreateCharacterGrid();
    }

    // ─── Export / Import ─────────────────────────────────────────
    private record TemplateBundle(
        string Name,
        int Version,
        Dictionary<string, StrokeData> Characters);

    private void ExportTemplate_Click(object sender, RoutedEventArgs e)
    {
        string folder = GetTemplateFolder();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show("No files to export — draw some letters first.");
            return;
        }

        var files = Directory.GetFiles(folder, "*.json");
        if (files.Length == 0)
        {
            MessageBox.Show("Template folder is empty.");
            return;
        }

        var chars = new Dictionary<string, StrokeData>();
        foreach (var f in files)
        {
            var sd = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(f));
            if (sd != null) chars[System.IO.Path.GetFileName(f)] = sd;
        }

        var bundle = new TemplateBundle(CurrentTemplate, 1, chars);
        string json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Eksportuj template",
            FileName   = $"{CurrentTemplate}.nwt",
            DefaultExt = ".nwt",
            Filter     = "Nottwrite Template (*.nwt)|*.nwt|JSON (*.json)|*.json"
        };

        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show($"Exported {chars.Count} characters to:\n{dlg.FileName}");
        }
    }

    private void ImportTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Importuj template",
            Filter = "Nottwrite Template (*.nwt)|*.nwt|JSON (*.json)|*.json"
        };

        if (dlg.ShowDialog() != true) return;

        TemplateBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<TemplateBundle>(File.ReadAllText(dlg.FileName));
        }
        catch
        {
            MessageBox.Show("Invalid template file.");
            return;
        }

        if (bundle == null || bundle.Characters.Count == 0)
        {
            MessageBox.Show("File is empty or corrupted.");
            return;
        }

        // Use bundle name, avoid overwriting built-ins without confirmation
        string name = bundle.Name;
        var builtIn = new[] { "Default", "School", "Fancy", "Messy" };

        if (builtIn.Contains(name))
        {
            var result = MessageBox.Show(
                $"Template '{name}' is a built-in template. Overwrite?\n\nClick No to use a custom name.",
                "Warning", MessageBoxButton.YesNoCancel);

            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.No)
            {
                name = $"{name}_imported";
            }
        }

        string targetFolder = System.IO.Path.Combine(TemplatesRoot, name);
        if (Directory.Exists(targetFolder) && !builtIn.Contains(bundle.Name))
        {
            if (MessageBox.Show($"Template '{name}' already exists. Overwrite?",
                    "Warning", MessageBoxButton.YesNo) == MessageBoxResult.No)
                return;
        }

        Directory.CreateDirectory(targetFolder);
        foreach (var (filename, sd) in bundle.Characters)
        {
            string path = System.IO.Path.Combine(targetFolder, filename);
            File.WriteAllText(path, JsonSerializer.Serialize(sd,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        RefreshTemplateComboBox(name);
        MessageBox.Show($"Imported '{name}' — {bundle.Characters.Count} characters.");
    }
}
