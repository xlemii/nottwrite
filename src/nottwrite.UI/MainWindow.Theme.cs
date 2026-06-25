using nottwrite.Core.Models;
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

namespace nottwrite.UI;

public partial class MainWindow
{
    // Themes
    public static readonly (string Id, string Name, Dictionary<string, string> Colors)[] Themes =
    [
        ("dark", "Dark", new() {
            ["AppBg"]             = "#1E1E1E",
            ["TitleBarBg"]        = "#202020",
            ["PanelBg"]           = "#282828",
            ["Surface2Bg"]        = "#242424",
            ["CardBg"]            = "#2E2E2E",
            ["CardBg2"]           = "#363636",
            ["AccentBrush"]       = "#8B7DC4",
            ["AccentHover"]       = "#A898D8",
            ["NavActiveBg"]       = "#3A3260",
            ["ButtonBg"]          = "#2E2E2E",
            ["PrimaryText"]       = "#F0F0F0",
            ["SecondaryText"]     = "#A0A0A0",
            ["AppBorderBrush"]    = "#333333",
            ["CanvasBg"]          = "#141414",
            ["NotebookLineBrush"] = "#2E2E44",
            ["StrokeBrush"]       = "#E8E8E8",
            ["SuccessBrush"]      = "#34D399",
            ["WarningBrush"]      = "#FBBF24",
            ["ErrorBrush"]        = "#F87171",
            ["DangerBrush"]       = "#F87171",
            ["InfoBrush"]         = "#60A5FA",
        }),
        ("light", "Light", new() {
            ["AppBg"]             = "#F0F0F0",
            ["TitleBarBg"]        = "#E8E8E8",
            ["PanelBg"]           = "#EBEBEB",
            ["Surface2Bg"]        = "#E2E2E2",
            ["CardBg"]            = "#FFFFFF",
            ["CardBg2"]           = "#F5F5F5",
            ["AccentBrush"]       = "#6B5DB8",
            ["AccentHover"]       = "#8878CC",
            ["NavActiveBg"]       = "#D8D3F0",
            ["ButtonBg"]          = "#DEDEDE",
            ["PrimaryText"]       = "#1A1A1A",
            ["SecondaryText"]     = "#5E5E5E",
            ["AppBorderBrush"]    = "#CCCCCC",
            ["CanvasBg"]          = "#F8F8F8",
            ["NotebookLineBrush"] = "#CDCDE0",
            ["StrokeBrush"]       = "#1A1A1A",
            ["SuccessBrush"]      = "#15803D",
            ["WarningBrush"]      = "#B45309",
            ["ErrorBrush"]        = "#DC2626",
            ["DangerBrush"]       = "#DC2626",
            ["InfoBrush"]         = "#2563EB",
        }),
        ("pink", "Pink", new() {
            ["AppBg"]             = "#1A0F18",
            ["TitleBarBg"]        = "#1E1220",
            ["PanelBg"]           = "#231628",
            ["Surface2Bg"]        = "#1F1324",
            ["CardBg"]            = "#2A1A30",
            ["CardBg2"]           = "#321E38",
            ["AccentBrush"]       = "#E070A8",
            ["AccentHover"]       = "#F09AC0",
            ["NavActiveBg"]       = "#4D1840",
            ["ButtonBg"]          = "#2A1A30",
            ["PrimaryText"]       = "#F5E8F0",
            ["SecondaryText"]     = "#A98A9E",
            ["AppBorderBrush"]    = "#3D2040",
            ["CanvasBg"]          = "#100A14",
            ["NotebookLineBrush"] = "#3D1E38",
            ["StrokeBrush"]       = "#F5E8F0",
            ["SuccessBrush"]      = "#34D399",
            ["WarningBrush"]      = "#FBBF24",
            ["ErrorBrush"]        = "#FB7185",
            ["DangerBrush"]       = "#FB7185",
            ["InfoBrush"]         = "#60A5FA",
        }),
        ("blue", "Blue", new() {
            ["AppBg"]             = "#0D1117",
            ["TitleBarBg"]        = "#161B22",
            ["PanelBg"]           = "#1C2128",
            ["Surface2Bg"]        = "#161B22",
            ["CardBg"]            = "#21262D",
            ["CardBg2"]           = "#2D333B",
            ["AccentBrush"]       = "#58A6FF",
            ["AccentHover"]       = "#79C0FF",
            ["NavActiveBg"]       = "#1F3A6E",
            ["ButtonBg"]          = "#21262D",
            ["PrimaryText"]       = "#E6EDF3",
            ["SecondaryText"]     = "#9BA5B0",
            ["AppBorderBrush"]    = "#30363D",
            ["CanvasBg"]          = "#040D1A",
            ["NotebookLineBrush"] = "#1A2D4A",
            ["StrokeBrush"]       = "#E6EDF3",
            ["SuccessBrush"]      = "#3FB950",
            ["WarningBrush"]      = "#D29922",
            ["ErrorBrush"]        = "#F85149",
            ["DangerBrush"]       = "#F85149",
            ["InfoBrush"]         = "#58A6FF",
        }),
        ("hc", "High Contrast", new() {
            ["AppBg"]             = "#000000",
            ["TitleBarBg"]        = "#000000",
            ["PanelBg"]           = "#000000",
            ["Surface2Bg"]        = "#0A0A0A",
            ["CardBg"]            = "#000000",
            ["CardBg2"]           = "#1A1A1A",
            ["AccentBrush"]       = "#FFFF00",
            ["AccentHover"]       = "#FFFF66",
            ["NavActiveBg"]       = "#333300",
            ["ButtonBg"]          = "#000000",
            ["PrimaryText"]       = "#FFFFFF",
            ["SecondaryText"]     = "#FFFFFF",
            ["AppBorderBrush"]    = "#FFFFFF",
            ["CanvasBg"]          = "#000000",
            ["NotebookLineBrush"] = "#777777",
            ["StrokeBrush"]       = "#FFFFFF",
            ["SuccessBrush"]      = "#00FF00",
            ["WarningBrush"]      = "#FFFF00",
            ["ErrorBrush"]        = "#FF5555",
            ["DangerBrush"]       = "#FF5555",
            ["InfoBrush"]         = "#00FFFF",
        }),
    ];
    public string _currentThemeId = "dark";
    public bool TiltEnabled = true;
    public bool AutoSaveEnabled  = true;
    public int  AutoSaveMinutes  = 5;

    // True while Windows High Contrast is active — overrides the chosen theme
    // visually without changing the user's saved preference.
    private bool _systemHighContrast;

    /// User-chosen theme: persists the preference, then applies it (or HC if active).
    public void ApplyTheme(string themeId)
    {
        if (Themes.All(t => t.Id != themeId)) return;
        _currentThemeId = themeId;
        ApplyThemeResources(_systemHighContrast ? "hc" : themeId);
        SaveSettings();
    }

    /// Swap the live brushes only — no persistence, no change to _currentThemeId.
    private void ApplyThemeResources(string themeId)
    {
        var theme = Themes.FirstOrDefault(t => t.Id == themeId);
        if (theme == default) return;
        foreach (var (key, hex) in theme.Colors)
            if (TryParseHexColor(hex, out var c))
                Resources[key] = new SolidColorBrush(c);
        TypeSkiaCanvas?.InvalidateVisual();
        AlphabetInputCanvas?.InvalidateVisual();
        AlphabetEditCanvas?.InvalidateVisual();
        Dispatcher.InvokeAsync(RefreshToolbarState, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// Detect Windows High Contrast at startup and follow live changes, without
    /// overwriting the user's saved theme choice.
    public void InitHighContrast()
    {
        _systemHighContrast = SystemParameters.HighContrast;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SystemParameters.HighContrast)) return;
            Dispatcher.Invoke(() =>
            {
                _systemHighContrast = SystemParameters.HighContrast;
                ApplyThemeResources(_systemHighContrast ? "hc" : _currentThemeId);
            });
        };
    }

}
