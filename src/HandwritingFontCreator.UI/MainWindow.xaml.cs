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
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;


namespace HandwritingFontCreator.UI;

public partial class MainWindow : Window
{
    private readonly List<List<Point>> _strokes = [];
    private readonly Stack<List<Point>> _redoStack = [];

    private string CurrentTemplate   = "Default";
    private string _defaultTemplate  = "Default";
    private string CurrentCharacter = "A";
    private Button? _selectedCharacterButton;
    private bool _hasUnsavedChanges;
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

    // ── Pen pressure (Windows Ink) ───────────────────────────────
    // per-point pressure, keyed by stroke-list reference
    private readonly Dictionary<List<Point>, List<double>> _pressureByStroke = new();
    private readonly Dictionary<char, List<List<double>>> _charPressures = new();
    private bool   _penActive;
    private double _penPressure = 1.0;
    private double CurrentInputPressure => _penActive ? _penPressure : 1.0;

    private void Alphabet_StylusDown(object sender, System.Windows.Input.StylusDownEventArgs e)
    {
        _penActive = true;
        UpdatePenPressure(e.GetStylusPoints(AlphabetInputCanvas));
    }
    private void Alphabet_StylusMove(object sender, System.Windows.Input.StylusEventArgs e)
    {
        if (!_penActive) return;
        UpdatePenPressure(e.GetStylusPoints(AlphabetInputCanvas));
    }
    private void Alphabet_StylusUp(object sender, System.Windows.Input.StylusEventArgs e) => _penActive = false;

    private void UpdatePenPressure(System.Windows.Input.StylusPointCollection pts)
    {
        if (pts.Count == 0) return;
        float pf = pts[^1].PressureFactor;   // 0..1
        // map to half-width factor: light touch thin, hard touch slightly bold
        _penPressure = 0.4 + 1.0 * pf;
    }
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
        double RotDeg, double JitterY, int VariantIdx,
        bool Underline = false, bool Strikethrough = false);

    private enum TextAlign { Left, Center, Right, Justify }

    private List<CharData> _typeChars  = new();
    private int            _typeCursor = 0;
    private string?        _typeFontName = null;   // null = use template strokes; non-null = system/app font name
    private int            _selAnchor  = -1;   // -1 = no selection; fixed end of selection range
    private bool      _typeBold        = false;
    private bool      _typeItalic      = false;
    private bool      _typeUnderline   = false;
    private bool      _typeStrike      = false;
    private TextAlign _textAlign       = TextAlign.Left;

    private bool HasSelection => _selAnchor >= 0 && _selAnchor != _typeCursor;
    private int  SelFrom      => HasSelection ? Math.Min(_selAnchor, _typeCursor) : _typeCursor;
    private int  SelTo        => HasSelection ? Math.Max(_selAnchor, _typeCursor) : _typeCursor;
    private void ClearSel()   => _selAnchor = -1;
    private bool           _cursorVisible = true;
    private DispatcherTimer? _cursorTimer;

    private Stack<(List<CharData> chars, int cursor)> _undoStack    = new();
    private Stack<(List<CharData> chars, int cursor)> _redoTypeStack = new();

    // Per-note history so switching notes doesn't wipe undo/redo
    private readonly Dictionary<string, Stack<(List<CharData> chars, int cursor)>> _undoByNote = new();
    private readonly Dictionary<string, Stack<(List<CharData> chars, int cursor)>> _redoByNote = new();

    // Save the live stacks into the per-note store
    private void StashHistory(string noteId)
    {
        _undoByNote[noteId] = _undoStack;
        _redoByNote[noteId] = _redoTypeStack;
    }

    // Load (or create) the stacks for a note into the live fields
    private void LoadHistory(string noteId)
    {
        _undoStack     = _undoByNote.TryGetValue(noteId, out var u) ? u : new();
        _redoTypeStack = _redoByNote.TryGetValue(noteId, out var r) ? r : new();
    }

    private void DropHistory(string noteId)
    {
        _undoByNote.Remove(noteId);
        _redoByNote.Remove(noteId);
    }

    // ── Layout cache (invalidated by bumping _layoutVersion) ──────
    private int    _layoutVersion       = 0;
    private int    _cachedLayoutVersion = -1;
    private double _cachedLayoutCanvasW = -1;
    private List<CharLayout> _cachedLayout   = new();
    private float[]          _cachedCursorXs = [];

    // ── Reusable render resources (avoid per-frame allocs) ────────
    private readonly List<Point>  _pointsBuffer   = new(256);
    private readonly List<double> _pressureBuffer = new(256);
    private readonly SKPaint     _sharedFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    private void PushUndo()
    {
        _undoStack.Push((_typeChars.ToList(), _typeCursor));
        if (_undoStack.Count > 200) _undoStack = new(_undoStack.Take(200));
        _redoTypeStack.Clear();
    }

    // ── Type mode color picker ─────────────────────────────────────
    private Color _typePickerColor = Color.FromRgb(0xE8, 0xE8, 0xE8);

    private static readonly string TemplatesPath =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "nottwrite", "templates");

    // First run: copy the templates shipped next to the exe into %APPDATA%.
    private static void SeedTemplates()
    {
        try
        {
            Directory.CreateDirectory(TemplatesPath);
            if (Directory.EnumerateDirectories(TemplatesPath).Any()) return; // already seeded

            string bundled = System.IO.Path.Combine(AppContext.BaseDirectory, "templates");
            if (!Directory.Exists(bundled)) return;

            foreach (var dir in Directory.GetDirectories(bundled))
            {
                string dest = System.IO.Path.Combine(TemplatesPath, System.IO.Path.GetFileName(dir));
                Directory.CreateDirectory(dest);
                foreach (var f in Directory.GetFiles(dir, "*.json"))
                    File.Copy(f, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(f)), true);
            }
        }
        catch { }
    }

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
        InvalidateLayout(); RefreshGeneratedText();
    }

    private void WordSpacingSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _wordSpacing = WordSpacingSlider.Value;
        WordSpacingLabel.Text = _wordSpacing.ToString("+0;-0;0") + "px";
        InvalidateLayout(); RefreshGeneratedText();
    }

    private void LineHeightSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_resourcesReady) return;
        _lineHeightMult = LineHeightSlider.Value;
        LineHeightLabel.Text = _lineHeightMult.ToString("0.0") + "×";
        InvalidateLayout(); RefreshGeneratedText();
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
        LoadSettings();   // theme + tilt + autosave from settings.json
        Loaded += (_, _) => { ApplyTheme(_currentThemeId); StartAutoSaveTimer(); MaybeShowOnboarding(); };

        Resources["StrokeBrush"]       = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        Resources["LoadedStrokeBrush"] = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
        Resources["NotebookLineBrush"] = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
        _resourcesReady = true;

        LoadCustomChars();
        LoadPenSettings();
        SeedTemplates();
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
            UpdateAlphabetProgress();   // seed the global glyph counter
            SwitchMode(AppMode.Notes);
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
        {
            _pressureByStroke.TryGetValue(s, out var pr);
            data.Strokes.Add(new Stroke
            {
                Points = s.Select((p, i) => new PointData
                {
                    X = p.X, Y = p.Y,
                    Pressure = pr != null && i < pr.Count ? pr[i] : 1.0,
                }).ToList()
            });
        }
        File.WriteAllText(path, JsonSerializer.Serialize(data));
        _charCache[_editActiveChar] = (_strokes.Select(s => s.ToList()).ToList(), GridCellW, GridCellH);
        _charPressures[_editActiveChar] = _strokes
            .Select(s => _pressureByStroke.TryGetValue(s, out var pr)
                ? new List<double>(pr)
                : Enumerable.Repeat(1.0, s.Count).ToList())
            .ToList();
        _strokeDataCache.Remove(path);
        _hasUnsavedChanges = false;
        UpdateAlphabetProgress();
        FontPreviewCanvas?.InvalidateVisual();
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
        var charPr = new List<List<double>>();
        foreach (var s in data.Strokes)
        {
            var pts = s.Points.Select(p => new Point(p.X * sx, p.Y * sy)).ToList();
            var pr  = s.Points.Select(p => p.Pressure).ToList();
            _strokes.Add(pts);
            _pressureByStroke[pts] = pr;
            charPr.Add(pr);
        }
        _charCache[_editActiveChar] = (_strokes.Select(s => s.ToList()).ToList(), GridCellW, GridCellH);
        _charPressures[_editActiveChar] = charPr;
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
        List<List<double>>? pressures = null;
        double fileW = GridCellW, fileH = GridCellH;
        if (c == _editActiveChar)
        {
            strokes = _strokes; fileW = GridCellW; fileH = GridCellH;
            pressures = _strokes.Select(s => _pressureByStroke.TryGetValue(s, out var pr) ? pr : null!)
                                .ToList();
        }
        else if (_charCache.TryGetValue(c, out var cached))
        {
            strokes = cached.strokes; fileW = cached.fileW; fileH = cached.fileH;
            _charPressures.TryGetValue(c, out pressures);
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
            for (int si = 0; si < strokes.Count; si++)
            {
                var stroke = strokes[si];
                if (stroke.Count == 0) continue;
                var pts = stroke.Select(p => new Point(p.X * sx + cx, p.Y * sy + cy)).ToList();
                var pr = pressures != null && si < pressures.Count ? pressures[si] : null;
                RenderGridStroke(canvas, pts, col, maxW, pr);
            }
        }
    }

    private void RenderGridStroke(SKCanvas canvas, IList<Point> pts, SKColor color, float maxW,
        IList<double>? pressures = null)
    {
        int n = pts.Count;
        if (n == 0) return;
        _sharedFillPaint.Color = color;   // reuse pooled paint — no alloc

        if (n == 1)
        {
            float r = maxW * 0.5f * (pressures is { Count: > 0 } ? (float)pressures[0] : 1f);
            canvas.DrawCircle((float)pts[0].X, (float)pts[0].Y, r, _sharedFillPaint);
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
            if (pressures != null && i < pressures.Count) hw *= (float)pressures[i];   // pen pressure
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
        AddCatmullRomToPathReversed(path, right);
        path.Close();
        canvas.DrawPath(path, _sharedFillPaint);
    }

    // ── Live font preview (pangram from current template glyphs) ──
    private const string PreviewText = "The quick brown fox";

    private void FontPreviewCanvas_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width, h = e.Info.Height;
        canvas.Clear(GetSkColor("CanvasBg", new SKColor(0x14, 0x14, 0x14)));
        if (_typeFontName != null) return;   // system font — preview is its own renderer; skip

        // measure: lay glyphs in natural units (height = 100)
        const double targetH = 100;
        double pad = 10;
        var glyphs = new List<(StrokeData sd, double x, double gscale)>();
        double cursor = 0;
        foreach (char ch in PreviewText)
        {
            if (ch == ' ') { cursor += targetH * 0.34; continue; }
            var variants = GetAllVariantPaths(ch);
            if (variants.Count == 0) { cursor += targetH * 0.34; continue; }
            var sd = GetStrokeDataCached(variants[0]);
            if (sd == null || sd.Height < 1) { cursor += targetH * 0.34; continue; }
            double gscale = targetH / sd.Height;
            glyphs.Add((sd, cursor, gscale));
            cursor += sd.Width * gscale + targetH * 0.06;
        }
        if (glyphs.Count == 0) return;

        double totalW = cursor;
        // fit to canvas (uniform scale), centre vertically
        double S = Math.Min((w - pad * 2) / totalW, (h - pad) / targetH);
        double ox = pad + ((w - pad * 2) - totalW * S) / 2;
        double oy = (h - targetH * S) / 2;

        var col = GetSkColor("StrokeBrush", new SKColor(0xE8, 0xE8, 0xE8));
        float maxW = Math.Max(0.8f, (float)(_genStrokeWidth * S * 0.9));

        foreach (var (sd, gx, gscale) in glyphs)
        {
            foreach (var stroke in sd.Strokes)
            {
                if (stroke.Points.Count == 0) continue;
                var pts = stroke.Points
                    .Select(p => new Point(ox + (gx + p.X * gscale) * S, oy + p.Y * gscale * S))
                    .ToList();
                var pr = stroke.Points.Select(p => p.Pressure).ToList();
                RenderGridStroke(canvas, pts, col, maxW, pr);
            }
        }
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
        _pressureByStroke[_editCurrentStroke] = [CurrentInputPressure];
        AlphabetInputCanvas.CaptureMouse();
        AlphabetEditCanvas.InvalidateVisual();
    }

    private void Alphabet_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawingOnGrid || _editCurrentStroke == null) return;
        var pos = e.GetPosition(AlphabetInputCanvas);
        _editCurrentStroke.Add(new Point(pos.X - _editCellOrigin.X, pos.Y - _editCellOrigin.Y));
        if (_pressureByStroke.TryGetValue(_editCurrentStroke, out var pr)) pr.Add(CurrentInputPressure);
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
                // carry pressure across smoothing (count preserved); re-key the dict
                if (_pressureByStroke.Remove(_editCurrentStroke, out var oldPr))
                    _pressureByStroke[smoothed] = oldPr.Count == smoothed.Count
                        ? oldPr : Enumerable.Repeat(1.0, smoothed.Count).ToList();
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

        bool shift = (m & System.Windows.Input.ModifierKeys.Shift) != 0;
        bool ctrl  = (m & System.Windows.Input.ModifierKeys.Control) != 0;

        switch (k)
        {
            case System.Windows.Input.Key.Back:
                if (HasSelection) { DeleteSelection(); }
                else if (_typeCursor > 0) { PushUndo(); _typeChars.RemoveAt(_typeCursor - 1); _typeCursor--; InvalidateLayout(); }
                e.Handled = true; ClearSel(); ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Delete:
                if (HasSelection) { DeleteSelection(); }
                else if (_typeCursor < _typeChars.Count) { PushUndo(); _typeChars.RemoveAt(_typeCursor); InvalidateLayout(); }
                e.Handled = true; ClearSel(); ResetCursorBlink(); RefreshGeneratedText(); break;

            case System.Windows.Input.Key.Left:
                if (!shift && HasSelection) { _typeCursor = SelFrom; ClearSel(); }
                else { if (shift && !HasSelection) _selAnchor = _typeCursor; if (_typeCursor > 0) _typeCursor--; if (shift && _selAnchor == _typeCursor) ClearSel(); }
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Right:
                if (!shift && HasSelection) { _typeCursor = SelTo; ClearSel(); }
                else { if (shift && !HasSelection) _selAnchor = _typeCursor; if (_typeCursor < _typeChars.Count) _typeCursor++; if (shift && _selAnchor == _typeCursor) ClearSel(); }
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Home:
                if (shift && !HasSelection) _selAnchor = _typeCursor;
                while (_typeCursor > 0 && _typeChars[_typeCursor - 1].Ch != '\n') _typeCursor--;
                if (shift && _selAnchor == _typeCursor) ClearSel();
                if (!shift) ClearSel();
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.End:
                if (shift && !HasSelection) _selAnchor = _typeCursor;
                while (_typeCursor < _typeChars.Count && _typeChars[_typeCursor].Ch != '\n') _typeCursor++;
                if (shift && _selAnchor == _typeCursor) ClearSel();
                if (!shift) ClearSel();
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); ScrollToCursor(); break;

            case System.Windows.Input.Key.Space:
                InsertChar(' ');
                e.Handled = true; break;

            case System.Windows.Input.Key.Return:
                InsertChar('\n');
                e.Handled = true; break;

            case System.Windows.Input.Key.A when ctrl:
                _selAnchor = 0; _typeCursor = _typeChars.Count;
                e.Handled = true; ResetCursorBlink(); RefreshGeneratedText(); break;

            case System.Windows.Input.Key.C when ctrl:
                if (HasSelection)
                    System.Windows.Clipboard.SetText(GetSelectedText());
                e.Handled = true; break;

            case System.Windows.Input.Key.X when ctrl:
                if (HasSelection)
                {
                    System.Windows.Clipboard.SetText(GetSelectedText());
                    DeleteSelection();
                    RefreshGeneratedText();
                }
                e.Handled = true; break;

            case System.Windows.Input.Key.V when ctrl:
                var pasted = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrEmpty(pasted))
                    foreach (char pc in pasted) InsertChar(pc);
                e.Handled = true; break;

            case System.Windows.Input.Key.S when ctrl:
                // save current note (override template-save hotkey in Type mode)
                if (_currentNoteId != null)
                {
                    _tabSnapshots[_currentNoteId] = _typeChars.Select(ToSerial).ToList();
                    AutoSaveCurrentNote();
                    ShowToast("Note saved", ToastKind.Success);
                }
                e.Handled = true; break;

            default:
                if (_hk.Matches("Bold",   k, m))
                {
                    if (HasSelection) ApplyFormatToSelection(cd => cd with { Bold = !cd.Bold });
                    else BoldToggle();
                    e.Handled = true; break;
                }
                if (_hk.Matches("Italic", k, m))
                {
                    if (HasSelection) ApplyFormatToSelection(cd => cd with { Italic = !cd.Italic });
                    else ItalicToggle();
                    e.Handled = true; break;
                }
                if (k == System.Windows.Input.Key.U && ctrl)
                {
                    if (HasSelection) ApplyFormatToSelection(cd => cd with { Underline = !cd.Underline });
                    else UnderlineToggle();
                    e.Handled = true; break;
                }
                if (_hk.Matches("Undo",   k, m) && _undoStack.Count > 0)
                {
                    _redoTypeStack.Push((_typeChars.ToList(), _typeCursor));
                    var (uc, ucur) = _undoStack.Pop();
                    _typeChars = uc; _typeCursor = Math.Clamp(ucur, 0, uc.Count);
                    e.Handled = true; InvalidateLayout(); ResetCursorBlink(); RefreshGeneratedText();
                    break;
                }
                if (_hk.Matches("Redo",   k, m) && _redoTypeStack.Count > 0)
                {
                    _undoStack.Push((_typeChars.ToList(), _typeCursor));
                    var (rc, rcur) = _redoTypeStack.Pop();
                    _typeChars = rc; _typeCursor = Math.Clamp(rcur, 0, rc.Count);
                    e.Handled = true; InvalidateLayout(); ResetCursorBlink(); RefreshGeneratedText();
                    break;
                }
                break;
        }
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        PushUndo();
        InvalidateLayout();
        int from = SelFrom, to = SelTo;
        _typeChars.RemoveRange(from, to - from);
        _typeCursor = from;
        ClearSel();
    }

    private void InsertChar(char ch)
    {
        if (HasSelection) DeleteSelection();
        PushUndo();
        var variants = GetAllVariantPaths(ch);
        int vi = variants.Count > 0 ? Random.Shared.Next(variants.Count) : 0;
        double rot = _randomRotation > 0 ? (Random.Shared.NextDouble() * 2 - 1) * _randomRotation : 0;
        double jit = _randomOffsetY  > 0 ? (Random.Shared.NextDouble() * 2 - 1) * _randomOffsetY  : 0;
        var insertColor = _typePickerColor;
        if (_typePickerColor == System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8) &&
            variants.Count > 0 && GetStrokeDataCached(variants[vi]) is { } sd && sd.Color != null &&
            TryParseHexColor(sd.Color, out var tc))
            insertColor = tc;
        _typeChars.Insert(_typeCursor, new CharData(ch, _typeBold, _typeItalic, insertColor, rot, jit, vi,
            _typeUnderline, _typeStrike));
        _typeCursor++;
        InvalidateLayout();
        ResetCursorBlink();
        RefreshGeneratedText();
        ScrollToCursor();
    }

    private void ApplyFormatToSelection(Func<CharData, CharData> transform)
    {
        if (!HasSelection) return;
        PushUndo();
        int from = SelFrom, to = SelTo;
        for (int i = from; i < to; i++)
            _typeChars[i] = transform(_typeChars[i]);
        RefreshGeneratedText();
    }

    private string GetSelectedText()
    {
        if (!HasSelection) return "";
        return new string(_typeChars.Skip(SelFrom).Take(SelTo - SelFrom).Select(c => c.Ch).ToArray());
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
        RefreshToolbarState();
    }

    private void ItalicToggle()
    {
        _typeItalic = !_typeItalic;
        RefreshToolbarState();
    }

    private void UnderlineToggle()    { _typeUnderline = !_typeUnderline; RefreshToolbarState(); RefreshGeneratedText(); }
    private void StrikeToggle()       { _typeStrike    = !_typeStrike;    RefreshToolbarState(); RefreshGeneratedText(); }

    private void AlignLeft_Click(object s, RoutedEventArgs e)    => SetAlign(TextAlign.Left);
    private void AlignCenter_Click(object s, RoutedEventArgs e)  => SetAlign(TextAlign.Center);
    private void AlignRight_Click(object s, RoutedEventArgs e)   => SetAlign(TextAlign.Right);
    private void AlignJustify_Click(object s, RoutedEventArgs e) => SetAlign(TextAlign.Justify);

    private void SetAlign(TextAlign a)
    {
        _textAlign = a;
        InvalidateLayout();
        RefreshToolbarState();
        RefreshGeneratedText();
    }

    private void TbBold_Click(object s, RoutedEventArgs e)
    { if (HasSelection) ApplyFormatToSelection(cd => cd with { Bold = !cd.Bold }); else BoldToggle(); }
    private void TbItalic_Click(object s, RoutedEventArgs e)
    { if (HasSelection) ApplyFormatToSelection(cd => cd with { Italic = !cd.Italic }); else ItalicToggle(); }
    private void TbUnderline_Click(object s, RoutedEventArgs e)
    { if (HasSelection) ApplyFormatToSelection(cd => cd with { Underline = !cd.Underline }); else UnderlineToggle(); }
    private void TbStrike_Click(object s, RoutedEventArgs e)
    { if (HasSelection) ApplyFormatToSelection(cd => cd with { Strikethrough = !cd.Strikethrough }); else StrikeToggle(); }

    private Button? _tbBold, _tbItalic, _tbUnderline, _tbStrike;
    private Button? _tbAlignL, _tbAlignC, _tbAlignR, _tbAlignJ;

    private void RefreshToolbarState()
    {
        SetTbState(_tbBold,      _typeBold);
        SetTbState(_tbItalic,    _typeItalic);
        SetTbState(_tbUnderline, _typeUnderline);
        SetTbState(_tbStrike,    _typeStrike);
        SetTbState(_tbAlignL,    _textAlign == TextAlign.Left);
        SetTbState(_tbAlignC,    _textAlign == TextAlign.Center);
        SetTbState(_tbAlignR,    _textAlign == TextAlign.Right);
        SetTbState(_tbAlignJ,    _textAlign == TextAlign.Justify);
        // color bar in toolbar
        if (TbColorBar != null)
            TbColorBar.Background = new SolidColorBrush(_typePickerColor);
    }

    private void SetTbState(Button? btn, bool active)
    {
        if (btn == null) return;
        btn.Background = active ? GetBrush("AccentBrush") : GetBrush("ButtonBg");
        btn.Foreground = active ? GetBrush("AppBg")       : GetBrush("PrimaryText");
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
            var brush = new SolidColorBrush(_typePickerColor);
            TypeColorSwatch.Background = brush;
            if (TbColorBar != null) TbColorBar.Background = brush;
            if (HasSelection)
                ApplyFormatToSelection(cd => cd with { Color = _typePickerColor });
            RefreshGeneratedText();
        }
    }

    // ─── Character grid (hidden compat) ──────────────────────────
    private bool CharacterExists(string character) =>
        File.Exists(GetCharacterFilePath(character[0]));

    private void UpdateProgress()
    {
        UpdateAlphabetProgress();
        FontPreviewCanvas?.InvalidateVisual();
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

        // global indicator on the Edit nav button (always visible)
        if (EditCountText != null) EditCountText.Text = $"{done}/{total}";
        if (EditNavSubtitle != null)
            EditNavSubtitle.Text = done == 0 ? "draw characters"
                                 : done >= total ? "font complete ✓"
                                 : $"{done} of {total} drawn";
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
        FontPreviewCanvas?.InvalidateVisual();
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
            ShowToast("Draw a letter first", ToastKind.Warning);
            return;
        }

        SaveEditActiveChar();
        ShowToast($"Saved character '{_editActiveChar}'", ToastKind.Success);
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
        _pressureByStroke.Clear();
        _charPressures.Remove(_editActiveChar);
        string path = GetCharacterFilePath(_editActiveChar, _currentVariant);
        if (File.Exists(path)) File.Delete(path);
        _charCache.Remove(_editActiveChar);
        AlphabetEditCanvas?.InvalidateVisual();
        UpdateAlphabetProgress();
        FontPreviewCanvas?.InvalidateVisual();
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

        // Command palette (Ctrl+K) — global, works from anywhere
        if (k == Key.K && (m & ModifierKeys.Control) != 0)
        {
            ToggleCommandPalette();
            e.Handled = true;
            return;
        }
        if (CommandPaletteOverlay.Visibility == Visibility.Visible)
        {
            HandleCommandPaletteKey(e);
            if (e.Handled) return;
        }

        if (_hk.Matches("SwitchMode", k, m))
        {
            if (inTextBox)
            {
                // let Tab insert a tab character in text boxes — do not intercept
                return;
            }
            AppMode next = _appMode switch
            {
                AppMode.Notes => AppMode.Type,
                AppMode.Type  => AppMode.Edit,
                _             => AppMode.Notes,
            };
            SwitchMode(next);
            e.Handled = true;
            return;
        }

        if (k == Key.Escape && _appMode == AppMode.Type && _currentNoteId != null)
        {
            RequestCloseTab(_currentNoteId);
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

    private void SkiaTaperedStroke(SKCanvas canvas, IList<Point> pts, SKColor color, float maxW)
    {
        int n = pts.Count;
        if (n == 0) return;
        _sharedFillPaint.Color = color;

        if (n == 1)
        {
            canvas.DrawCircle((float)pts[0].X, (float)pts[0].Y, maxW * 0.5f, _sharedFillPaint);
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
        AddCatmullRomToPathReversed(path, right);
        path.Close();

        canvas.DrawPath(path, _sharedFillPaint);
    }

    // Same curve as AddCatmullRomToPath but traverses pts backward — avoids Reverse().ToArray().
    private static void AddCatmullRomToPathReversed(SKPath path, IList<SKPoint> pts)
    {
        int n = pts.Count;
        for (int i = n - 1; i >= 1; i--)
        {
            var p0 = pts[Math.Min(n - 1, i + 1)];
            var p1 = pts[i];
            var p2 = pts[i - 1];
            var p3 = pts[Math.Max(0, i - 2)];
            float cx1 = p1.X + (p2.X - p0.X) / 6f;
            float cy1 = p1.Y + (p2.Y - p0.Y) / 6f;
            float cx2 = p2.X - (p3.X - p1.X) / 6f;
            float cy2 = p2.Y - (p3.Y - p1.Y) / 6f;
            path.CubicTo(cx1, cy1, cx2, cy2, p2.X, p2.Y);
        }
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
        InvalidateLayout(); RefreshGeneratedText();
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

    // ─── App mode ────────────────────────────────────────────────
    private enum AppMode { Edit, Type, Notes }
    private AppMode _appMode = AppMode.Notes;

    private void EditModeBtn_Click(object sender, RoutedEventArgs e)  => SwitchMode(AppMode.Edit);
    private void TypeModeBtn_Click(object sender, RoutedEventArgs e)  => SwitchMode(AppMode.Type);
    private void NotesModeBtn_Click(object sender, RoutedEventArgs e) => SwitchMode(AppMode.Notes);

    // ── Type-mode font combo ──────────────────────────────────────
    private bool _fontComboReady = false;

    private void PopulateTypeFontCombo()
    {
        if (_fontComboReady) return;
        _fontComboReady = true;

        TypeFontCombo.SelectionChanged -= TypeFontCombo_SelectionChanged;
        TypeFontCombo.Items.Clear();

        // --- Handwriting templates group header ---
        TypeFontCombo.Items.Add(new ComboBoxItem
        {
            Content = "── Handwriting ──",
            IsEnabled = false,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        });

        var builtIn = new[] { "Default", "School", "Fancy", "Messy" };
        IEnumerable<string> onDisk = Directory.Exists(TemplatesPath)
            ? Directory.GetDirectories(TemplatesPath)
                       .Select(System.IO.Path.GetFileName)
                       .Where(n => n != null && !builtIn.Contains(n))
                       .OrderBy(n => n)!
            : Enumerable.Empty<string>();

        int selectIdx = 1; // default = first template
        int i = 1;
        foreach (var name in builtIn.Concat(onDisk))
        {
            var item = new ComboBoxItem { Content = name, Tag = $"template:{name}" };
            TypeFontCombo.Items.Add(item);
            if (name == CurrentTemplate && _typeFontName == null) selectIdx = i;
            i++;
        }

        // --- System fonts group header ---
        TypeFontCombo.Items.Add(new ComboBoxItem
        {
            Content = "── System Fonts ──",
            IsEnabled = false,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        });

        var sysFonts = Fonts.SystemFontFamilies
                            .Select(f => f.Source)
                            .OrderBy(n => n)
                            .ToList();
        foreach (var name in sysFonts)
        {
            var item = new ComboBoxItem { Content = name, Tag = $"system:{name}" };
            TypeFontCombo.Items.Add(item);
            if (_typeFontName == name) selectIdx = TypeFontCombo.Items.Count - 1;
            i++;
        }

        TypeFontCombo.SelectedIndex = selectIdx;
        TypeFontCombo.SelectionChanged += TypeFontCombo_SelectionChanged;
    }

    // Re-select the toolbar font picker to match current state (no rebuild).
    private void SyncTypeFontCombo()
    {
        if (!_fontComboReady || TypeFontCombo == null) return;
        string want = _typeFontName != null ? $"system:{_typeFontName}" : $"template:{CurrentTemplate}";
        TypeFontCombo.SelectionChanged -= TypeFontCombo_SelectionChanged;
        foreach (var obj in TypeFontCombo.Items)
            if (obj is ComboBoxItem ci && (ci.Tag as string) == want)
            {
                TypeFontCombo.SelectedItem = ci;
                break;
            }
        TypeFontCombo.SelectionChanged += TypeFontCombo_SelectionChanged;
    }

    private void TypeFontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TypeFontCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        if (tag.StartsWith("template:"))
        {
            var tname = tag["template:".Length..];
            _typeFontName = null;
            CurrentTemplate = tname;
            _charCache.Clear();
            _strokeDataCache.Clear();
            RefreshTemplateComboBox(tname);
        }
        else if (tag.StartsWith("system:"))
        {
            _typeFontName = tag["system:".Length..];
            _cachedTfName = null; // force typeface cache rebuild
        }
        BindFontToCurrentNote();   // remember choice on the open note
        InvalidateLayout();
        RefreshGeneratedText();
    }

    private void SwitchMode(AppMode mode)
    {
        bool changed = _lastFadeMode != mode;
        _lastFadeMode = mode;
        _appMode = mode;
        bool isEdit  = mode == AppMode.Edit;
        bool isType  = mode == AppMode.Type;
        bool isNotes = mode == AppMode.Notes;

        EditLeftPanel.Visibility        = isEdit  ? Visibility.Visible   : Visibility.Collapsed;
        TypeLeftPanel.Visibility        = isType  ? Visibility.Visible   : Visibility.Collapsed;
        NotesLeftPanel.Visibility       = isNotes ? Visibility.Visible   : Visibility.Collapsed;
        AlphabetScrollViewer.Visibility = isEdit  ? Visibility.Visible   : Visibility.Collapsed;
        TypeScrollViewer.Visibility     = isType  ? Visibility.Visible   : Visibility.Collapsed;
        NotesPanel.Visibility           = isNotes ? Visibility.Visible   : Visibility.Collapsed;
        TypeToolbar.Visibility          = isType  ? Visibility.Visible   : Visibility.Collapsed;
        CenterPanelHeader.Visibility = isType ? Visibility.Collapsed : Visibility.Visible;
        if (isType) PopulateTypeFontCombo();

        bool noteActive = isType && _currentNoteId != null;
        NotesBackBtn.Visibility  = noteActive ? Visibility.Visible : Visibility.Collapsed;
        NotesBackSep.Visibility  = noteActive ? Visibility.Visible : Visibility.Collapsed;

        if (isType) RefreshTabBar();
        else NoteTabBar.Visibility = Visibility.Collapsed;

        var accent      = GetBrush("AccentBrush");
        var activeBg    = GetBrush("NavActiveBg");
        var none        = new SolidColorBrush(Colors.Transparent);

        void NavStyle(Button btn, bool active)
        {
            btn.Background      = active ? activeBg : none;
            btn.BorderBrush     = active ? accent   : none;
            btn.BorderThickness = new Thickness(3, 0, 0, 0);
        }
        NavStyle(NotesModeBtn, isNotes);
        NavStyle(EditModeBtn,  isEdit);
        NavStyle(TypeModeBtn,  isType);

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
            FontPreviewCanvas?.InvalidateVisual();
        }
        else if (isType)
        {
            CenterPanelTitle.Text = "PAPER";
            _displayMode = DisplayMode.Text;
            StartCursorBlink();
            Keyboard.Focus(this);
            if (_tbBold == null)
            {
                _tbBold      = TbBoldBtn;
                _tbItalic    = TbItalicBtn;
                _tbUnderline = TbUnderlineBtn;
                _tbStrike    = TbStrikeBtn;
                _tbAlignL    = TbAlignLBtn;
                _tbAlignC    = TbAlignCBtn;
                _tbAlignR    = TbAlignRBtn;
                _tbAlignJ    = TbAlignJBtn;
                ApplyToolbarButtonStyle(TbBoldBtn, TbItalicBtn, TbUnderlineBtn, TbStrikeBtn,
                                        TbAlignLBtn, TbAlignCBtn, TbAlignRBtn, TbAlignJBtn);
            }
            RefreshToolbarState();
            RefreshGeneratedText();
        }
        else // Notes
        {
            CenterPanelTitle.Text = "MY NOTES";
            _cursorTimer?.Stop();
            LoadNotes();
        }

        if (changed) FadeCenterContent();
    }

    private AppMode? _lastFadeMode;

    private void FadeCenterContent()
    {
        if (CenterContent == null) return;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0,
            TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        CenterContent.BeginAnimation(OpacityProperty, fade);
    }

    private void ApplyToolbarButtonStyle(params Button[] buttons)
    {
        foreach (var b in buttons)
        {
            b.Background    = GetBrush("ButtonBg");
            b.Foreground    = GetBrush("PrimaryText");
            b.BorderBrush   = GetBrush("AppBorderBrush");
            b.BorderThickness = new Thickness(1);
            b.Cursor        = Cursors.Hand;
            b.Template      = MakeTbButtonTemplate();
        }
    }

    private static ControlTemplate? _tbBtnTemplate;
    private static ControlTemplate MakeTbButtonTemplate()
    {
        if (_tbBtnTemplate != null) return _tbBtnTemplate;
        var tmpl = new ControlTemplate(typeof(Button));
        var bd   = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        bd.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        bd.SetBinding(Border.BorderThicknessProperty,
            new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
        bd.AppendChild(cp);
        tmpl.VisualTree = bd;
        _tbBtnTemplate = tmpl;
        return tmpl;
    }

    // ─── Generate / Preview ──────────────────────────────────────
    private bool _resourcesReady = false;
    private enum DisplayMode { None, Text, Alphabet }
    private DisplayMode _displayMode = DisplayMode.Text;

    // ─── (old WPF text rendering — replaced by Skia above) ────────

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
