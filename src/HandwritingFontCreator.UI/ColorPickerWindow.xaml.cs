using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HandwritingFontCreator.UI;

public partial class ColorPickerWindow : Window
{
    private float _hue        = 0f;   // 0–360
    private float _saturation = 1f;   // 0–1
    private float _value      = 1f;   // 0–1

    private bool _updatingInputs = false;
    private bool _draggingSv     = false;
    private bool _draggingHue   = false;

    public Color SelectedColor { get; private set; } = Colors.White;

    public ColorPickerWindow(Color initial)
    {
        InitializeComponent();
        var (h, s, v) = RgbToHsv(initial);
        _hue = h; _saturation = s; _value = v;
        OldColorSwatch.Background = new SolidColorBrush(initial);
        Loaded += (_, _) => SyncAll();
    }

    // ── Paint ─────────────────────────────────────────────────────────

    private void SvCanvas_Paint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width, h = e.Info.Height;

        // Horizontal: white → pure hue
        var (pr, pg, pb) = HsvToSkColor(_hue, 1f, 1f);
        using var hShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(w, 0),
            new[] { SKColors.White, new SKColor(pr, pg, pb) },
            null, SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, new SKPaint { Shader = hShader, IsAntialias = true });

        // Vertical overlay: transparent → black
        using var vShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, h),
            new[] { SKColors.Transparent, SKColors.Black },
            null, SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, new SKPaint { Shader = vShader, IsAntialias = true });

        // Cursor
        float cx = _saturation * w;
        float cy = (1f - _value) * h;
        using var outer = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true };
        using var inner = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(cx, cy, 7, outer);
        canvas.DrawCircle(cx, cy, 7, inner);
    }

    private void HueCanvas_Paint(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float w = e.Info.Width, h = e.Info.Height;

        var colors = new SKColor[]
        {
            new SKColor(255, 0, 0),
            new SKColor(255, 255, 0),
            new SKColor(0, 255, 0),
            new SKColor(0, 255, 255),
            new SKColor(0, 0, 255),
            new SKColor(255, 0, 255),
            new SKColor(255, 0, 0)
        };
        var positions = new float[] { 0f, 1f/6, 2f/6, 3f/6, 4f/6, 5f/6, 1f };

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, h),
            colors, positions, SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, w, h, new SKPaint { Shader = shader, IsAntialias = true });

        // Indicator
        float y = (_hue / 360f) * h;
        using var linePaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2, Style = SKPaintStyle.Stroke, IsAntialias = true };
        canvas.DrawLine(0, y, w, y, linePaint);
        linePaint.Color = SKColors.Black;
        linePaint.StrokeWidth = 0.8f;
        canvas.DrawLine(0, y, w, y, linePaint);
    }

    // ── SV mouse ──────────────────────────────────────────────────────

    private void SvInput_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = true;
        SvInputCanvas.CaptureMouse();
        ApplySvPoint(e.GetPosition(SvInputCanvas));
    }

    private void SvInput_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingSv) return;
        ApplySvPoint(e.GetPosition(SvInputCanvas));
    }

    private void SvInput_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingSv = false;
        SvInputCanvas.ReleaseMouseCapture();
    }

    private void ApplySvPoint(Point p)
    {
        _saturation = (float)Math.Clamp(p.X / SvInputCanvas.ActualWidth,  0, 1);
        _value      = (float)Math.Clamp(1 - p.Y / SvInputCanvas.ActualHeight, 0, 1);
        SyncAll();
    }

    // ── Hue mouse ─────────────────────────────────────────────────────

    private void HueInput_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = true;
        HueInputCanvas.CaptureMouse();
        ApplyHuePoint(e.GetPosition(HueInputCanvas));
    }

    private void HueInput_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingHue) return;
        ApplyHuePoint(e.GetPosition(HueInputCanvas));
    }

    private void HueInput_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _draggingHue = false;
        HueInputCanvas.ReleaseMouseCapture();
    }

    private void ApplyHuePoint(Point p)
    {
        _hue = (float)Math.Clamp(p.Y / HueInputCanvas.ActualHeight * 360, 0, 359.99f);
        SyncAll();
    }

    // ── Text input handlers ───────────────────────────────────────────

    private void HsvInput_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingInputs) return;
        if (float.TryParse(HInput.Text, out float h) &&
            float.TryParse(SInput.Text, out float s) &&
            float.TryParse(VInput.Text, out float v))
        {
            _hue        = Math.Clamp(h, 0f, 360f);
            _saturation = Math.Clamp(s / 100f, 0f, 1f);
            _value      = Math.Clamp(v / 100f, 0f, 1f);
            SyncAll(skipHsv: true);
        }
    }

    private void RgbInput_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingInputs) return;
        if (byte.TryParse(RInput.Text, out byte r) &&
            byte.TryParse(GInput.Text, out byte g) &&
            byte.TryParse(BInput.Text, out byte b))
        {
            var (h, s, v) = RgbToHsv(Color.FromRgb(r, g, b));
            _hue = h; _saturation = s; _value = v;
            SyncAll(skipRgb: true);
        }
    }

    private void HexInput_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_updatingInputs) return;
        string raw = HexInput.Text.TrimStart('#');
        if (raw.Length == 6)
        {
            try
            {
                byte r = Convert.ToByte(raw[..2], 16);
                byte g = Convert.ToByte(raw[2..4], 16);
                byte b = Convert.ToByte(raw[4..6], 16);
                var (h, s, v) = RgbToHsv(Color.FromRgb(r, g, b));
                _hue = h; _saturation = s; _value = v;
                SyncAll(skipHex: true);
            }
            catch { }
        }
    }

    // ── Sync ──────────────────────────────────────────────────────────

    private void SyncAll(bool skipHsv = false, bool skipRgb = false, bool skipHex = false)
    {
        SelectedColor = HsvToColor(_hue, _saturation, _value);
        NewColorSwatch.Background = new SolidColorBrush(SelectedColor);

        SvCanvas.InvalidateVisual();
        HueCanvas.InvalidateVisual();

        _updatingInputs = true;

        if (!skipHsv)
        {
            HInput.Text = $"{_hue:0}";
            SInput.Text = $"{_saturation * 100:0}";
            VInput.Text = $"{_value * 100:0}";
        }

        if (!skipRgb)
        {
            RInput.Text = SelectedColor.R.ToString();
            GInput.Text = SelectedColor.G.ToString();
            BInput.Text = SelectedColor.B.ToString();
        }

        if (!skipHex)
            HexInput.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";

        _updatingInputs = false;
    }

    // ── Dialog buttons ────────────────────────────────────────────────

    private void OK_Click(object sender, RoutedEventArgs e)     => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Color math ────────────────────────────────────────────────────

    private static Color HsvToColor(float h, float s, float v)
    {
        var (r, g, b) = HsvToSkColor(h, s, v);
        return Color.FromRgb(r, g, b);
    }

    private static (byte r, byte g, byte b) HsvToSkColor(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        float c = v * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float rf, gf, bf;
        if      (h < 60)  { rf = c; gf = x; bf = 0; }
        else if (h < 120) { rf = x; gf = c; bf = 0; }
        else if (h < 180) { rf = 0; gf = c; bf = x; }
        else if (h < 240) { rf = 0; gf = x; bf = c; }
        else if (h < 300) { rf = x; gf = 0; bf = c; }
        else              { rf = c; gf = 0; bf = x; }
        return ((byte)((rf + m) * 255), (byte)((gf + m) * 255), (byte)((bf + m) * 255));
    }

    private static (float h, float s, float v) RgbToHsv(Color c)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float delta = max - min;
        float v = max;
        float s = max < 0.0001f ? 0f : delta / max;
        float h = 0f;
        if (delta > 0.0001f)
        {
            if      (max == r) h = 60f * (((g - b) / delta) % 6f);
            else if (max == g) h = 60f * (((b - r) / delta) + 2f);
            else               h = 60f * (((r - g) / delta) + 4f);
            if (h < 0f) h += 360f;
        }
        return (h, s, v);
    }
}
