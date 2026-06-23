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

public partial class MainWindow
{
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
}
