using HandwritingFontCreator.Core.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
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
using System.Windows.Threading;


namespace HandwritingFontCreator.UI;

public partial class MainWindow : Window
{
    private bool _isDrawing;
    private int _loadedStrokeCount = 0;

    private readonly List<List<Point>> _strokes = [];
    private List<Point>? _currentStroke;
    private readonly Stack<List<Point>> _redoStack = [];

    private string CurrentTemplate   = "Default";
    private string _defaultTemplate  = "Default";
    private string CurrentCharacter = "A";
    private Button? _selectedCharacterButton;
    private bool _hasUnsavedChanges;
    private bool _isDarkTheme = true;
    private int _lineSpacing      = 100;
    private double _lineThickness = 1.0;
    private double _fontSize      = 180.0;
    private int  _currentVariant  = 1;
    private bool _showGhost       = true;
    private double _taperAmount     = 0.0;
    private double _tipRoundness   = 1.0;
    private double _strokeWidth     = 3.5;
    private double _letterSpacing   = 0.0;
    private double _wordSpacing     = 0.0;
    private double _lineHeightMult  = 1.6;
    private double _genStrokeWidth  = 3.5;
    private bool   _eraserMode      = false;
    private double _randomRotation  = 2.0;
    private double _randomOffsetY   = 3.0;

    private enum PaperStyle { Lines, Dots, Clear }
    private PaperStyle _paperStyle  = PaperStyle.Lines;
    private double _dotSpacing      = 20.0;
    private double _dotSize         = 1.5;

    // Hotkeys
    private readonly HotkeyManager _hk = new();

    // Middle mouse pan
    private bool _panning;
    private System.Windows.Point _panStart;
    private double _panScrollH, _panScrollV;

    // Paper canvas
    private static readonly (string Name, int W, int H)[] PaperPresets =
    [
        ("A4 Portrait",   794, 1123),
        ("A4 Landscape", 1123,  794),
        ("A5 Portrait",   559,  794),
        ("Letter",        816, 1056),
    ];
    private int _paperPresetIdx = 0;
    private int _paperW  = 794;
    private int _paperMinH = 1123;
    private const int PaperPadTop  = 36;
    private const int PaperPadSide = 40;
    private const int PaperInnerH  = 52;
    private const int PaperInnerV  = 44;

    // ── Edit grid ──────────────────────────────────────────────────────
    private const int GridCellW = 150;
    private const int GridCellH = 170;
    private char _editActiveChar = 'A';
    private List<Point>? _editCurrentStroke;
    private bool _isDrawingOnGrid;
    private Point _editCellOrigin;
    private Dictionary<char, (List<List<Point>> strokes, double fileW, double fileH)> _charCache = new();
    private Dictionary<char, System.Windows.Media.Color> _editCharColors = new();

    // ── Inline color picker ───────────────────────────────────────────
    private float _cpHue = 0f, _cpSat = 0f, _cpVal = 0.91f;
    private bool _cpDraggingSv = false, _cpDraggingHue = false;
    private bool _cpUpdating = false;

    // ── Brush options ─────────────────────────────────────────────────
    private enum BrushShape { Round, Flat, Calligraphy, Triangle, Ink, Chisel }
    private BrushShape _brushShape = BrushShape.Round;
    private double _brushOpacity = 1.0;
    private int _smoothingPasses = 2;
    private bool _pressureSimulation = false;
    private double _calligraphyAngle = 45.0;
    private SKColor _editStrokeColor = new SKColor(0xE8, 0xE8, 0xE8);
    private bool _taperEnabled = false;
    private bool _isFirstMouseDown = true;

    // ── Speech recognition ─────────────────────────────────────────
    private bool _isListening = false;

    // ── StrokeData file cache ──────────────────────────────────────
    private readonly Dictionary<string, StrokeData?> _strokeDataCache = new();

    private StrokeData? GetStrokeDataCached(string filePath)
    {
        if (_strokeDataCache.TryGetValue(filePath, out var sd)) return sd;
        if (!File.Exists(filePath)) { _strokeDataCache[filePath] = null; return null; }
        try
        {
            var result = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(filePath));
            _strokeDataCache[filePath] = result;
            return result;
        }
        catch { _strokeDataCache[filePath] = null; return null; }
    }

    // ── Type-on-paper model ────────────────────────────────────────
    private record struct CharData(
        char Ch, bool Bold, bool Italic, Color Color,
        double RotDeg, double JitterY, int VariantIdx);

    private List<CharData> _typeChars  = new();
    private int            _typeCursor = 0;
    private bool           _typeBold   = false;
    private bool           _typeItalic = false;
    private bool           _cursorVisible = true;
    private DispatcherTimer? _cursorTimer;

    private Stack<(List<CharData> chars, int cursor)> _undoStack    = new();
    private Stack<(List<CharData> chars, int cursor)> _redoTypeStack = new();

    private void PushUndo()
    {
        _undoStack.Push((_typeChars.ToList(), _typeCursor));
        if (_undoStack.Count > 200) _undoStack = new(_undoStack.Take(200));
        _redoTypeStack.Clear();
    }

    // ── Type mode color picker ─────────────────────────────────────
    private Color _typePickerColor = Color.FromRgb(0xE8, 0xE8, 0xE8);

    private static readonly string TemplatesPath =
        @"C:\Users\matys\Desktop\projects\nottwrite\templates";

    private static readonly string BaseChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "abcdefghijklmnopqrstuvwxyz" +
        "0123456789" +
        ".,!?():-;'@#&" +
        "ĄĘÓŚŹŻĆŃŁąęóśźżćńł";

    private readonly List<char> _customChars = [];

    private string AllChars => BaseChars + new string([.. _customChars]);

    private static string CustomCharsFilePath =>
        System.IO.Path.Combine(
            TemplatesPath,
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

        // Stroke color follows theme: white on dark, black on light
        _editStrokeColor = dark
            ? new SKColor(0xE8, 0xE8, 0xE8)
            : new SKColor(0x1A, 0x1A, 0x1A);

        RedrawCurrentStrokes();
        CreateCharacterGrid();
        AlphabetEditCanvas?.InvalidateVisual();
        BrushPreviewCanvas?.InvalidateVisual();
    }

    // ─── Pen settings persistence ─────────────────────────────────
    private static readonly string SettingsPath =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "nottwrite", "settings.json");

    private void SttDuration_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SttDurationCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            string s = item.Content.ToString()!.Replace("s", "");
            if (int.TryParse(s, out int sec)) _sttSeconds = sec;
        }
    }

    private void SttLang_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SttLangCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            _sttLang = item.Content.ToString() ?? "pl";
    }

    private void SavePenSettings()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath)!);
            var obj = new
            {
                fontSize        = _fontSize,
                taperAmount     = _taperAmount,
                taperEnabled    = _taperEnabled,
                tipRoundness    = _tipRoundness,
                letterSpacing   = _letterSpacing,
                wordSpacing     = _wordSpacing,
                lineHeightMult  = _lineHeightMult,
                genStrokeWidth  = _genStrokeWidth,
                lineSpacing     = _lineSpacing,
                lineThickness   = _lineThickness,
                paperStyle      = _paperStyle.ToString(),
                dotSpacing      = _dotSpacing,
                dotSize         = _dotSize,
                randomRotation  = _randomRotation,
                randomOffsetY   = _randomOffsetY,
                lastTemplate    = CurrentTemplate,
                defaultTemplate = _defaultTemplate,
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(obj));
        }
        catch { /* best-effort */ }
    }

    private void LoadPenSettings()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            var r = doc.RootElement;
            if (r.TryGetProperty("fontSize",       out var v)) _fontSize       = v.GetDouble();
            if (r.TryGetProperty("taperAmount",    out v))     _taperAmount    = v.GetDouble();
            if (r.TryGetProperty("letterSpacing",  out v))     _letterSpacing  = v.GetDouble();
            if (r.TryGetProperty("wordSpacing",    out v))     _wordSpacing    = v.GetDouble();
            if (r.TryGetProperty("lineHeightMult", out v))     _lineHeightMult = v.GetDouble();
            if (r.TryGetProperty("genStrokeWidth", out v))     _genStrokeWidth = v.GetDouble();
            if (r.TryGetProperty("lineSpacing",    out v))     _lineSpacing    = v.GetInt32();
            if (r.TryGetProperty("lineThickness",  out v))     _lineThickness  = v.GetDouble();
            if (r.TryGetProperty("paperStyle",     out v) &&
                Enum.TryParse<PaperStyle>(v.GetString(), out var ps)) _paperStyle = ps;
            if (r.TryGetProperty("dotSpacing",     out v))     _dotSpacing     = v.GetDouble();
            if (r.TryGetProperty("dotSize",        out v))     _dotSize        = v.GetDouble();
            if (r.TryGetProperty("randomRotation", out v))     _randomRotation = v.GetDouble();
            if (r.TryGetProperty("randomOffsetY",  out v))     _randomOffsetY  = v.GetDouble();
            if (r.TryGetProperty("taperEnabled",   out v))     _taperEnabled   = v.GetBoolean();
            if (r.TryGetProperty("tipRoundness",   out v))     _tipRoundness   = v.GetDouble();
            if (r.TryGetProperty("lastTemplate",    out v) && v.GetString() is string lt && !string.IsNullOrEmpty(lt))
                CurrentTemplate = lt;
            if (r.TryGetProperty("defaultTemplate", out v) && v.GetString() is string dt && !string.IsNullOrEmpty(dt))
                _defaultTemplate = dt;
        }
        catch { /* ignore corrupt settings */ }
    }

    private void VarBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (!int.TryParse(btn.Tag?.ToString(), out int v)) return;
        if (_hasUnsavedChanges) SaveEditActiveChar();
        _currentVariant = v;
        UpdateVariantButtons();
        LoadEditActiveChar();
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
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void DrawOptionsToggle_Click(object sender, RoutedEventArgs e)
    {
        // DrawOptionsPanel hidden in compat area — no-op for edit mode
    }

    // ─── Custom char add ─────────────────────────────────────────
    private void AddCustomChar_Click(object sender, RoutedEventArgs e) =>
        CustomCharInput.Focus();

    private void TaperSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _taperAmount = TaperSlider.Value;
        TaperValueLabel.Text = _taperAmount.ToString("0.#");
        BrushPreviewCanvas?.InvalidateVisual();
        AlphabetEditCanvas?.InvalidateVisual();
        RefreshDisplay();
    }

    private void StrokeWidthSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // kept for compat but BrushSizeSlider is primary now
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

    private void WordSpacingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _wordSpacing = WordSpacingSlider.Value;
        WordSpacingLabel.Text = _wordSpacing.ToString("+0;-0;0") + "px";
        RefreshGeneratedText();
    }

    private void LineHeightSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _lineHeightMult = LineHeightSlider.Value;
        LineHeightLabel.Text = _lineHeightMult.ToString("0.0") + "×";
        RefreshGeneratedText();
    }

    private void GenStrokeWidthSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _genStrokeWidth = GenStrokeWidthSlider.Value;
        GenStrokeWidthLabel.Text = _genStrokeWidth.ToString("0.#");
        RefreshGeneratedText();
    }

    private void RandomRotSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _randomRotation = RandomRotSlider.Value;
        RandomRotLabel.Text = $"±{_randomRotation:0.#}°";
        RefreshGeneratedText();
    }

    private void RandomYSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _randomOffsetY = RandomYSlider.Value;
        RandomYLabel.Text = $"±{_randomOffsetY:0.#}px";
        RefreshGeneratedText();
    }

    private void CustomCharInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_resourcesReady) return;
        string txt = CustomCharInput.Text;
        if (string.IsNullOrEmpty(txt)) return;
        char c = txt[0];
        CustomCharInput.Text = "";

        if (!AllChars.Contains(c))
        {
            _customChars.Add(c);
            SaveCustomChars();
            CreateCharacterGrid();
        }

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
                if (_hasUnsavedChanges) SaveEditActiveChar();

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
                break;
            }
        }
    }

    // ─── Init ────────────────────────────────────────────────────
    private string GetTemplateFolder()
    {
        string folder =
            System.IO.Path.Combine(
                TemplatesPath,
                CurrentTemplate);

        Directory.CreateDirectory(folder);
        return folder;
    }

    public MainWindow()
    {
        InitializeComponent();
        _hk.Load();

        Resources["StrokeBrush"]       = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        Resources["LoadedStrokeBrush"] = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
        Resources["NotebookLineBrush"] = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
        _resourcesReady = true;

        LoadCustomChars();
        LoadPenSettings();
        RefreshTemplateComboBox(CurrentTemplate);

        Closing += (_, _) => SavePenSettings();

        Loaded += (_, _) =>
        {
            DrawNotebookLines();
            FontSizeSlider.Value         = _fontSize;
            FontSizeLabel.Text           = $"{_fontSize:0}px";
            TaperSlider.Value            = _taperAmount;
            TaperValueLabel.Text         = _taperAmount.ToString("0.#");
            LetterSpacingSlider.Value    = _letterSpacing;
            LetterSpacingLabel.Text      = _letterSpacing.ToString("+0;-0;0") + "px";
            WordSpacingSlider.Value      = _wordSpacing;
            WordSpacingLabel.Text        = _wordSpacing.ToString("+0;-0;0") + "px";
            LineHeightSlider.Value       = _lineHeightMult;
            LineHeightLabel.Text         = _lineHeightMult.ToString("0.0") + "×";
            GenStrokeWidthSlider.Value   = _genStrokeWidth;
            GenStrokeWidthLabel.Text     = _genStrokeWidth.ToString("0.#");
            SpacingSlider.Value          = _lineSpacing;
            SpacingLabel.Text            = $"{_lineSpacing}px";
            ThicknessLabel.Text          = $"{_lineThickness:0.#}px";
            RandomRotLabel.Text          = $"±{_randomRotation:0.#}°";
            RandomYLabel.Text            = $"±{_randomOffsetY:0.#}px";

            UpdateVariantButtons();
            GhostToggleBtn.Background  = GetBrush("AccentBrush");
            EditActiveCharLabel.Text = _editActiveChar.ToString();
            UpdateBrushShapeButtons();
            BrushSizeLabel.Text     = _strokeWidth.ToString("0.#");
            BrushOpacityLabel.Text  = "100%";
            BrushSmoothLabel.Text   = "Med";
            CalligAngleLabel.Text   = $"{_calligraphyAngle:0}°";
            TaperToggleBtn.Background = _taperEnabled ? GetBrush("AccentBrush") : GetBrush("ButtonBg");
            TaperToggleLabel.Text     = _taperEnabled ? "ON" : "OFF";
            TipRoundnessSlider.Value  = _tipRoundness;
            TipRoundnessLabel.Text    = $"{_tipRoundness:0%}";
            SetPaperStyle(_paperStyle);
            DotSpacingLabel.Text = $"{_dotSpacing:0}px";
            DotSizeLabel.Text    = $"{_dotSize:0.#}px";

            AlphabetScrollViewer.SizeChanged += (_, _) => UpdateAlphabetGridHeight();
            UpdateDefaultStar();
            SwitchMode(AppMode.Edit);
        };

        CreateCharacterGrid();
        CurrentCharacter = "A";
        CurrentCharacterText.Text = "A";
    }

    // ─── Alphabet grid height ─────────────────────────────────────
    private void UpdateAlphabetGridHeight()
    {
        if (AlphabetScrollViewer.ActualWidth <= 0) return;
        int cols = Math.Max(1, (int)(AlphabetScrollViewer.ActualWidth / GridCellW));
        int rows = (int)Math.Ceiling((double)AllChars.Length / cols);
        AlphabetGridHost.Height = rows * GridCellH;
        AlphabetEditCanvas.InvalidateVisual();
    }

    // ─── Cache ────────────────────────────────────────────────────
    private void LoadAllCharsCache()
    {
        _charCache.Clear(); _strokeDataCache.Clear();
        foreach (char c in AllChars)
        {
            string path = GetCharacterFilePath(c, _currentVariant);
            if (!File.Exists(path)) continue;
            var data = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(path));
            if (data == null || data.Width <= 0 || data.Height <= 0) continue;
            var strokes = data.Strokes.Select(s =>
                s.Points.Select(p => new Point(p.X, p.Y)).ToList()).ToList();
            _charCache[c] = (strokes, data.Width, data.Height);
        }
    }

    // ─── Save / Load active char ──────────────────────────────────
    private void SaveEditActiveChar()
    {
        if (_strokes.Count == 0) return;
        string path = GetCharacterFilePath(_editActiveChar, _currentVariant);
        Directory.CreateDirectory(GetTemplateFolder());
        var data = new StrokeData { Width = GridCellW, Height = GridCellH, Baseline = GridCellH * 0.75 };
        if (_editCharColors.TryGetValue(_editActiveChar, out var cc))
            data.Color = $"#{cc.R:X2}{cc.G:X2}{cc.B:X2}";
        foreach (var s in _strokes)
            data.Strokes.Add(new Stroke { Points = s.Select(p => new PointData { X = p.X, Y = p.Y }).ToList() });
        File.WriteAllText(path, JsonSerializer.Serialize(data));
        _charCache[_editActiveChar] = (_strokes.Select(s => s.ToList()).ToList(), GridCellW, GridCellH);
        _strokeDataCache.Remove(path);
        _hasUnsavedChanges = false;
        UpdateAlphabetProgress();
    }

    private void LoadEditActiveChar()
    {
        _strokes.Clear();
        _redoStack.Clear();
        string path = GetCharacterFilePath(_editActiveChar, _currentVariant);
        if (!File.Exists(path)) { UpdateEditColorSwatch(); AlphabetEditCanvas?.InvalidateVisual(); return; }
        var data = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(path));
        if (data == null || data.Width <= 0 || data.Height <= 0) return;
        double sx = GridCellW / data.Width, sy = GridCellH / data.Height;
        foreach (var s in data.Strokes)
            _strokes.Add(s.Points.Select(p => new Point(p.X * sx, p.Y * sy)).ToList());
        _charCache[_editActiveChar] = (_strokes.Select(s => s.ToList()).ToList(), GridCellW, GridCellH);
        // Load per-char color from JSON
        if (data.Color != null && TryParseHexColor(data.Color, out var c))
            _editCharColors[_editActiveChar] = c;
        else
            _editCharColors.Remove(_editActiveChar);
        UpdateEditColorSwatch();
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private static bool TryParseHexColor(string hex, out System.Windows.Media.Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
        {
            color = System.Windows.Media.Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }
        return false;
    }

    private void UpdateEditColorSwatch()
    {
        var col = _editCharColors.TryGetValue(_editActiveChar, out var c)
            ? c : System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8);
        var brush = new SolidColorBrush(col);
        if (EditCharColorSwatch != null)  EditCharColorSwatch.Background  = brush;
        if (InlineColorPreview  != null)  InlineColorPreview.Background   = brush;
        // Sync inline picker HSV
        var (h, s, v) = CpRgbToHsv(col);
        _cpHue = h; _cpSat = s; _cpVal = v;
        CpSyncInputs();
        InlineSvCanvas?.InvalidateVisual();
        InlineHueCanvas?.InvalidateVisual();
    }

    private void EditCharColorSwatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

    // ── Inline color picker handlers ──────────────────────────────────
    private void InlineSvCanvas_Paint(object sender, SKPaintSurfaceEventArgs e)
    {
        var cv = e.Surface.Canvas; float w = e.Info.Width, h = e.Info.Height;
        var (pr, pg, pb) = CpHsvToRgb(_cpHue, 1f, 1f);
        using var hSh = SKShader.CreateLinearGradient(new SKPoint(0,0), new SKPoint(w,0),
            new[]{SKColors.White, new SKColor(pr,pg,pb)}, null, SKShaderTileMode.Clamp);
        cv.DrawRect(0,0,w,h, new SKPaint{Shader=hSh, IsAntialias=true});
        using var vSh = SKShader.CreateLinearGradient(new SKPoint(0,0), new SKPoint(0,h),
            new[]{SKColors.Transparent, SKColors.Black}, null, SKShaderTileMode.Clamp);
        cv.DrawRect(0,0,w,h, new SKPaint{Shader=vSh, IsAntialias=true});
        float cx = _cpSat*w, cy = (1f-_cpVal)*h;
        cv.DrawCircle(cx,cy,6, new SKPaint{Color=SKColors.Black, Style=SKPaintStyle.Stroke, StrokeWidth=2f, IsAntialias=true});
        cv.DrawCircle(cx,cy,6, new SKPaint{Color=SKColors.White, Style=SKPaintStyle.Stroke, StrokeWidth=1.2f, IsAntialias=true});
    }

    private void InlineHueCanvas_Paint(object sender, SKPaintSurfaceEventArgs e)
    {
        var cv = e.Surface.Canvas; float w = e.Info.Width, h = e.Info.Height;
        var cols = new SKColor[]{new(255,0,0),new(255,255,0),new(0,255,0),new(0,255,255),new(0,0,255),new(255,0,255),new(255,0,0)};
        var pos  = new float[]{0f,1f/6,2f/6,3f/6,4f/6,5f/6,1f};
        using var sh = SKShader.CreateLinearGradient(new SKPoint(0,0), new SKPoint(0,h), cols, pos, SKShaderTileMode.Clamp);
        cv.DrawRect(0,0,w,h, new SKPaint{Shader=sh, IsAntialias=true});
        float y = (_cpHue/360f)*h;
        cv.DrawLine(0,y,w,y, new SKPaint{Color=SKColors.White, StrokeWidth=2, Style=SKPaintStyle.Stroke, IsAntialias=true});
        cv.DrawLine(0,y,w,y, new SKPaint{Color=SKColors.Black, StrokeWidth=0.8f, Style=SKPaintStyle.Stroke, IsAntialias=true});
    }

    private void InlineSvInput_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _cpDraggingSv=true; InlineSvInput.CaptureMouse(); CpApplySv(e.GetPosition(InlineSvInput)); }
    private void InlineSvInput_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    { if(_cpDraggingSv) CpApplySv(e.GetPosition(InlineSvInput)); }
    private void InlineSvInput_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _cpDraggingSv=false; InlineSvInput.ReleaseMouseCapture(); SaveEditActiveCharColor(); }

    private void InlineHueInput_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _cpDraggingHue=true; InlineHueInput.CaptureMouse(); CpApplyHue(e.GetPosition(InlineHueInput)); }
    private void InlineHueInput_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    { if(_cpDraggingHue) CpApplyHue(e.GetPosition(InlineHueInput)); }
    private void InlineHueInput_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { _cpDraggingHue=false; InlineHueInput.ReleaseMouseCapture(); SaveEditActiveCharColor(); }

    private void CpApplySv(Point p)
    {
        _cpSat = (float)Math.Clamp(p.X / InlineSvInput.ActualWidth,  0, 1);
        _cpVal = (float)Math.Clamp(1 - p.Y / InlineSvInput.ActualHeight, 0, 1);
        CpCommit();
    }

    private void CpApplyHue(Point p)
    {
        _cpHue = (float)Math.Clamp(p.Y / InlineHueInput.ActualHeight * 360, 0, 359.99f);
        CpCommit();
    }

    private void InlineRgbInput_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_cpUpdating) return;
        if (byte.TryParse(InlineRInput.Text, out byte r) &&
            byte.TryParse(InlineGInput.Text, out byte g) &&
            byte.TryParse(InlineBInput.Text, out byte b))
        {
            var (h, s, v) = CpRgbToHsv(System.Windows.Media.Color.FromRgb(r, g, b));
            _cpHue=h; _cpSat=s; _cpVal=v;
            CpCommit(skipRgb: true);
        }
    }

    private void InlineHexInput_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_cpUpdating) return;
        string raw = InlineHexInput.Text.TrimStart('#');
        if (raw.Length == 6)
        {
            try
            {
                byte r = Convert.ToByte(raw[..2], 16);
                byte g = Convert.ToByte(raw[2..4], 16);
                byte b = Convert.ToByte(raw[4..6], 16);
                var (h, s, v) = CpRgbToHsv(System.Windows.Media.Color.FromRgb(r, g, b));
                _cpHue=h; _cpSat=s; _cpVal=v;
                CpCommit(skipHex: true);
            }
            catch { }
        }
    }

    private void CpCommit(bool skipRgb = false, bool skipHex = false)
    {
        var (r, g, b) = CpHsvToRgb(_cpHue, _cpSat, _cpVal);
        var col = System.Windows.Media.Color.FromRgb(r, g, b);
        _editCharColors[_editActiveChar] = col;
        if (EditCharColorSwatch != null) EditCharColorSwatch.Background = new SolidColorBrush(col);
        if (InlineColorPreview  != null) InlineColorPreview.Background  = new SolidColorBrush(col);
        InlineSvCanvas?.InvalidateVisual();
        InlineHueCanvas?.InvalidateVisual();
        CpSyncInputs(skipRgb, skipHex);
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void CpSyncInputs(bool skipRgb = false, bool skipHex = false)
    {
        if (InlineRInput == null) return;
        _cpUpdating = true;
        var (r, g, b) = CpHsvToRgb(_cpHue, _cpSat, _cpVal);
        if (!skipRgb)
        {
            InlineRInput.Text = r.ToString();
            InlineGInput.Text = g.ToString();
            InlineBInput.Text = b.ToString();
        }
        if (!skipHex) InlineHexInput.Text = $"#{r:X2}{g:X2}{b:X2}";
        _cpUpdating = false;
    }

    private static (byte r, byte g, byte b) CpHsvToRgb(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = v * s, x = c * (1f - Math.Abs((h / 60f) % 2f - 1f)), m = v - c;
        float rf, gf, bf;
        if      (h < 60)  { rf=c; gf=x; bf=0; }
        else if (h < 120) { rf=x; gf=c; bf=0; }
        else if (h < 180) { rf=0; gf=c; bf=x; }
        else if (h < 240) { rf=0; gf=x; bf=c; }
        else if (h < 300) { rf=x; gf=0; bf=c; }
        else              { rf=c; gf=0; bf=x; }
        return ((byte)((rf+m)*255), (byte)((gf+m)*255), (byte)((bf+m)*255));
    }

    private static (float h, float s, float v) CpRgbToHsv(System.Windows.Media.Color c)
    {
        float r = c.R/255f, g = c.G/255f, b = c.B/255f;
        float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        float delta = max - min, v = max, s = max < 0.0001f ? 0f : delta / max, h = 0f;
        if (delta > 0.0001f)
        {
            if      (max == r) h = 60f * (((g - b) / delta) % 6f);
            else if (max == g) h = 60f * (((b - r) / delta) + 2f);
            else               h = 60f * (((r - g) / delta) + 4f);
            if (h < 0f) h += 360f;
        }
        return (h, s, v);
    }

    private void SaveEditActiveCharColor()
    {
        string path = GetCharacterFilePath(_editActiveChar, _currentVariant);
        if (!File.Exists(path)) return;
        var data = JsonSerializer.Deserialize<StrokeData>(File.ReadAllText(path));
        if (data == null) return;
        if (_editCharColors.TryGetValue(_editActiveChar, out var c))
            data.Color = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        else
            data.Color = null;
        File.WriteAllText(path, JsonSerializer.Serialize(data));
        _strokeDataCache.Remove(path);
    }

    // ─── Alphabet edit canvas paint ───────────────────────────────
    private void AlphabetEditCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float totalW = e.Info.Width;
        canvas.Clear(GetSkColor("CanvasBg", new SKColor(0x1A, 0x1A, 0x1A)));

        var chars = AllChars;
        int cols = Math.Max(1, (int)(totalW / GridCellW));

        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            float cx = (i % cols) * GridCellW;
            float cy = (i / cols) * GridCellH;
            DrawGridCell(canvas, c, cx, cy, c == _editActiveChar);
        }
    }

    private void DrawGridCell(SKCanvas canvas, char c, float cx, float cy, bool active)
    {
        float cw = GridCellW, ch = GridCellH;

        // background
        canvas.DrawRect(cx, cy, cw, ch,
            new SKPaint { Color = active ? new SKColor(0x2A, 0x2D, 0x30) : new SKColor(0x22, 0x22, 0x23), Style = SKPaintStyle.Fill });

        // border
        float bw = active ? 2f : 0.5f;
        canvas.DrawRect(cx + bw/2, cy + bw/2, cw - bw, ch - bw,
            new SKPaint { Color = active ? GetSkColor("AccentBrush", new SKColor(0x56,0x9C,0xD6)) : new SKColor(0x3F, 0x3F, 0x46),
                          Style = SKPaintStyle.Stroke, StrokeWidth = bw, IsAntialias = true });

        // ghost letter
        using var ghostFont = new SKPaint
        {
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
            TextSize = ch * 0.62f, TextAlign = SKTextAlign.Center,
            IsAntialias = true,
            Color = active ? new SKColor(100, 149, 237, 22) : new SKColor(100, 149, 237, 10)
        };
        canvas.DrawText(c.ToString(), cx + cw / 2, cy + ch * 0.82f, ghostFont);

        // char label top-right
        using var labelFont = new SKPaint { TextSize = 10, Color = new SKColor(0x55, 0x55, 0x55), TextAlign = SKTextAlign.Right, IsAntialias = true };
        canvas.DrawText(c.ToString(), cx + cw - 4, cy + 13, labelFont);

        // strokes
        List<List<Point>>? strokes = null;
        double fileW = GridCellW, fileH = GridCellH;
        if (c == _editActiveChar)
        {
            strokes = _strokes; fileW = GridCellW; fileH = GridCellH;
        }
        else if (_charCache.TryGetValue(c, out var cached))
        {
            strokes = cached.strokes; fileW = cached.fileW; fileH = cached.fileH;
        }

        if (strokes != null)
        {
            float sx = (float)(cw / fileW), sy = (float)(ch / fileH);
            byte alpha = c == _editActiveChar ? (byte)(_brushOpacity * 255) : (byte)210;
            SKColor col;
            if (_editCharColors.TryGetValue(c, out var charWpfCol))
                col = new SKColor(charWpfCol.R, charWpfCol.G, charWpfCol.B, alpha);
            else if (c == _editActiveChar)
                col = new SKColor(_editStrokeColor.Red, _editStrokeColor.Green, _editStrokeColor.Blue, alpha);
            else
                col = new SKColor(0xD0, 0xD0, 0xD0, alpha);
            float maxW = (float)(_strokeWidth + _taperAmount) * (sx + sy) * 0.5f;
            maxW = Math.Max(0.8f, Math.Min(maxW, 12f));
            foreach (var stroke in strokes)
            {
                if (stroke.Count == 0) continue;
                var pts = stroke.Select(p => new Point(p.X * sx + cx, p.Y * sy + cy)).ToList();
                RenderGridStroke(canvas, pts, col, maxW);
            }
        }
    }

    private void RenderGridStroke(SKCanvas canvas, IList<Point> pts, SKColor color, float maxW)
    {
        int n = pts.Count;
        if (n == 0) return;
        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };

        if (n == 1)
        {
            canvas.DrawCircle((float)pts[0].X, (float)pts[0].Y, maxW * 0.5f, paint);
            return;
        }

        double totalLen = 0;
        var arcLen = new double[n];
        for (int i = 1; i < n; i++)
        {
            double dx = pts[i].X - pts[i-1].X, dy = pts[i].Y - pts[i-1].Y;
            totalLen += Math.Sqrt(dx*dx + dy*dy);
            arcLen[i] = totalLen;
        }

        var left = new SKPoint[n]; var right = new SKPoint[n];
        for (int i = 0; i < n; i++)
        {
            var prev = pts[Math.Max(0, i-1)]; var next = pts[Math.Min(n-1, i+1)];
            float dx = (float)(next.X - prev.X), dy = (float)(next.Y - prev.Y);
            float len = MathF.Sqrt(dx*dx + dy*dy);
            float nx = len > 0.001f ? -dy/len : 0f, ny = len > 0.001f ? dx/len : 0f;
            double t = totalLen > 0 ? arcLen[i] / totalLen : 0.5;
            float baseHw = maxW * 0.5f;
            float taperFactor = _taperEnabled ? (float)Math.Sin(Math.PI * t) : 1f;
            float roundFactor = (float)(1.0 - _tipRoundness * (1.0 - Math.Sin(Math.PI * t)));
            float hw = baseHw * (_taperEnabled ? taperFactor : (_tipRoundness > 0 ? roundFactor : 1f));
            if (hw < 0.35f) hw = 0.35f;

            if (_brushShape == BrushShape.Flat)
            {
                hw *= 0.35f;
            }
            else if (_brushShape == BrushShape.Calligraphy)
            {
                double ang = _calligraphyAngle * Math.PI / 180.0;
                float cnx = (float)(nx * Math.Cos(ang) - ny * Math.Sin(ang));
                float cny = (float)(nx * Math.Sin(ang) + ny * Math.Cos(ang));
                nx = cnx; ny = cny;
                hw *= 0.6f;
            }
            else if (_brushShape == BrushShape.Triangle)
            {
                // Narrow at start, wide at middle, sharp at end
                hw *= (float)(t < 0.5 ? t * 2 : (1 - t) * 1.4 + 0.3);
            }
            else if (_brushShape == BrushShape.Ink)
            {
                // Simulate ink pressure — variable width with slight random feel based on position
                double jitter = Math.Sin(i * 1.7 + totalLen * 0.01) * 0.15 + 1.0;
                hw *= (float)(0.4 + 0.6 * Math.Sin(Math.PI * t) * jitter);
            }
            else if (_brushShape == BrushShape.Chisel)
            {
                // Fixed angle chisel — combine nx/ny with 45° angle
                double ang = 45.0 * Math.PI / 180.0;
                nx = (float)Math.Cos(ang); ny = (float)Math.Sin(ang);
                hw *= 0.5f;
            }

            left[i]  = new SKPoint((float)pts[i].X + nx * hw, (float)pts[i].Y + ny * hw);
            right[i] = new SKPoint((float)pts[i].X - nx * hw, (float)pts[i].Y - ny * hw);
        }

        var path = new SKPath();
        path.MoveTo(left[0]);
        AddCatmullRomToPath(path, left);
        path.LineTo(right[n-1]);
        AddCatmullRomToPath(path, right.AsEnumerable().Reverse().ToArray());
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void BrushPreviewCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width, h = e.Info.Height;
        canvas.Clear(GetSkColor("AppBg", new SKColor(0x1E, 0x1E, 0x1E)));

        var pts = new List<Point>();
        for (int i = 0; i < 30; i++)
        {
            double t = i / 29.0;
            pts.Add(new Point(w * 0.08 + t * w * 0.84, h * 0.5 + Math.Sin(t * Math.PI * 1.5) * h * 0.3));
        }

        byte alpha = (byte)(_brushOpacity * 255);
        var col = new SKColor(_editStrokeColor.Red, _editStrokeColor.Green, _editStrokeColor.Blue, alpha);
        float maxW = (float)(_strokeWidth + _taperAmount);
        RenderGridStroke(canvas, pts, col, maxW);
    }

    // ─── Alphabet mouse handlers ──────────────────────────────────
    private (char c, float cellX, float cellY) GridCellAt(Point p)
    {
        float cw = (float)AlphabetInputCanvas.ActualWidth;
        int cols = Math.Max(1, (int)(cw / GridCellW));
        int col = (int)(p.X / GridCellW);
        int row = (int)(p.Y / GridCellH);
        if (col < 0 || col >= cols) return ('\0', 0, 0);
        int idx = row * cols + col;
        var chars = AllChars;
        if (idx < 0 || idx >= chars.Length) return ('\0', 0, 0);
        return (chars[idx], col * GridCellW, row * GridCellH);
    }

    private void Alphabet_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_eraserMode) { Alphabet_MouseRightDown(sender, new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Right) { RoutedEvent = e.RoutedEvent }); return; }
        var pos = e.GetPosition(AlphabetInputCanvas);
        var (c, cx, cy) = GridCellAt(pos);
        if (c == '\0') return;

        if (c != _editActiveChar)
        {
            SaveEditActiveChar();
            _editActiveChar = c;
            LoadEditActiveChar();
            EditActiveCharLabel.Text = c.ToString();
            CurrentCharacter = c.ToString();
            // First click just selects the cell — don't start drawing yet
            _isFirstMouseDown = true;
            AlphabetEditCanvas.InvalidateVisual();
            return;
        }

        if (_isFirstMouseDown) { _isFirstMouseDown = false; return; }

        _editCellOrigin = new Point(cx, cy);
        _isDrawingOnGrid = true;
        _redoStack.Clear();
        _hasUnsavedChanges = true;

        var localPt = new Point(pos.X - cx, pos.Y - cy);
        _editCurrentStroke = [localPt];
        _strokes.Add(_editCurrentStroke);
        AlphabetInputCanvas.CaptureMouse();
        AlphabetEditCanvas.InvalidateVisual();
    }

    private void Alphabet_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawingOnGrid || _editCurrentStroke == null) return;
        var pos = e.GetPosition(AlphabetInputCanvas);
        _editCurrentStroke.Add(new Point(pos.X - _editCellOrigin.X, pos.Y - _editCellOrigin.Y));
        AlphabetEditCanvas.InvalidateVisual();
    }

    private void Alphabet_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawingOnGrid) return;
        _isDrawingOnGrid = false;
        AlphabetInputCanvas.ReleaseMouseCapture();
        if (_smoothingPasses > 0 && _editCurrentStroke is { Count: > 3 })
        {
            int idx = _strokes.IndexOf(_editCurrentStroke);
            if (idx >= 0)
            {
                var smoothed = _editCurrentStroke;
                for (int i = 0; i < _smoothingPasses; i++) smoothed = SmoothStroke(smoothed);
                _strokes[idx] = smoothed;
                _editCurrentStroke = smoothed;
            }
        }
        SaveEditActiveChar();
        AlphabetEditCanvas.InvalidateVisual();
        CreateCharacterGrid();
    }

    private void Alphabet_MouseRightDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(AlphabetInputCanvas);
        var (c, cx, cy) = GridCellAt(pos);
        if (c == '\0') return;

        if (c != _editActiveChar)
        {
            SaveEditActiveChar();
            _editActiveChar = c;
            LoadEditActiveChar();
            EditActiveCharLabel.Text = c.ToString();
            CurrentCharacter = c.ToString();
        }
        var localPos = new Point(pos.X - cx, pos.Y - cy);
        int idx = FindStrokeNear(localPos, 15);
        if (idx < 0) return;
        _redoStack.Push(_strokes[idx]);
        _strokes.RemoveAt(idx);
        SaveEditActiveChar();
        AlphabetEditCanvas.InvalidateVisual();
        e.Handled = true;
    }

    // ─── Brush option handlers ────────────────────────────────────
    private void EditGridPrevChar_Click(object sender, RoutedEventArgs e) => NavigateEditGrid(-1);
    private void EditGridNextChar_Click(object sender, RoutedEventArgs e) => NavigateEditGrid(1);

    private void NavigateEditGrid(int delta)
    {
        var chars = AllChars;
        int idx = chars.IndexOf(_editActiveChar);
        idx = Math.Clamp(idx + delta, 0, chars.Length - 1);
        if (chars[idx] == _editActiveChar) return;
        SaveEditActiveChar();
        _editActiveChar = chars[idx];
        LoadEditActiveChar();
        EditActiveCharLabel.Text = _editActiveChar.ToString();
        CurrentCharacter = _editActiveChar.ToString();
        AlphabetEditCanvas.InvalidateVisual();
        double totalW = AlphabetScrollViewer.ActualWidth;
        int cols = Math.Max(1, (int)(totalW / GridCellW));
        int row = idx / cols;
        AlphabetScrollViewer.ScrollToVerticalOffset(row * GridCellH - 20);
    }

    private void BrushShape_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _brushShape = btn.Tag?.ToString() switch
        {
            "Flat"        => BrushShape.Flat,
            "Calligraphy" => BrushShape.Calligraphy,
            "Triangle"    => BrushShape.Triangle,
            "Ink"         => BrushShape.Ink,
            "Chisel"      => BrushShape.Chisel,
            _             => BrushShape.Round
        };
        UpdateBrushShapeButtons();
        CalligAnglePanel.Visibility = _brushShape == BrushShape.Calligraphy ? Visibility.Visible : Visibility.Collapsed;
        BrushPreviewCanvas?.InvalidateVisual();
    }

    private void UpdateBrushShapeButtons()
    {
        var active = GetBrush("AccentBrush"); var inactive = GetBrush("ButtonBg");
        BrushRoundBtn.Background    = _brushShape == BrushShape.Round        ? active : inactive;
        BrushFlatBtn.Background     = _brushShape == BrushShape.Flat         ? active : inactive;
        BrushCalligBtn.Background   = _brushShape == BrushShape.Calligraphy  ? active : inactive;
        BrushTriangleBtn.Background = _brushShape == BrushShape.Triangle      ? active : inactive;
        BrushInkBtn.Background      = _brushShape == BrushShape.Ink           ? active : inactive;
        BrushChiselBtn.Background   = _brushShape == BrushShape.Chisel        ? active : inactive;
    }

    private void BrushSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _strokeWidth = BrushSizeSlider.Value;
        BrushSizeLabel.Text = _strokeWidth.ToString("0.#");
        BrushPreviewCanvas?.InvalidateVisual();
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void BrushOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _brushOpacity = BrushOpacitySlider.Value / 100.0;
        BrushOpacityLabel.Text = $"{(int)(BrushOpacitySlider.Value)}%";
        BrushPreviewCanvas?.InvalidateVisual();
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void BrushSmooth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _smoothingPasses = (int)BrushSmoothSlider.Value;
        BrushSmoothLabel.Text = _smoothingPasses switch { 0 => "Off", 1 => "Low", 2 => "Med", 3 => "High", _ => "Max" };
    }

    private void CalligAngle_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _calligraphyAngle = CalligAngleSlider.Value;
        CalligAngleLabel.Text = $"{_calligraphyAngle:0}°";
        BrushPreviewCanvas?.InvalidateVisual();
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void PressureToggle_Click(object sender, RoutedEventArgs e)
    {
        _pressureSimulation = !_pressureSimulation;
        PressureToggleLabel.Text = _pressureSimulation ? "ON" : "OFF";
        PressureToggleBtn.Background = _pressureSimulation ? GetBrush("AccentBrush") : GetBrush("ButtonBg");
    }

    private void TipRoundness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _tipRoundness = e.NewValue;
        TipRoundnessLabel.Text = $"{_tipRoundness:0%}";
        AlphabetEditCanvas?.InvalidateVisual();
        TypeSkiaCanvas?.InvalidateVisual();
    }

    private void TaperToggle_Click(object sender, RoutedEventArgs e)
    {
        _taperEnabled = !_taperEnabled;
        TaperToggleLabel.Text = _taperEnabled ? "ON" : "OFF";
        TaperToggleBtn.Background = _taperEnabled ? GetBrush("AccentBrush") : GetBrush("ButtonBg");
        BrushPreviewCanvas?.InvalidateVisual();
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void StrokeColorBtn_Click(object sender, RoutedEventArgs e) { } // color now auto-from-theme

    // ─── Template creation ────────────────────────────────────────
    private void NewTemplate_Click(object sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "New Template",
            Width = 300, Height = 148,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBg"),
            Foreground = (Brush)FindResource("PrimaryText")
        };
        var panel = new StackPanel { Margin = new Thickness(14) };
        var label = new TextBlock { Text = "Template name:", FontSize = 12, Margin = new Thickness(0,0,0,6),
                                    Foreground = (Brush)FindResource("SecondaryText") };
        var tb = new System.Windows.Controls.TextBox { FontSize = 13, Padding = new Thickness(6,4,6,4),
                                                        Background = (Brush)FindResource("Surface2Bg"),
                                                        Foreground = (Brush)FindResource("PrimaryText"),
                                                        BorderBrush = (Brush)FindResource("AppBorderBrush"),
                                                        Margin = new Thickness(0,0,0,10) };
        var btnOk = new Button { Content = "Create", Height = 38,
                                  HorizontalAlignment = HorizontalAlignment.Stretch,
                                  FontSize = 13, FontWeight = FontWeights.SemiBold,
                                  Background = (Brush)FindResource("AccentBrush"),
                                  Foreground = (Brush)FindResource("PrimaryText") };
        btnOk.Click += (_, _) =>
        {
            string name = tb.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;
            // Sanitize
            foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(bad.ToString(), "");
            if (string.IsNullOrEmpty(name)) return;
            string folder = System.IO.Path.Combine(
                TemplatesPath, name);
            Directory.CreateDirectory(folder);
            RefreshTemplateComboBox(name);
            win.DialogResult = true;
        };
        panel.Children.Add(label);
        panel.Children.Add(tb);
        panel.Children.Add(btnOk);
        win.Content = panel;
        win.Loaded += (_, _) => tb.Focus();
        win.ShowDialog();
    }

    // ─── Speech-to-text ──────────────────────────────────────────
    // ─── STT via faster-whisper (Python subprocess) ───────────────
    private static readonly string SttScriptPath =
        System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "stt.py");

    private System.Diagnostics.Process? _sttProcess;
    private int    _sttSeconds = 5;
    private string _sttLang    = "pl";

    private void SpeechBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isListening) StopSpeech();
        else              StartSpeech();
    }

    private void StartSpeech()
    {
        if (_isListening) return;
        _isListening = true;
        SpeechBtn.Background  = GetBrush("AccentBrush");
        SpeechBtnIcon.Text    = "⏹";
        SttStatusLabel.Text   = "loading…";

        string script = System.IO.Path.GetFullPath(SttScriptPath);
        if (!File.Exists(script))
        {
            MessageBox.Show($"stt.py not found:\n{script}", "STT Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StopSpeech(); return;
        }

        Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "python",
                    Arguments              = $"-u \"{script}\" {_sttSeconds} {_sttLang}",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding  = System.Text.Encoding.UTF8,
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) throw new Exception("Failed to start python.exe");
                _sttProcess = proc;

                string? line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                {
                    if (!_isListening) break;
                    string l = line.Trim();
                    if (l == "READY")
                        Dispatcher.Invoke(() => SttStatusLabel.Text = "listening…");
                    else if (l == "RECORDING")
                        Dispatcher.Invoke(() => SttStatusLabel.Text = "recording…");
                    else if (l.StartsWith("RESULT:"))
                    {
                        string text = l["RESULT:".Length..].Trim() + " ";
                        Dispatcher.Invoke(() =>
                        {
                            SttStatusLabel.Text = "✓";
                            foreach (char ch in text) InsertChar(ch);
                        });
                    }
                }
                proc.WaitForExit();
                Dispatcher.Invoke(() => StopSpeech());
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StopSpeech();
                    MessageBox.Show($"STT error:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        });
    }

    private void StopSpeech()
    {
        try { _sttProcess?.Kill(); } catch { }
        _sttProcess          = null;
        _isListening         = false;
        SpeechBtn.Background = GetBrush("ButtonBg");
        SpeechBtnIcon.Text   = "🎤";
        if (SttStatusLabel != null) SttStatusLabel.Text = "";
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSpeech();
        base.OnClosed(e);
    }

    // ─── Cursor blink ────────────────────────────────────────────
    private void StartCursorBlink()
    {
        _cursorTimer?.Stop();
        _cursorVisible = true;
        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            if (_appMode == AppMode.Type) TypeSkiaCanvas?.InvalidateVisual();
        };
        _cursorTimer.Start();
    }

    protected override void OnActivated(EventArgs e)    { base.OnActivated(e);    if (_appMode == AppMode.Type) StartCursorBlink(); }
    protected override void OnDeactivated(EventArgs e)  { base.OnDeactivated(e);  _cursorTimer?.Stop(); _cursorVisible = false; TypeSkiaCanvas?.InvalidateVisual(); }

    // ─── Keyboard input ──────────────────────────────────────────
    protected override void OnPreviewTextInput(System.Windows.Input.TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (_appMode != AppMode.Type || string.IsNullOrEmpty(e.Text)) return;
        foreach (char ch in e.Text)
        {
            if (ch <= 32) continue; // space and below handled in OnPreviewKeyDown
            InsertChar(ch);
        }
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_appMode != AppMode.Type) return;
        // Don't steal keys from text inputs (e.g. custom paper size boxes)
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        var k = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var m = System.Windows.Input.Keyboard.Modifiers;

        switch (k)
        {
            case System.Windows.Input.Key.Back:
                if (_typeCursor > 0) { PushUndo(); _typeChars.RemoveAt(_typeCursor - 1); _typeCursor--; }
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Delete:
                if (_typeCursor < _typeChars.Count) { PushUndo(); _typeChars.RemoveAt(_typeCursor); }
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); break;

            case System.Windows.Input.Key.Left:
                if (_typeCursor > 0) _typeCursor--;
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Right:
                if (_typeCursor < _typeChars.Count) _typeCursor++;
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Home:
                while (_typeCursor > 0 && _typeChars[_typeCursor - 1].Ch != '\n') _typeCursor--;
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.End:
                while (_typeCursor < _typeChars.Count && _typeChars[_typeCursor].Ch != '\n') _typeCursor++;
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Space:
                InsertChar(' ');
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Return:
                InsertChar('\n');
                e.Handled = true; ScrollToCursor(); break;

            default:
                if (_hk.Matches("Bold",   k, m)) { BoldToggle();   e.Handled = true; break; }
                if (_hk.Matches("Italic", k, m)) { ItalicToggle(); e.Handled = true; break; }
                if (_hk.Matches("Undo",   k, m) && _undoStack.Count > 0)
                {
                    _redoTypeStack.Push((_typeChars.ToList(), _typeCursor));
                    var (uc, ucur) = _undoStack.Pop();
                    _typeChars = uc; _typeCursor = Math.Clamp(ucur, 0, uc.Count);
                    e.Handled = true; ResetCursorBlink(); RefreshGeneratedText();
                    break;
                }
                if (_hk.Matches("Redo",   k, m) && _redoTypeStack.Count > 0)
                {
                    _undoStack.Push((_typeChars.ToList(), _typeCursor));
                    var (rc, rcur) = _redoTypeStack.Pop();
                    _typeChars = rc; _typeCursor = Math.Clamp(rcur, 0, rc.Count);
                    e.Handled = true; ResetCursorBlink(); RefreshGeneratedText();
                    break;
                }
                break;
        }
    }

    private void InsertChar(char ch)
    {
        PushUndo();
        var variants = GetAllVariantPaths(ch);
        int vi = variants.Count > 0 ? Random.Shared.Next(variants.Count) : 0;
        double rot = _randomRotation > 0 ? (Random.Shared.NextDouble() * 2 - 1) * _randomRotation : 0;
        double jit = _randomOffsetY  > 0 ? (Random.Shared.NextDouble() * 2 - 1) * _randomOffsetY  : 0;
        // Use template char color as default if picker is still at the gray default
        var insertColor = _typePickerColor;
        if (_typePickerColor == System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8) &&
            variants.Count > 0 && GetStrokeDataCached(variants[vi]) is { } sd && sd.Color != null &&
            TryParseHexColor(sd.Color, out var tc))
            insertColor = tc;
        _typeChars.Insert(_typeCursor, new CharData(ch, _typeBold, _typeItalic, insertColor, rot, jit, vi));
        _typeCursor++;
        ResetCursorBlink();
        RefreshGeneratedText();
        ScrollToCursor();
    }

    private void ResetCursorBlink()
    {
        _cursorVisible = true;
        _cursorTimer?.Stop();
        _cursorTimer?.Start();
    }

    private void BoldToggle()
    {
        _typeBold = !_typeBold;
        BoldBtn.Background = _typeBold ? GetBrush("AccentBrush") : GetBrush("ButtonBg");
    }

    private void ItalicToggle()
    {
        _typeItalic = !_typeItalic;
        ItalicBtn.Background = _typeItalic ? GetBrush("AccentBrush") : GetBrush("ButtonBg");
    }

    // ─── Type mode color picker ───────────────────────────────────
    private void TypeColorSwatch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenTypeColorPicker();

    private void TypeColorPickerBtn_Click(object sender, RoutedEventArgs e)
        => OpenTypeColorPicker();

    private void OpenTypeColorPicker()
    {
        var picker = new ColorPickerWindow(_typePickerColor) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            _typePickerColor = picker.SelectedColor;
            TypeColorSwatch.Background = new SolidColorBrush(_typePickerColor);
            RefreshGeneratedText();
        }
    }

    // ─── Character grid (hidden compat) ──────────────────────────
    private bool CharacterExists(string character) =>
        File.Exists(GetCharacterFilePath(character[0]));

    private void UpdateProgress()
    {
        UpdateAlphabetProgress();
    }

    private void UpdateAlphabetProgress()
    {
        if (!_resourcesReady || AlphabetProgressBar == null) return;
        int total = AllChars.Length;
        int done = 0;
        foreach (char c in AllChars)
            if (File.Exists(GetCharacterFilePath(c, _currentVariant)) || _charCache.ContainsKey(c)) done++;
        AlphabetProgressText.Text = $"{done}/{total} ({done * 100 / Math.Max(1, total)}%)";
        AlphabetProgressBar.Value = done * 100.0 / Math.Max(1, total);
    }

    private void CreateCharacterGrid()
    {
        CharacterGrid.Children.Clear();

        var groups = new List<(char primary, char secondary)>();
        for (char c = 'A'; c <= 'Z'; c++)
            groups.Add((c, char.ToLower(c)));
        foreach (char c in BaseChars.Where(ch => !char.IsLetter(ch)))
            groups.Add((c, '\0'));
        foreach (char c in _customChars.Where(ch => !char.IsLetter(ch)))
            groups.Add((c, '\0'));
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
        var secondaryTxt = GetBrush("SecondaryText");

        foreach (var (primary, secondary) in groups)
        {
            bool primaryExists   = CharacterExists(primary.ToString());
            bool secondaryExists = secondary != '\0' && CharacterExists(secondary.ToString());
            bool anyExists       = primaryExists || secondaryExists;

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

        UpdateAlphabetProgress();
    }

    private void CharacterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (_hasUnsavedChanges) SaveEditActiveChar();

        var (primary, secondary) = ((char, char))button.Tag;

        if (button == _selectedCharacterButton && secondary != '\0')
        {
            bool isCurrentlyPrimary = CurrentCharacter == primary.ToString();
            CurrentCharacter = isCurrentlyPrimary ? secondary.ToString() : primary.ToString();
        }
        else
        {
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

        RefreshButtonDisplay(button, primary, secondary, showPrimary: CurrentCharacter == primary.ToString());
        CurrentCharacterText.Text = CurrentCharacter;
        // Navigate edit grid to this char
        char c = CurrentCharacter[0];
        if (c != _editActiveChar)
        {
            SaveEditActiveChar();
            _editActiveChar = c;
            LoadEditActiveChar();
            EditActiveCharLabel.Text = c.ToString();
        }
    }

    private void RefreshButtonDisplay(Button btn, char primary, char secondary, bool showPrimary)
    {
        if (btn.Content is not Grid grid) return;
        char shown = showPrimary ? primary : secondary;
        char other = showPrimary ? secondary : primary;

        if (grid.Children.Count > 0 && grid.Children[0] is TextBlock big)
        {
            big.Text       = shown.ToString();
            bool exists    = CharacterExists(shown.ToString());
            big.Foreground = exists ? GetBrush("CharExistsFg") : GetBrush("CharMissingFg");
        }

        if (grid.Children.Count > 1 && grid.Children[1] is TextBlock small && secondary != '\0')
        {
            small.Text       = other.ToString();
            bool exists      = CharacterExists(other.ToString());
            small.Foreground = exists ? GetBrush("CharExistsFg") : GetBrush("SecondaryText");
        }

        bool shownExists = CharacterExists(shown.ToString());
        bool otherExists = secondary != '\0' && CharacterExists(other.ToString());
        btn.Background = (shownExists || otherExists) ? GetBrush("CharExistsBg") : GetBrush("CharMissingBg");
    }

    private void LoadCurrentCharacter()
    {
        // now delegates to LoadEditActiveChar (chars loaded into _strokes in cell coords)
        LoadEditActiveChar();
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
        SaveEditActiveChar();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0)
        {
            MessageBox.Show("Draw a letter first");
            return;
        }

        SaveEditActiveChar();
        MessageBox.Show($"Saved {_editActiveChar}");
        CreateCharacterGrid();
    }

    // ─── Load ────────────────────────────────────────────────────
    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        LoadEditActiveChar();
    }

    // ─── Old drawing canvas handlers (kept for compat) ────────────
    private void DrawingCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
    private void DrawingCanvas_MouseMove(object sender, MouseEventArgs e) { }
    private void DrawingCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _strokes.Clear();
        _redoStack.Clear();
        _loadedStrokeCount = 0;
        string path = GetCharacterFilePath(_editActiveChar, _currentVariant);
        if (File.Exists(path)) File.Delete(path);
        _charCache.Remove(_editActiveChar);
        AlphabetEditCanvas?.InvalidateVisual();
        UpdateAlphabetProgress();
        CreateCharacterGrid();
    }

    // ─── Eraser ──────────────────────────────────────────────────
    private void EraserBtn_Click(object sender, RoutedEventArgs e)
    {
        _eraserMode = !_eraserMode;
        var activeBg     = new SolidColorBrush(Color.FromRgb(0xC0, 0x3A, 0x3A));
        var activeBorder = new SolidColorBrush(Color.FromRgb(0xE0, 0x55, 0x55));
        EraserBtn.Background      = _eraserMode ? activeBg              : GetBrush("ButtonBg");
        EraserBtn.BorderBrush     = _eraserMode ? activeBorder          : GetBrush("AppBorderBrush");
        EraserBtn.BorderThickness = _eraserMode ? new Thickness(2)      : new Thickness(1);

        if (EraserBtn.Content is StackPanel sp && sp.Children.Count >= 2
            && sp.Children[1] is TextBlock lbl)
            lbl.Text = _eraserMode ? "Erasing…" : "Erase";
    }

    private void DrawingCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) { }

    private int FindStrokeNear(Point pos, double hitRadius)
    {
        double r2 = hitRadius * hitRadius;
        for (int i = _strokes.Count - 1; i >= 0; i--)
        {
            foreach (var pt in _strokes[i])
            {
                double dx = pt.X - pos.X, dy = pt.Y - pos.Y;
                if (dx * dx + dy * dy <= r2)
                    return i;
            }
        }
        return -1;
    }

    // ─── Keyboard shortcuts ───────────────────────────────────────
    private void SaveCommand_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) =>
        SaveButton_Click(sender, new RoutedEventArgs());

    private void UndoCommand_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) =>
        UndoButton_Click(sender, new RoutedEventArgs());

    private void RedoCommand_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e) =>
        RedoButton_Click(sender, new RoutedEventArgs());

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        var m = Keyboard.Modifiers;
        bool inTextBox = Keyboard.FocusedElement is TextBox or RichTextBox;

        if (_hk.Matches("SwitchMode", k, m) && !inTextBox)
        {
            SwitchMode(_appMode == AppMode.Edit ? AppMode.Type : AppMode.Edit);
            e.Handled = true;
            return;
        }

        if (inTextBox) return;

        if (_appMode == AppMode.Edit)
        {
            if (_hk.Matches("NavLeft",      k, m)) { NavigateEditGrid(-1); e.Handled = true; }
            else if (_hk.Matches("NavRight", k, m)) { NavigateEditGrid(+1); e.Handled = true; }
            else if (_hk.Matches("ToggleEraser", k, m)) { EraserBtn_Click(EraserBtn, new RoutedEventArgs()); e.Handled = true; }
        }
    }

    private void NavigateCharGrid(int delta)
    {
        NavigateEditGrid(delta);
    }

    // ─── Undo / Redo ─────────────────────────────────────────────
    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokes.Count == 0) return;
        var last = _strokes[^1];
        _strokes.RemoveAt(_strokes.Count - 1);
        _redoStack.Push(last);
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        _strokes.Add(_redoStack.Pop());
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void RedrawCurrentStrokes() => InvalidateSkia();

    private void InvalidateSkia() => AlphabetEditCanvas?.InvalidateVisual();

    // ─── SkiaSharp paint surface (0x0 collapsed, kept for compat) ──
    private void SkiaCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        // no-op: canvas is collapsed
    }

    private SKColor GetSkColor(string key, SKColor fallback = default)
    {
        if (_resourcesReady && FindResource(key) is SolidColorBrush b)
        {
            var c = b.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return fallback;
    }

    private static void SkiaTaperedStroke(SKCanvas canvas, IList<Point> pts, SKColor color, float maxW)
    {
        int n = pts.Count;
        if (n == 0) return;

        using var paint = new SKPaint
        {
            Color       = color,
            IsAntialias = true,
            Style       = SKPaintStyle.Fill
        };

        if (n == 1)
        {
            canvas.DrawCircle((float)pts[0].X, (float)pts[0].Y, maxW * 0.5f, paint);
            return;
        }

        double totalLen = 0;
        var arcLen = new double[n];
        for (int i = 1; i < n; i++)
        {
            double dx = pts[i].X - pts[i-1].X, dy = pts[i].Y - pts[i-1].Y;
            totalLen += Math.Sqrt(dx*dx + dy*dy);
            arcLen[i] = totalLen;
        }

        var left  = new SKPoint[n];
        var right = new SKPoint[n];
        for (int i = 0; i < n; i++)
        {
            var prev = pts[Math.Max(0, i-1)];
            var next = pts[Math.Min(n-1, i+1)];
            float dx = (float)(next.X - prev.X), dy = (float)(next.Y - prev.Y);
            float len = MathF.Sqrt(dx*dx + dy*dy);
            float nx = len > 0.001f ? -dy/len : 0f;
            float ny = len > 0.001f ?  dx/len : 0f;
            double t = totalLen > 0 ? arcLen[i] / totalLen : 0.5;
            float  hw = maxW * 0.5f * (float)Math.Sin(Math.PI * t);
            if (hw < 0.4f) hw = 0.4f;
            left[i]  = new SKPoint((float)pts[i].X + nx * hw, (float)pts[i].Y + ny * hw);
            right[i] = new SKPoint((float)pts[i].X - nx * hw, (float)pts[i].Y - ny * hw);
        }

        var path = new SKPath();
        path.MoveTo(left[0]);
        AddCatmullRomToPath(path, left);
        path.LineTo(right[n-1]);
        AddCatmullRomToPath(path, right.AsEnumerable().Reverse().ToArray());
        path.Close();

        canvas.DrawPath(path, paint);
    }

    private static void AddCatmullRomToPath(SKPath path, IList<SKPoint> pts)
    {
        int n = pts.Count;
        for (int i = 0; i < n - 1; i++)
        {
            var p0 = pts[Math.Max(0, i-1)];
            var p1 = pts[i];
            var p2 = pts[i+1];
            var p3 = pts[Math.Min(n-1, i+2)];
            float cx1 = p1.X + (p2.X - p0.X) / 6f;
            float cy1 = p1.Y + (p2.Y - p0.Y) / 6f;
            float cx2 = p2.X - (p3.X - p1.X) / 6f;
            float cy2 = p2.Y - (p3.Y - p1.Y) / 6f;
            path.CubicTo(cx1, cy1, cx2, cy2, p2.X, p2.Y);
        }
    }

    private static UIElement BuildTaperedPath(IList<Point> pts, Brush stroke, double maxW)
    {
        int n = pts.Count;
        if (n == 0) return new UIElement();

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };

        if (n == 1)
        {
            group.Children.Add(new EllipseGeometry(pts[0], maxW * 0.5, maxW * 0.5));
        }
        else
        {
            double totalLen = 0;
            var arcLen = new double[n];
            arcLen[0] = 0;
            for (int i = 1; i < n; i++)
            {
                double dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
                totalLen += Math.Sqrt(dx * dx + dy * dy);
                arcLen[i] = totalLen;
            }

            double step = Math.Max(0.8, maxW * 0.12);
            int    seg  = 0;
            for (double d = 0; d <= totalLen + step; d += step)
            {
                double dc = Math.Min(d, totalLen);
                while (seg < n - 2 && arcLen[seg + 1] < dc) seg++;

                double span = arcLen[seg + 1] - arcLen[seg];
                double u    = span > 0.001 ? (dc - arcLen[seg]) / span : 0;
                double px   = pts[seg].X + u * (pts[seg + 1].X - pts[seg].X);
                double py   = pts[seg].Y + u * (pts[seg + 1].Y - pts[seg].Y);

                double t = totalLen > 0 ? dc / totalLen : 0.5;
                double r = maxW * 0.5 * Math.Sin(Math.PI * t);
                if (r < 0.4) r = 0.4;

                group.Children.Add(new EllipseGeometry(new Point(px, py), r, r));
            }
        }

        group.Freeze();
        return new System.Windows.Shapes.Path
        {
            Data             = group,
            Fill             = stroke,
            IsHitTestVisible = false
        };
    }

    private void DrawNotebookLines() { /* no-op */ }

    private void DrawDisplayLines(double width, double height)
    {
        DrawLinesOnCanvas(DisplayCanvas, width, height);
    }

    private void DrawLinesOnCanvas(Canvas canvas, double width, double height)
    {
        var brush = NotebookLineBrush;

        if (_paperStyle == PaperStyle.Lines)
        {
            int spacing = Math.Max(10, _lineSpacing);
            for (int y = spacing; y <= (int)height + spacing; y += spacing)
            {
                canvas.Children.Add(new Line
                {
                    X1 = 0, X2 = width, Y1 = y, Y2 = y,
                    Stroke = brush, StrokeThickness = _lineThickness
                });
            }
        }
        else // Dots — single Rectangle with tiling DrawingBrush (GPU tiled, no N² elements)
        {
            double spacing = Math.Max(4.0, _dotSpacing);
            double r       = Math.Max(0.3, _dotSize);

            var dotGeo = new EllipseGeometry(new Point(spacing / 2, spacing / 2), r, r);
            var tileBrush = new DrawingBrush
            {
                Drawing    = new GeometryDrawing { Geometry = dotGeo, Brush = brush },
                TileMode   = TileMode.Tile,
                Viewport   = new Rect(0, 0, spacing, spacing),
                ViewportUnits = BrushMappingMode.Absolute
            };
            canvas.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = width, Height = height, Fill = tileBrush
            });
        }
    }

    private void FontSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
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
        RefreshDisplay();
    }

    private void PaperLinesBtn_Click(object sender, RoutedEventArgs e)  => SetPaperStyle(PaperStyle.Lines);
    private void PaperDotsBtn_Click(object sender, RoutedEventArgs e)   => SetPaperStyle(PaperStyle.Dots);
    private void PaperClearBtn_Click(object sender, RoutedEventArgs e)  => SetPaperStyle(PaperStyle.Clear);

    private void SetPaperStyle(PaperStyle style)
    {
        _paperStyle = style;
        var accent  = GetBrush("AccentBrush");
        var normal  = GetBrush("ButtonBg");
        PaperLinesBtn.Background  = style == PaperStyle.Lines  ? accent : normal;
        PaperDotsBtn.Background   = style == PaperStyle.Dots   ? accent : normal;
        PaperClearBtn.Background  = style == PaperStyle.Clear  ? accent : normal;
        PaperLinesPanel.Visibility = style == PaperStyle.Lines ? Visibility.Visible : Visibility.Collapsed;
        PaperDotsPanel.Visibility  = style == PaperStyle.Dots  ? Visibility.Visible : Visibility.Collapsed;
        RefreshDisplay();
    }

    private void DotSpacingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _dotSpacing = DotSpacingSlider.Value;
        DotSpacingLabel.Text = $"{_dotSpacing:0}px";
        RefreshDisplay();
    }

    private void DotSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _dotSize = DotSizeSlider.Value;
        DotSizeLabel.Text = $"{_dotSize:0.#}px";
        RefreshDisplay();
    }

    private void PaperSizeCombo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_resourcesReady) return;
        int idx = PaperSizeCombo?.SelectedIndex ?? 0;
        bool isCustom = idx >= PaperPresets.Length;
        if (CustomResPanel != null)
            CustomResPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (!isCustom && idx >= 0 && idx < PaperPresets.Length)
        {
            _paperPresetIdx = idx;
            (_paperW, _paperMinH) = (PaperPresets[idx].W, PaperPresets[idx].H);
            RefreshGeneratedText();
        }
    }

    private void PaperCustomW_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_resourcesReady) return;
        if (int.TryParse(PaperCustomW?.Text, out int w) && w >= 100 && w <= 5000)
        { _paperW = w; RefreshGeneratedText(); }
    }

    private void PaperCustomH_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_resourcesReady) return;
        if (int.TryParse(PaperCustomH?.Text, out int h) && h >= 100 && h <= 8000)
        { _paperMinH = h; RefreshGeneratedText(); }
    }

    // ─── Settings ────────────────────────────────────────────────
    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_hk) { Owner = this };
        win.ShowDialog();
    }

    // ─── Middle mouse pan ────────────────────────────────────────
    private void TypeScrollViewer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Middle) return;
        _panning     = true;
        _panStart    = e.GetPosition(TypeScrollViewer);
        _panScrollV  = TypeScrollViewer.VerticalOffset;
        _panScrollH  = TypeScrollViewer.HorizontalOffset;
        TypeScrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void TypeScrollViewer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_panning) return;
        var pos   = e.GetPosition(TypeScrollViewer);
        double dy = _panStart.Y - pos.Y;
        double dx = _panStart.X - pos.X;
        TypeScrollViewer.ScrollToVerticalOffset(_panScrollV + dy);
        TypeScrollViewer.ScrollToHorizontalOffset(_panScrollH + dx);
        e.Handled = true;
    }

    private void TypeScrollViewer_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        TypeScrollViewer.ReleaseMouseCapture();
        e.Handled = true;
    }

    // ─── Auto-scroll to cursor ───────────────────────────────────
    private void ScrollToCursor()
    {
        if (TypeScrollViewer == null || !_resourcesReady) return;
        Dispatcher.InvokeAsync(() =>
        {
            float cw = (float)(TypeScrollViewer.ActualWidth > 0 ? TypeScrollViewer.ActualWidth : 1200);
            var layout = LayoutTypeChars(cw);
            var (_, paperY, _, _) = GetPaperBounds(cw, layout);
            double baseY = paperY + PaperInnerV + _fontSize * (_lineHeightMult * 0.75);

            double cursorY;
            if (layout.Count == 0 || _typeCursor == 0)
                cursorY = baseY;
            else if (_typeCursor >= layout.Count)
            {
                var last = layout[^1];
                cursorY = last.Cd.Ch == '\n'
                    ? baseY + last.Y + _fontSize * _lineHeightMult
                    : baseY + last.Y;
            }
            else
                cursorY = baseY + layout[_typeCursor].Y;

            double viewH = TypeScrollViewer.ActualHeight;
            double off   = TypeScrollViewer.VerticalOffset;
            if (cursorY + _fontSize * _lineHeightMult > off + viewH - 30)
                TypeScrollViewer.ScrollToVerticalOffset(cursorY + _fontSize * _lineHeightMult - viewH + 60);
            else if (cursorY < off + 30)
                TypeScrollViewer.ScrollToVerticalOffset(cursorY - 40);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private void ThicknessSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _lineThickness = e.NewValue;
        string fmt = _lineThickness % 1 == 0 ? $"{_lineThickness:0}px" : $"{_lineThickness:0.0}px";
        ThicknessLabel.Text = fmt;
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

    // ─── App mode (Edit / Type) ───────────────────────────────────
    private enum AppMode { Edit, Type }
    private AppMode _appMode = AppMode.Edit;

    private void EditModeBtn_Click(object sender, RoutedEventArgs e) => SwitchMode(AppMode.Edit);
    private void TypeModeBtn_Click(object sender, RoutedEventArgs e) => SwitchMode(AppMode.Type);

    private void SwitchMode(AppMode mode)
    {
        _appMode = mode;
        bool isEdit = mode == AppMode.Edit;

        EditLeftPanel.Visibility = isEdit ? Visibility.Visible : Visibility.Collapsed;
        TypeLeftPanel.Visibility = isEdit ? Visibility.Collapsed : Visibility.Visible;
        PreviewAlphabetBtn.Visibility = isEdit ? Visibility.Visible : Visibility.Collapsed;

        AlphabetScrollViewer.Visibility = isEdit ? Visibility.Visible : Visibility.Collapsed;
        TypeScrollViewer.Visibility     = isEdit ? Visibility.Collapsed : Visibility.Visible;

        var accentBrush  = GetBrush("AccentBrush");
        var navActiveBg  = GetBrush("NavActiveBg");
        var transparent  = new SolidColorBrush(Colors.Transparent);

        EditModeBtn.Background    = isEdit ? navActiveBg  : transparent;
        EditModeBtn.BorderBrush   = isEdit ? accentBrush  : transparent;
        EditModeBtn.BorderThickness = new Thickness(3, 0, 0, 0);
        TypeModeBtn.Background    = isEdit ? transparent  : navActiveBg;
        TypeModeBtn.BorderBrush   = isEdit ? transparent  : accentBrush;
        TypeModeBtn.BorderThickness = new Thickness(3, 0, 0, 0);

        if (isEdit)
        {
            CenterPanelTitle.Text = "ALPHABET EDIT";
            _displayMode = DisplayMode.Alphabet;
            _cursorTimer?.Stop();
            LoadAllCharsCache();
            LoadEditActiveChar();
            EditActiveCharLabel.Text = _editActiveChar.ToString();
            UpdateAlphabetGridHeight();
            AlphabetEditCanvas.InvalidateVisual();
        }
        else
        {
            CenterPanelTitle.Text = "PAPER";
            _displayMode = DisplayMode.Text;
            StartCursorBlink();
            Keyboard.Focus(this);
            RefreshGeneratedText();
        }
    }

    // ─── Generate / Preview ──────────────────────────────────────
    private bool _resourcesReady = false;
    private enum DisplayMode { None, Text, Alphabet }
    private DisplayMode _displayMode = DisplayMode.Text;

    private void RefreshDisplay()
    {
        if (!_resourcesReady) return;
        if (_displayMode == DisplayMode.Text)          RefreshGeneratedText();
        else if (_displayMode == DisplayMode.Alphabet) AlphabetEditCanvas?.InvalidateVisual();
    }

    private (float paperX, float paperY, float paperW, float paperH) GetPaperBounds(float canvasW, List<CharLayout>? layout = null)
    {
        float paperX = Math.Max(PaperPadSide, (canvasW - _paperW) / 2f);
        float paperY = PaperPadTop;
        double maxY  = layout != null && layout.Count > 0 ? layout.Max(l => l.Y) : 0;
        float paperH = Math.Max(_paperMinH, (float)(maxY + _fontSize * _lineHeightMult + PaperInnerV * 2));
        return (paperX, paperY, _paperW, paperH);
    }

    private void RefreshGeneratedText()
    {
        if (!_resourcesReady || TypeSkiaCanvas == null || TypeCanvasHost == null || TypeScrollViewer == null) return;
        _displayMode = DisplayMode.Text;
        float canvasW = (float)(TypeScrollViewer.ActualWidth > 16 ? TypeScrollViewer.ActualWidth : 1200);
        var layout    = LayoutTypeChars(canvasW);
        var (_, paperY, _, paperH) = GetPaperBounds(canvasW, layout);
        double minH   = TypeScrollViewer.ActualHeight > 0 ? TypeScrollViewer.ActualHeight : 800;
        TypeCanvasHost.Height = Math.Max(minH, paperY + paperH + PaperPadTop);
        TypeSkiaCanvas.InvalidateVisual();
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e) => RefreshGeneratedText();

    // ─── Formatting handlers ─────────────────────────────────────
    private void BoldBtn_Click(object sender, RoutedEventArgs e)   => BoldToggle();
    private void ItalicBtn_Click(object sender, RoutedEventArgs e) => ItalicToggle();

    private void ColorBtn_Click(object sender, RoutedEventArgs e) { /* legacy, no-op */ }

    private void PreviewAlphabetButton_Click(object sender, RoutedEventArgs e)
    {
        _displayMode = DisplayMode.Alphabet;
        AlphabetEditCanvas?.InvalidateVisual();
    }

    private void DrawAlphabetPreview()
    {
        // now rendered in AlphabetEditCanvas
        AlphabetEditCanvas?.InvalidateVisual();
    }

    // ─── Skia Type mode rendering ─────────────────────────────────

    private record struct CharLayout(CharData Cd, double X, double Y, double W);

    private List<CharLayout> LayoutTypeChars(double canvasW)
    {
        var result = new List<CharLayout>(_typeChars.Count);
        double targetH = _fontSize;
        double lineH   = _fontSize * _lineHeightMult;
        double gap     = targetH * 0.06 + _letterSpacing;
        double spaceW  = targetH * 0.38 + _wordSpacing;
        float  paperX  = Math.Max(PaperPadSide, ((float)canvasW - _paperW) / 2f);
        double margin  = PaperInnerH;
        double ox = paperX + margin, oy = 0;

        int i = 0;
        while (i < _typeChars.Count)
        {
            var cd = _typeChars[i];
            double startX = paperX + margin;
            double endX   = paperX + _paperW - margin;
            if (cd.Ch == '\n') { result.Add(new(cd, ox, oy, 0)); i++; ox = startX; oy += lineH; continue; }
            if (cd.Ch == ' ')  { result.Add(new(cd, ox, oy, spaceW)); i++; ox += spaceW; continue; }

            int wEnd = i; double wW = 0;
            while (wEnd < _typeChars.Count && _typeChars[wEnd].Ch != ' ' && _typeChars[wEnd].Ch != '\n')
            { wW += MeasureCharW(_typeChars[wEnd], targetH) + gap; wEnd++; }
            if (wW > gap) wW -= gap;
            if (ox > startX && ox + wW > endX) { ox = startX; oy += lineH; }

            for (int j = i; j < wEnd; j++)
            {
                double cw = MeasureCharW(_typeChars[j], targetH);
                result.Add(new(_typeChars[j], ox, oy, cw));
                ox += cw + gap;
            }
            i = wEnd;
        }
        return result;
    }

    private double MeasureCharW(CharData cd, double targetH)
    {
        if (cd.Ch == ' ' || cd.Ch == '\n') return 0;
        var variants = GetAllVariantPaths(cd.Ch);
        if (variants.Count == 0) return targetH * 0.4;
        int vi = Math.Clamp(cd.VariantIdx, 0, variants.Count - 1);
        var sd = GetStrokeDataCached(variants[vi]);
        return sd != null && sd.Height > 0 ? sd.Width * targetH / sd.Height : targetH * 0.4;
    }

    private void TypeSkia_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        if (!_resourcesReady) return;
        var skCanvas = e.Surface.Canvas;
        float fw = e.Info.Width, fh = e.Info.Height;

        // desk background (darker than paper)
        skCanvas.Clear(new SKColor(0x0D, 0x0D, 0x12));

        var layout = LayoutTypeChars(fw);
        var (paperX, paperY, paperW, paperH) = GetPaperBounds(fw, layout);

        // subtle drop shadow
        using (var shadow = new SKPaint {
            Color = new SKColor(0, 0, 0, 80),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14) })
            skCanvas.DrawRect(paperX + 5, paperY + 5, paperW, paperH, shadow);

        // paper surface
        using (var pp = new SKPaint { Color = new SKColor(0x17, 0x17, 0x22) })
            skCanvas.DrawRect(paperX, paperY, paperW, paperH, pp);

        // paper border
        using (var pb = new SKPaint {
            Color = new SKColor(0x30, 0x30, 0x48),
            Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
            skCanvas.DrawRect(paperX, paperY, paperW, paperH, pb);

        // lines / dots on paper
        if (_paperStyle != PaperStyle.Clear)
            DrawPaperSkia(skCanvas, paperX, paperY, paperW, paperH);

        double baseY    = paperY + PaperInnerV + _fontSize * (_lineHeightMult * 0.75);
        double scaledSW = _genStrokeWidth * (_fontSize / 180.0);

        foreach (var item in layout)
        {
            if (item.Cd.Ch == ' ' || item.Cd.Ch == '\n') continue;
            RenderTypeCharSkia(skCanvas, item.Cd, item.X, baseY + item.Y, scaledSW);
        }

        if (_cursorVisible && _appMode == AppMode.Type)
            DrawCursorSkia(skCanvas, layout, baseY);
    }

    private void DrawPaperSkia(SKCanvas canvas, float px, float py, float pw, float ph)
    {
        var lineCol = GetSkColor("NotebookLineBrush", new SKColor(0x2E, 0x2E, 0x44));
        canvas.Save();
        canvas.ClipRect(new SKRect(px, py, px + pw, py + ph));
        if (_paperStyle == PaperStyle.Lines)
        {
            using var p = new SKPaint { Color = lineCol, StrokeWidth = (float)_lineThickness, IsAntialias = false };
            int sp = Math.Max(10, _lineSpacing);
            for (float y = py + sp; y <= py + ph + sp; y += sp)
                canvas.DrawLine(px, y, px + pw, y, p);
        }
        else
        {
            using var p = new SKPaint { Color = lineCol, IsAntialias = true, Style = SKPaintStyle.Fill };
            float sp = (float)Math.Max(4, _dotSpacing);
            float r  = (float)Math.Max(0.3, _dotSize);
            for (float y = py + sp; y <= py + ph + sp; y += sp)
            for (float x = px + sp; x <= px + pw + sp; x += sp)
                canvas.DrawCircle(x, y, r, p);
        }
        canvas.Restore();

        // page-break dividers when paper taller than one page
        if (ph > _paperMinH * 1.1f)
        {
            using var divPaint = new SKPaint
            {
                Color = new SKColor(0xFF, 0xFF, 0xFF, 18),
                StrokeWidth = 1.5f,
                PathEffect = SKPathEffect.CreateDash([8f, 6f], 0)
            };
            for (float divY = py + _paperMinH; divY < py + ph; divY += _paperMinH)
                canvas.DrawLine(px + 8, divY, px + pw - 8, divY, divPaint);
        }
    }

    private void RenderTypeCharSkia(SKCanvas canvas, CharData cd, double x, double baselineY, double scaledSW)
    {
        var variants = GetAllVariantPaths(cd.Ch);
        if (variants.Count == 0) return;
        int vi     = Math.Clamp(cd.VariantIdx, 0, variants.Count - 1);
        var letter = GetStrokeDataCached(variants[vi]);
        if (letter == null) return;

        double scale  = letter.Height > 0 ? _fontSize / letter.Height : 1;
        double charW  = letter.Width * scale;
        double baseOff = baselineY - letter.Baseline * scale;
        float  maxW   = (float)((cd.Bold ? scaledSW * 1.6 : scaledSW) + _taperAmount);
        double rotRad = cd.RotDeg * Math.PI / 180.0;
        double cosR   = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
        double pivotX = x + charW / 2, pivotY = baselineY;
        var    col    = new SKColor(cd.Color.R, cd.Color.G, cd.Color.B, cd.Color.A);

        foreach (var s in letter.Strokes)
        {
            var pts = s.Points.Select(p =>
            {
                double px = x + p.X * scale;
                double py = baseOff + p.Y * scale + cd.JitterY;
                if (cd.Italic) px += (baselineY - py) * 0.28;
                if (cd.RotDeg != 0)
                {
                    double rx = px - pivotX, ry = py - pivotY;
                    px = pivotX + rx * cosR - ry * sinR;
                    py = pivotY + rx * sinR + ry * cosR;
                }
                return new Point(px, py);
            }).ToList();
            RenderGridStroke(canvas, pts, col, maxW);
        }
    }

    private void DrawCursorSkia(SKCanvas canvas, List<CharLayout> layout, double baseY)
    {
        double gap = _fontSize * 0.06 + _letterSpacing;
        float  startX = Math.Max(PaperPadSide, (canvas.LocalClipBounds.Width - _paperW) / 2f) + PaperInnerH;
        Point cursorPt;

        if (layout.Count == 0 || _typeCursor == 0)
            cursorPt = new(startX, baseY);
        else if (_typeCursor >= layout.Count)
        {
            var last = layout[^1];
            cursorPt = last.Cd.Ch == '\n'
                ? new(startX, baseY + last.Y + _fontSize * _lineHeightMult)
                : new(last.X + last.W + gap, baseY + last.Y);
        }
        else
        {
            var cur = layout[_typeCursor];
            bool prevNL = _typeCursor > 0 && layout[_typeCursor - 1].Cd.Ch == '\n';
            cursorPt = prevNL ? new(startX, baseY + cur.Y) : new(cur.X, baseY + cur.Y);
        }

        float ch  = (float)(_fontSize * 0.85);
        var   col = new SKColor(_typePickerColor.R, _typePickerColor.G, _typePickerColor.B);
        using var paint = new SKPaint { Color = col, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawLine((float)cursorPt.X, (float)(cursorPt.Y - ch * 0.82f),
                        (float)cursorPt.X, (float)(cursorPt.Y + ch * 0.14f), paint);
    }

    private void TypeCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var pos    = e.GetPosition(DisplayCanvas);
        float cw   = (float)(TypeSkiaCanvas.ActualWidth > 0 ? TypeSkiaCanvas.ActualWidth : 1200);
        var layout = LayoutTypeChars(cw);
        var (_, paperY, _, paperH) = GetPaperBounds(cw, layout);
        double baseY = paperY + PaperInnerV + _fontSize * (_lineHeightMult * 0.75);
        double gap   = _fontSize * 0.06 + _letterSpacing;

        int    best  = _typeChars.Count;
        double bestD = double.MaxValue;

        for (int i = 0; i < layout.Count; i++)
        {
            if (layout[i].Cd.Ch == '\n') continue;
            double cy = baseY + layout[i].Y;
            double d1 = Math.Abs(pos.X - layout[i].X) + Math.Abs(pos.Y - cy) * 0.3;
            if (d1 < bestD) { bestD = d1; best = i; }
            double d2 = Math.Abs(pos.X - (layout[i].X + layout[i].W + gap)) + Math.Abs(pos.Y - cy) * 0.3;
            if (d2 < bestD) { bestD = d2; best = i + 1; }
        }

        _typeCursor = Math.Clamp(best, 0, _typeChars.Count);
        ResetCursorBlink();
        RefreshGeneratedText();
        System.Windows.Input.Keyboard.Focus(this);
    }

    // ─── (old WPF text rendering — replaced by Skia above) ────────

    // ─── Export ──────────────────────────────────────────────────
    private void ExportPngButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory("exports");
        const int W = 3000;
        var layout   = LayoutTypeChars(W);
        double maxY  = layout.Count > 0 ? layout.Max(l => l.Y) : 0;
        int H        = (int)Math.Max(800, maxY + _fontSize * _lineHeightMult + 80);

        using var bitmap   = new SKBitmap(W, H);
        using var skCanvas = new SKCanvas(bitmap);
        skCanvas.Clear(SKColors.White);

        double baseY    = _fontSize * (_lineHeightMult * 0.75);
        double scaledSW = _genStrokeWidth * (_fontSize / 180.0);
        foreach (var item in layout)
        {
            if (item.Cd.Ch == ' ' || item.Cd.Ch == '\n') continue;
            var blackCd = item.Cd with { Color = Color.FromRgb(0, 0, 0) };
            RenderTypeCharSkia(skCanvas, blackCd, item.X, baseY + item.Y, scaledSW);
        }

        using var image  = SKImage.FromBitmap(bitmap);
        using var data   = image.Encode(SKEncodedImageFormat.Png, 100);
        string    filePath = System.IO.Path.Combine("exports", $"{DateTime.Now:yyyyMMdd_HHmmss}.png");
        using var stream = File.Create(filePath);
        data.SaveTo(stream);

        MessageBox.Show($"Saved PNG:\n{System.IO.Path.GetFullPath(filePath)}");
    }

    private void ExportJpgButton_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save as JPG",
            Filter = "JPEG Image|*.jpg",
            FileName = $"nottwrite_{DateTime.Now:yyyyMMdd_HHmmss}.jpg"
        };
        if (dlg.ShowDialog() != true) return;

        const int W = 3000;
        var layout   = LayoutTypeChars(W);
        double maxY  = layout.Count > 0 ? layout.Max(l => l.Y) : 0;
        int H        = (int)Math.Max(800, maxY + _fontSize * _lineHeightMult + 80);

        using var bitmap   = new SKBitmap(W, H);
        using var skCanvas = new SKCanvas(bitmap);
        skCanvas.Clear(SKColors.White);

        double baseY    = _fontSize * (_lineHeightMult * 0.75);
        double scaledSW = _genStrokeWidth * (_fontSize / 180.0);
        foreach (var item in layout)
        {
            if (item.Cd.Ch == ' ' || item.Cd.Ch == '\n') continue;
            var blackCd = item.Cd with { Color = Color.FromRgb(0, 0, 0) };
            RenderTypeCharSkia(skCanvas, blackCd, item.X, baseY + item.Y, scaledSW);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data  = image.Encode(SKEncodedImageFormat.Jpeg, 92);
        using var stream = File.Create(dlg.FileName);
        data.SaveTo(stream);
        MessageBox.Show($"Saved JPG:\n{System.IO.Path.GetFullPath(dlg.FileName)}");
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Save As PDF",
            FileName   = $"nottwrite_{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
            DefaultExt = ".pdf",
            Filter     = "PDF File (*.pdf)|*.pdf"
        };
        if (dlg.ShowDialog() != true) return;

        float pdfW  = _paperW + PaperPadSide * 2;
        var layout  = LayoutTypeChars(pdfW);
        var (paperX, paperY, paperW, paperH) = GetPaperBounds(pdfW, layout);
        float pdfH  = paperY + paperH + PaperPadTop;

        using var stream = File.Create(dlg.FileName);
        using var doc    = SKDocument.CreatePdf(stream);
        using var canvas = doc.BeginPage(pdfW, pdfH);

        canvas.Clear(new SKColor(0x0D, 0x0D, 0x12));
        using (var pp = new SKPaint { Color = new SKColor(0x17, 0x17, 0x22) })
            canvas.DrawRect(paperX, paperY, paperW, paperH, pp);
        if (_paperStyle != PaperStyle.Clear)
            DrawPaperSkia(canvas, paperX, paperY, paperW, paperH);

        double baseY    = paperY + PaperInnerV + _fontSize * (_lineHeightMult * 0.75);
        double scaledSW = _genStrokeWidth * (_fontSize / 180.0);
        foreach (var item in layout)
        {
            if (item.Cd.Ch == ' ' || item.Cd.Ch == '\n') continue;
            RenderTypeCharSkia(canvas, item.Cd, item.X, baseY + item.Y, scaledSW);
        }

        doc.EndPage();
        doc.Close();
        MessageBox.Show($"Saved PDF:\n{dlg.FileName}");
    }

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

        string svg = _appMode == AppMode.Type ? BuildTypedTextSvg() : BuildSpecimenSvg();
        File.WriteAllText(dlg.FileName, svg, System.Text.Encoding.UTF8);
        MessageBox.Show($"Saved SVG:\n{dlg.FileName}");
    }

    private string BuildTypedTextSvg()
    {
        const double W = 1200;
        var layout  = LayoutTypeChars(W);
        double baseY   = _fontSize * (_lineHeightMult * 0.75);
        double maxY    = layout.Count > 0 ? layout.Max(l => l.Y) : 0;
        double H       = Math.Max(400, maxY + _fontSize * _lineHeightMult + 80);
        double scaledSW = _genStrokeWidth * (_fontSize / 180.0);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {W:F1} {H:F1}\" width=\"{W:F1}\" height=\"{H:F1}\">");

        foreach (var item in layout)
        {
            if (item.Cd.Ch == ' ' || item.Cd.Ch == '\n') continue;
            var variants = GetAllVariantPaths(item.Cd.Ch);
            if (variants.Count == 0) continue;
            int vi   = Math.Clamp(item.Cd.VariantIdx, 0, variants.Count - 1);
            var data = GetStrokeDataCached(variants[vi]);
            if (data == null) continue;

            double scale   = data.Height > 0 ? _fontSize / data.Height : 1;
            double baseOff = baseY + item.Y - data.Baseline * scale;
            double thick   = item.Cd.Bold ? scaledSW * 1.6 : scaledSW;
            string col     = $"#{item.Cd.Color.R:X2}{item.Cd.Color.G:X2}{item.Cd.Color.B:X2}";

            foreach (var stroke in data.Strokes)
            {
                if (stroke.Points.Count < 2) continue;
                var pts = stroke.Points.Select(p =>
                {
                    double px = item.X + p.X * scale;
                    double py = baseOff + p.Y * scale + item.Cd.JitterY;
                    if (item.Cd.Italic) px += (baseY + item.Y - py) * 0.28;
                    return $"{px:F2},{py:F2}";
                });
                sb.AppendLine($"  <polyline points=\"{string.Join(" ", pts)}\" stroke=\"{col}\" stroke-width=\"{thick:F2}\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            }
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private string BuildSpecimenSvg()
    {
        const double cellW      = 160;
        const double cellH      = 180;
        const double charHeight = 120;
        const double padding    = 20;
        const int    cols       = 10;
        const double labelH     = 20;

        var chars = AllChars.Where(c => File.Exists(GetCharacterFilePath(c, 1))).ToList();
        int rows  = (int)Math.Ceiling(chars.Count / (double)cols);

        double svgW = cols * cellW + padding * 2;
        double svgH = rows * cellH + padding * 2 + 40;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
                      $"viewBox=\"0 0 {svgW:F1} {svgH:F1}\" " +
                      $"width=\"{svgW:F1}\" height=\"{svgH:F1}\">");
        sb.AppendLine($"  <title>{CurrentTemplate} — Handwriting Font Specimen</title>");
        sb.AppendLine($"  <rect width=\"{svgW:F1}\" height=\"{svgH:F1}\" fill=\"#1a1a1a\"/>");

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

            sb.AppendLine($"  <rect x=\"{cellX:F1}\" y=\"{cellY:F1}\" " +
                          $"width=\"{cellW:F1}\" height=\"{cellH:F1}\" " +
                          $"fill=\"#252526\" rx=\"4\"/>");

            sb.AppendLine($"  <text x=\"{cellX + 8:F1}\" y=\"{cellY + 15:F1}\" " +
                          $"font-family=\"monospace\" font-size=\"11\" fill=\"#569CD6\">" +
                          $"{EscapeXml(c.ToString())}</text>");

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

            if (taper > 0 && pts.Count >= 1)
            {
                double totalLen = 0;
                var arcLen = new double[pts.Count];
                for (int i = 1; i < pts.Count; i++)
                {
                    double dx2 = pts[i].X - pts[i-1].X, dy2 = pts[i].Y - pts[i-1].Y;
                    totalLen += Math.Sqrt(dx2*dx2 + dy2*dy2);
                    arcLen[i] = totalLen;
                }
                double effectiveMax = maxW + taper;
                double step = Math.Max(0.8, effectiveMax * 0.12);
                int seg = 0;
                var circles = new System.Text.StringBuilder();
                for (double dist = 0; dist <= totalLen + step; dist += step)
                {
                    double dc = Math.Min(dist, totalLen);
                    while (seg < pts.Count - 2 && arcLen[seg+1] < dc) seg++;
                    double span = arcLen[seg+1] - arcLen[seg];
                    double u  = span > 0.001 ? (dc - arcLen[seg]) / span : 0;
                    double cx = pts[seg].X + u * (pts[seg+1].X - pts[seg].X);
                    double cy = pts[seg].Y + u * (pts[seg+1].Y - pts[seg].Y);
                    double t  = totalLen > 0 ? dc / totalLen : 0.5;
                    double r  = effectiveMax * 0.5 * Math.Sin(Math.PI * t);
                    if (r < 0.4) r = 0.4;
                    circles.Append($"<circle cx=\"{cx:F2}\" cy=\"{cy:F2}\" r=\"{r:F2}\"/>");
                }
                sb.AppendLine($"    <g fill=\"#e8e8e8\">{circles}</g>");
            }
            else
            {
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
    private static readonly string TemplatesRoot = TemplatesPath;

    private void RefreshTemplateComboBox(string selectName)
    {
        TemplateComboBox.SelectionChanged -= TemplateComboBox_SelectionChanged;
        TemplateComboBox.Items.Clear();

        var builtIn = new[] { "Default", "School", "Fancy", "Messy" };
        IEnumerable<string> onDisk = Directory.Exists(TemplatesRoot)
            ? Directory.GetDirectories(TemplatesRoot)
                       .Select(System.IO.Path.GetFileName)
                       .Where(n => n != null && !builtIn.Contains(n))
                       .OrderBy(n => n)!
            : Enumerable.Empty<string>();

        string selectLabel = selectName;
        foreach (var n in builtIn.Concat(onDisk))
        {
            string label = n == _defaultTemplate ? $"{n} ★" : n;
            TemplateComboBox.Items.Add(label);
            if (n == selectName) selectLabel = label;
        }

        int idx = TemplateComboBox.Items.IndexOf(selectLabel);
        TemplateComboBox.SelectedIndex = idx >= 0 ? idx : 0;
        TemplateComboBox.SelectionChanged += TemplateComboBox_SelectionChanged;

        // Sync CurrentTemplate from selection
        var raw = TemplateComboBox.SelectedItem?.ToString() ?? "Default";
        CurrentTemplate = raw.TrimEnd(' ', '★');
        UpdateDefaultStar();
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var raw = TemplateComboBox.SelectedItem?.ToString() ?? "Default";
        CurrentTemplate = raw.TrimEnd(' ', '★');
        CreateCharacterGrid();
        _charCache.Clear(); _strokeDataCache.Clear();
        LoadAllCharsCache();
        UpdateDefaultStar();
        if (_appMode == AppMode.Type)
            RefreshGeneratedText();
        else
            AlphabetEditCanvas?.InvalidateVisual();
    }

    private void ClearTemplate_Click(object sender, RoutedEventArgs e) { /* removed — use Delete */ }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        var name = CurrentTemplate;
        var builtIn = new[] { "Default", "School", "Fancy", "Messy" };
        if (builtIn.Contains(name))
        {
            MessageBox.Show($"Cannot delete built-in template \"{name}\".\nYou can clear individual characters manually.",
                "Cannot delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(
                $"Delete template \"{name}\" and all its characters?\nThis cannot be undone.",
                "Delete template", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        string folder = System.IO.Path.Combine(TemplatesPath, name);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        if (_defaultTemplate == name) _defaultTemplate = "Default";
        _charCache.Clear(); _strokeDataCache.Clear();
        RefreshTemplateComboBox("Default");
        UpdateDefaultStar();
    }

    private void SetDefaultTemplate_Click(object sender, RoutedEventArgs e)
    {
        _defaultTemplate = CurrentTemplate;
        SavePenSettings();
        RefreshTemplateComboBox(CurrentTemplate);
    }

    private void UpdateDefaultStar()
    {
        if (DefaultStarLabel == null) return;
        DefaultStarLabel.Text = CurrentTemplate == _defaultTemplate ? "★" : "☆";
        DefaultStarLabel.Foreground = CurrentTemplate == _defaultTemplate
            ? GetBrush("AccentBrush") : GetBrush("SecondaryText");
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

    // ── Custom window chrome ──────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
