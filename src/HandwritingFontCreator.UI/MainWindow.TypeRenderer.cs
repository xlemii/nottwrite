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
        var layout    = GetLayout(canvasW);
        var (_, paperY, _, paperH) = GetPaperBounds(canvasW, layout);
        double minH   = TypeScrollViewer.ActualHeight > 0 ? TypeScrollViewer.ActualHeight : 800;
        TypeCanvasHost.Height = Math.Max(minH, paperY + paperH + PaperPadTop);
        TypeSkiaCanvas.InvalidateVisual();
        UpdateTypeStats();
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
        // system fonts have internal metrics spacing; template fonts need extra gap for stroke separation
        double gap     = (_typeFontName != null ? 0 : targetH * 0.06) + _letterSpacing;
        double spaceW  = targetH * 0.38 + _wordSpacing;
        float  paperX  = Math.Max(PaperPadSide, ((float)canvasW - _paperW) / 2f);
        double margin  = PaperInnerH;
        double startX  = paperX + margin;
        double endX    = paperX + _paperW - margin;
        double lineW   = endX - startX;
        double ox = startX, oy = 0;
        int lineStart  = 0; // index in result where current line started

        void FinishLine(int lineEnd, bool isHardBreak)
        {
            if (_textAlign == TextAlign.Left) return;
            // measure actual width of this line
            double lineUsed = 0;
            for (int k = lineStart; k < lineEnd; k++)
                if (result[k].Cd.Ch != '\n')
                    lineUsed = Math.Max(lineUsed, result[k].X + result[k].W - startX);
            double shift = _textAlign switch
            {
                TextAlign.Center  => (lineW - lineUsed) / 2,
                TextAlign.Right   => lineW - lineUsed,
                TextAlign.Justify when !isHardBreak => 0, // TODO: justify spaces
                _ => 0
            };
            if (shift <= 0) return;
            for (int k = lineStart; k < lineEnd; k++)
                result[k] = result[k] with { X = result[k].X + shift };
        }

        int i = 0;
        while (i < _typeChars.Count)
        {
            var cd = _typeChars[i];
            if (cd.Ch == '\n')
            {
                FinishLine(result.Count, true);
                result.Add(new(cd, ox, oy, 0));
                i++; ox = startX; oy += lineH;
                lineStart = result.Count;
                continue;
            }
            if (cd.Ch == ' ')  { result.Add(new(cd, ox, oy, spaceW)); i++; ox += spaceW; continue; }

            int wEnd = i; double wW = 0;
            while (wEnd < _typeChars.Count && _typeChars[wEnd].Ch != ' ' && _typeChars[wEnd].Ch != '\n')
            { wW += MeasureCharW(_typeChars[wEnd], targetH) + gap; wEnd++; }
            if (wW > gap) wW -= gap;
            if (ox > startX && ox + wW > endX)
            {
                FinishLine(result.Count, false);
                ox = startX; oy += lineH;
                lineStart = result.Count;
            }

            for (int j = i; j < wEnd; j++)
            {
                double cw = MeasureCharW(_typeChars[j], targetH);
                result.Add(new(_typeChars[j], ox, oy, cw));
                ox += cw + gap;
            }
            i = wEnd;
        }
        FinishLine(result.Count, true);
        return result;
    }

    // ── Layout cache helpers ──────────────────────────────────────

    private void InvalidateLayout()
    {
        _layoutVersion++;
        UpdateTypeStats();
        if (_currentNoteId != null)
        {
            if (_dirtyTabs.Add(_currentNoteId))
                RefreshTabBar();
            _tabSnapshots[_currentNoteId] = _typeChars.Select(ToSerial).ToList();
            ScheduleNoteSave();
        }
    }

    // Live word / character count for the current note (Type toolbar)
    private void UpdateTypeStats()
    {
        if (TypeStatsLabel == null) return;
        int chars = _typeChars.Count;
        int words = 0;
        bool inWord = false;
        foreach (var cd in _typeChars)
        {
            bool ws = cd.Ch == ' ' || cd.Ch == '\n' || cd.Ch == '\t';
            if (!ws && !inWord) { words++; inWord = true; }
            else if (ws) inWord = false;
        }
        TypeStatsLabel.Text = chars == 0
            ? ""
            : $"{words} {(words == 1 ? "word" : "words")} · {chars} {(chars == 1 ? "char" : "chars")}";
    }

    // Returns cached layout if version + canvasW match, else recomputes.
    private List<CharLayout> GetLayout(double canvasW)
    {
        if (_cachedLayoutVersion == _layoutVersion && Math.Abs(_cachedLayoutCanvasW - canvasW) < 0.5)
            return _cachedLayout;
        _cachedLayout        = LayoutTypeChars(canvasW);
        _cachedLayoutCanvasW = canvasW;
        _cachedLayoutVersion = _layoutVersion;
        BuildCursorXs(canvasW);
        return _cachedLayout;
    }

    // Precomputes cursor X for every position 0..layout.Count in a single O(N) pass.
    private void BuildCursorXs(double canvasW)
    {
        var layout = _cachedLayout;
        if (_cachedCursorXs.Length != layout.Count + 1)
            _cachedCursorXs = new float[layout.Count + 1];
        float startX = Math.Max(PaperPadSide, ((float)canvasW - _paperW) / 2f) + PaperInnerH;
        float cx = startX; double prevY = -1;
        _cachedCursorXs[0] = startX;
        for (int i = 0; i < layout.Count; i++)
        {
            var l = layout[i];
            if (l.Cd.Ch == '\n') { cx = startX; prevY = l.Y; }
            else
            {
                if (prevY >= 0 && l.Y > prevY + 0.5) cx = startX;
                cx = Math.Max(cx + 2f, (float)(l.X + l.W));
                prevY = l.Y;
            }
            _cachedCursorXs[i + 1] = cx;
        }
    }

    private double MeasureCharW(CharData cd, double targetH)
    {
        if (cd.Ch == ' ' || cd.Ch == '\n') return 0;

        if (_typeFontName != null)
        {
            _sysFontPaint.Typeface = GetSystemTypeface(cd.Bold, cd.Italic);
            _sysFontPaint.TextSize = (float)targetH * 0.75f;
            var bounds = SKRect.Empty;
            float advance = _sysFontPaint.MeasureText(cd.Ch.ToString(), ref bounds);
            // use max of advance and visual right edge — display/script fonts can overhang
            return Math.Max(advance, bounds.Right);
        }

        var variants = GetAllVariantPaths(cd.Ch);
        if (variants.Count == 0) return MeasureFallback(cd, targetH);
        int vi = Math.Clamp(cd.VariantIdx, 0, variants.Count - 1);
        var sd = GetStrokeDataCached(variants[vi]);
        return sd != null && sd.Height > 0 ? sd.Width * targetH / sd.Height : MeasureFallback(cd, targetH);
    }

    private void TypeSkia_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        if (!_resourcesReady) return;
        var skCanvas = e.Surface.Canvas;
        float fw = e.Info.Width, fh = e.Info.Height;

        // desk background (darker than paper)
        skCanvas.Clear(new SKColor(0x0D, 0x0D, 0x12));

        var layout = GetLayout(fw);
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

        // selection highlight — one rounded rect per line, using cached cursor X array
        if (HasSelection && layout.Count > 0)
        {
            int selFrom = SelFrom, selTo = SelTo;
            using var selP = new SKPaint { Color = new SKColor(0x58, 0xA6, 0xFF, 65), IsAntialias = true };
            float highlightH  = (float)(_fontSize * 1.05);
            float highlightOY = (float)(-_fontSize * 0.83);
            var cxs = _cachedCursorXs;

            // scan selection, emit one rect per line (track span per line key)
            int    lineKey = int.MinValue;
            float  lx1 = 0, lx2 = 0, lly = 0;

            void FlushLine()
            {
                if (lineKey == int.MinValue) return;
                float hy = (float)(baseY + lly + highlightOY);
                var rr = new SKRoundRect(new SKRect(lx1 - 1, hy, lx2 + 1, hy + highlightH), 3, 3);
                skCanvas.DrawRoundRect(rr, selP);
            }

            for (int si = selFrom; si < selTo && si < layout.Count; si++)
            {
                var l = layout[si];
                if (l.Cd.Ch == '\n') continue;
                int lk = (int)Math.Round(l.Y);
                float x1 = si     < cxs.Length ? cxs[si]     : (float)l.X;
                float x2 = si + 1 < cxs.Length ? cxs[si + 1] : x1 + 8f;
                if (lk != lineKey)
                {
                    FlushLine();
                    lineKey = lk; lx1 = x1; lx2 = x2; lly = (float)l.Y;
                }
                else
                {
                    if (x1 < lx1) lx1 = x1;
                    if (x2 > lx2) lx2 = x2;
                }
            }
            FlushLine();
        }

        foreach (var item in layout)
        {
            if (item.Cd.Ch == ' ' || item.Cd.Ch == '\n') continue;
            RenderTypeCharSkia(skCanvas, item.Cd, item.X, baseY + item.Y, scaledSW);
        }

        // underline / strikethrough decorations
        using var decoP = new SKPaint { StrokeWidth = Math.Max(1f, (float)(scaledSW * 0.45)), IsAntialias = true };
        foreach (var item in layout)
        {
            if (item.Cd.Ch == ' ' || item.Cd.Ch == '\n') continue;
            if (!item.Cd.Underline && !item.Cd.Strikethrough) continue;
            var col = new SKColor(item.Cd.Color.R, item.Cd.Color.G, item.Cd.Color.B, item.Cd.Color.A);
            decoP.Color = col;
            float x1 = (float)item.X, x2 = (float)(item.X + item.W);
            float charBottom = (float)(baseY + item.Y + _fontSize * 0.15);
            float charMid    = (float)(baseY + item.Y - _fontSize * 0.28);
            if (item.Cd.Underline)
                skCanvas.DrawLine(x1, charBottom, x2, charBottom, decoP);
            if (item.Cd.Strikethrough)
                skCanvas.DrawLine(x1, charMid, x2, charMid, decoP);
        }

        // word / char count — single pass, no string allocation
        if (_typeChars.Count > 0)
        {
            int wordCount = 0, charCount = 0; bool inWord = false;
            foreach (var cd in _typeChars)
            {
                if (cd.Ch == ' ' || cd.Ch == '\n') { if (inWord) { wordCount++; inWord = false; } }
                else { inWord = true; charCount++; }
            }
            if (inWord) wordCount++;
            using var cntP = new SKPaint
            {
                Color       = new SKColor(0x88, 0x88, 0x88, 0xCC),
                TextSize    = 11,
                IsAntialias = true,
                TextAlign   = SKTextAlign.Left
            };
            string label = $"Words: {wordCount}   Chars: {charCount}";
            skCanvas.DrawText(label, (float)paperX + 6, (float)(paperY + paperH + 18), cntP);
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

    // Cached typefaces for system font — rebuilt only when _typeFontName changes
    private string?     _cachedTfName;
    private SKTypeface? _cachedTfRegular, _cachedTfBold, _cachedTfItalic, _cachedTfBoldItalic;

    private void EnsureTypefaceCache()
    {
        if (_typeFontName == _cachedTfName) return;
        _cachedTfRegular?.Dispose();   _cachedTfRegular   = null;
        _cachedTfBold?.Dispose();      _cachedTfBold      = null;
        _cachedTfItalic?.Dispose();    _cachedTfItalic    = null;
        _cachedTfBoldItalic?.Dispose();_cachedTfBoldItalic= null;
        _cachedTfName      = _typeFontName;
        _cachedTfRegular   = SKTypeface.FromFamilyName(_typeFontName, SKFontStyle.Normal)     ?? SKTypeface.Default;
        _cachedTfBold      = SKTypeface.FromFamilyName(_typeFontName, SKFontStyle.Bold)       ?? _cachedTfRegular;
        _cachedTfItalic    = SKTypeface.FromFamilyName(_typeFontName, SKFontStyle.Italic)     ?? _cachedTfRegular;
        _cachedTfBoldItalic= SKTypeface.FromFamilyName(_typeFontName, SKFontStyle.BoldItalic) ?? _cachedTfBold;
    }

    private SKTypeface GetSystemTypeface(bool bold, bool italic)
    {
        EnsureTypefaceCache();
        return bold && italic ? _cachedTfBoldItalic!
             : bold           ? _cachedTfBold!
             : italic         ? _cachedTfItalic!
             : _cachedTfRegular!;
    }

    // Reuse one SKPaint for system font rendering (updated per-char, never recreated)
    private readonly SKPaint _sysFontPaint = new() { IsAntialias = true };

    // Fallback typeface for characters a handwriting template doesn't have a glyph for.
    private SKTypeface? _fbReg, _fbBold, _fbItalic, _fbBoldItalic;
    private SKTypeface FallbackTypeface(bool bold, bool italic)
    {
        _fbReg ??= SKTypeface.FromFamilyName(null, SKFontStyle.Normal) ?? SKTypeface.Default;
        if (bold && italic) return _fbBoldItalic ??= SKTypeface.FromFamilyName(null, SKFontStyle.BoldItalic) ?? _fbReg;
        if (bold)           return _fbBold       ??= SKTypeface.FromFamilyName(null, SKFontStyle.Bold)       ?? _fbReg;
        if (italic)         return _fbItalic     ??= SKTypeface.FromFamilyName(null, SKFontStyle.Italic)     ?? _fbReg;
        return _fbReg;
    }

    private void DrawFallbackChar(SKCanvas canvas, CharData cd, double x, double baselineY)
    {
        _sysFontPaint.Typeface = FallbackTypeface(cd.Bold, cd.Italic);
        _sysFontPaint.TextSize = (float)_fontSize * 0.75f;
        _sysFontPaint.Color    = new SKColor(cd.Color.R, cd.Color.G, cd.Color.B, cd.Color.A);
        canvas.DrawText(cd.Ch.ToString(), (float)x, (float)baselineY, _sysFontPaint);
    }

    private double MeasureFallback(CharData cd, double targetH)
    {
        _sysFontPaint.Typeface = FallbackTypeface(cd.Bold, cd.Italic);
        _sysFontPaint.TextSize = (float)targetH * 0.75f;
        return _sysFontPaint.MeasureText(cd.Ch.ToString());
    }

    private void RenderTypeCharSkia(SKCanvas canvas, CharData cd, double x, double baselineY, double scaledSW)
    {
        if (_typeFontName != null)
        {
            _sysFontPaint.Typeface = GetSystemTypeface(cd.Bold, cd.Italic);
            _sysFontPaint.TextSize = (float)_fontSize * 0.75f;
            _sysFontPaint.Color    = new SKColor(cd.Color.R, cd.Color.G, cd.Color.B, cd.Color.A);
            canvas.DrawText(cd.Ch.ToString(), (float)x, (float)baselineY, _sysFontPaint);
            return;
        }

        var variants = GetAllVariantPaths(cd.Ch);
        if (variants.Count == 0) { DrawFallbackChar(canvas, cd, x, baselineY); return; }
        int vi     = Math.Clamp(cd.VariantIdx, 0, variants.Count - 1);
        var letter = GetStrokeDataCached(variants[vi]);
        if (letter == null) { DrawFallbackChar(canvas, cd, x, baselineY); return; }

        double scale  = letter.Height > 0 ? _fontSize / letter.Height : 1;
        double charW  = letter.Width * scale;
        double baseOff = baselineY - letter.Baseline * scale;
        float  maxW   = (float)((cd.Bold ? scaledSW * 1.6 : scaledSW) + _taperAmount);
        double rotRad = cd.RotDeg * Math.PI / 180.0;
        double cosR   = Math.Cos(rotRad), sinR = Math.Sin(rotRad);
        double pivotX = x + charW / 2, pivotY = baselineY;
        var    col    = new SKColor(cd.Color.R, cd.Color.G, cd.Color.B, cd.Color.A);

        bool rotate = cd.RotDeg != 0;
        bool italic = cd.Italic;
        foreach (var s in letter.Strokes)
        {
            _pointsBuffer.Clear();
            _pressureBuffer.Clear();
            foreach (var p in s.Points)
            {
                double px = x + p.X * scale;
                double py = baseOff + p.Y * scale + cd.JitterY;
                if (italic) px += (baselineY - py) * 0.28;
                if (rotate)
                {
                    double rx = px - pivotX, ry = py - pivotY;
                    px = pivotX + rx * cosR - ry * sinR;
                    py = pivotY + rx * sinR + ry * cosR;
                }
                _pointsBuffer.Add(new Point(px, py));
                _pressureBuffer.Add(p.Pressure);
            }
            RenderGridStroke(canvas, _pointsBuffer, col, maxW, _pressureBuffer);
        }
    }

    // Returns cursor X for position `cursor`. Uses the precomputed cache when available (O(1)).
    private float ComputeCursorX(List<CharLayout> layout, int cursor, float startX)
    {
        if (cursor >= 0 && cursor < _cachedCursorXs.Length) return _cachedCursorXs[cursor];
        // fallback: O(N) manual computation (cache miss — rare)
        if (cursor == 0 || layout.Count == 0) return startX;
        float x = startX; double prevY = -1;
        int count = Math.Min(cursor, layout.Count);
        for (int i = 0; i < count; i++)
        {
            var l = layout[i];
            if (l.Cd.Ch == '\n') { x = startX; prevY = l.Y; continue; }
            if (prevY >= 0 && l.Y > prevY + 0.5) x = startX;
            x = Math.Max(x + 2f, (float)(l.X + l.W));
            prevY = l.Y;
        }
        return x;
    }

    private void DrawCursorSkia(SKCanvas canvas, List<CharLayout> layout, double baseY)
    {
        float  startX = Math.Max(PaperPadSide, (canvas.LocalClipBounds.Width - _paperW) / 2f) + PaperInnerH;
        Point cursorPt;

        if (layout.Count == 0)
            cursorPt = new(startX, baseY);
        else if (_typeCursor == 0)
            cursorPt = new(layout[0].X, baseY + layout[0].Y);
        else if (_typeCursor >= layout.Count)
        {
            var last = layout[^1];
            double cursorY = last.Cd.Ch == '\n'
                ? last.Y + _fontSize * _lineHeightMult
                : last.Y;
            float cursorX = last.Cd.Ch == '\n' ? startX : ComputeCursorX(layout, _typeCursor, startX);
            cursorPt = new(cursorX, baseY + cursorY);
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

    private bool _mouseSelectDragging = false;

    // O(N) — uses precomputed _cachedCursorXs instead of calling ComputeCursorX per char.
    private int HitTestLayout(System.Windows.Point pos, List<CharLayout> layout, double baseY)
    {
        if (layout.Count == 0) return 0;
        int best = _typeChars.Count; double bestD = double.MaxValue;
        var cxs = _cachedCursorXs;
        for (int i = 0; i < layout.Count; i++)
        {
            if (layout[i].Cd.Ch == '\n') continue;
            double cy = baseY + layout[i].Y;
            float leftX  = i     < cxs.Length ? cxs[i]     : (float)layout[i].X;
            float rightX = i + 1 < cxs.Length ? cxs[i + 1] : leftX + 8f;
            double d1 = Math.Abs(pos.X - leftX)  + Math.Abs(pos.Y - cy) * 0.4;
            double d2 = Math.Abs(pos.X - rightX) + Math.Abs(pos.Y - cy) * 0.4;
            if (d1 < bestD) { bestD = d1; best = i; }
            if (d2 < bestD) { bestD = d2; best = i + 1; }
        }
        return Math.Clamp(best, 0, _typeChars.Count);
    }

    private void TypeCanvas_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        float cw   = (float)(TypeSkiaCanvas.ActualWidth > 0 ? TypeSkiaCanvas.ActualWidth : 1200);
        var layout = GetLayout(cw);
        var (_, paperY, _, _) = GetPaperBounds(cw, layout);
        double baseY = paperY + PaperInnerV + _fontSize * (_lineHeightMult * 0.75);
        var pos = e.GetPosition(DisplayCanvas);
        int hit = HitTestLayout(pos, layout, baseY);

        bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
        if (shift)
        {
            if (!HasSelection) _selAnchor = _typeCursor;
            _typeCursor = hit;
            if (_selAnchor == _typeCursor) ClearSel();
        }
        else
        {
            ClearSel();
            _typeCursor = hit;
            _selAnchor  = hit; // start drag anchor
            _mouseSelectDragging = true;
            DisplayCanvas.CaptureMouse();
        }
        ResetCursorBlink(); RefreshGeneratedText();
        System.Windows.Input.Keyboard.Focus(this);
    }

    private void TypeCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_mouseSelectDragging) return;
        float cw   = (float)(TypeSkiaCanvas.ActualWidth > 0 ? TypeSkiaCanvas.ActualWidth : 1200);
        var layout = GetLayout(cw);
        var (_, paperY, _, _) = GetPaperBounds(cw, layout);
        double baseY = paperY + PaperInnerV + _fontSize * (_lineHeightMult * 0.75);
        int hit = HitTestLayout(e.GetPosition(DisplayCanvas), layout, baseY);
        _typeCursor = hit;
        if (_selAnchor == _typeCursor) { } // same pos = no visible selection (cursor visible)
        ResetCursorBlink(); RefreshGeneratedText();
    }

    private void TypeCanvas_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_mouseSelectDragging) return;
        _mouseSelectDragging = false;
        DisplayCanvas.ReleaseMouseCapture();
        if (_selAnchor == _typeCursor) ClearSel(); // click without drag = no selection
        RefreshGeneratedText();
    }

}
