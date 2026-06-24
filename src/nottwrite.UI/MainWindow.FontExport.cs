using System.IO;
using System.Windows;
using nottwrite.Core;
using nottwrite.Core.Models;
using SkiaSharp;

namespace nottwrite.UI;

public partial class MainWindow
{
    // Font design metrics
    private const int FontUpm        = 1000;  // units per em
    private const int FontCapUnits   = 720;   // glyph height in em units
    private const int FontAscender   = 800;
    private const int FontDescender  = -200;
    private const int FontSideBearing = 60;   // left/right margin per glyph

    private const string FontPunct  = ".,!?():-;'@#&";
    private const string FontPolish = "ĄĘÓŚŹŻĆŃŁąęóśźżćńł";

    private void ExportFontButton_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        FontRangeOverlay.Visibility = Visibility.Visible;
    }

    private void FontRangeCancel_Click(object sender, RoutedEventArgs e)
        => FontRangeOverlay.Visibility = Visibility.Collapsed;

    private void FontRangeExport_Click(object sender, RoutedEventArgs e)
    {
        FontRangeOverlay.Visibility = Visibility.Collapsed;

        bool up = RangeUpper.IsChecked == true, lo = RangeLower.IsChecked == true,
             di = RangeDigits.IsChecked == true, pu = RangePunct.IsChecked == true,
             pl = RangePolish.IsChecked == true;

        bool Include(char c) =>
            (pl && FontPolish.Contains(c)) ||
            (up && char.IsUpper(c) && c < 128) ||
            (lo && char.IsLower(c) && c < 128) ||
            (di && char.IsDigit(c)) ||
            (pu && FontPunct.Contains(c));

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Export font",
            FileName   = $"{SanitizeFontName(CurrentTemplate)}.ttf",
            DefaultExt = ".ttf",
            Filter     = "TrueType Font (*.ttf)|*.ttf",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            int glyphs = BuildAndSaveFont(dlg.FileName, Include);
            if (glyphs == 0)
            {
                ShowToast("No drawn characters in the selected range", ToastKind.Warning);
                return;
            }
            ShowToast($"Font exported — {glyphs} glyphs", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast("Font export failed: " + ex.Message, ToastKind.Error);
        }
    }

    private int BuildAndSaveFont(string path, Func<char, bool>? include = null)
    {
        string family = string.IsNullOrWhiteSpace(CurrentTemplate) ? "Nottwrite" : CurrentTemplate;
        var builder = new TrueTypeFontBuilder(family, "Regular",
            FontUpm, FontAscender, FontDescender);

        // always include a space glyph
        builder.AddGlyph(' ', new TrueTypeFontBuilder.Glyph { AdvanceWidth = FontUpm / 3 });

        int built = 0;
        foreach (char ch in AllChars.Distinct())
        {
            if (ch == ' ') continue;
            if (include != null && !include(ch)) continue;
            var variants = GetAllVariantPaths(ch);
            if (variants.Count == 0) continue;
            var sd = GetStrokeDataCached(variants[0]);
            if (sd == null || sd.Strokes.Count == 0 || sd.Height < 1) continue;

            var glyph = BuildGlyph(sd);
            if (glyph.Contours.Count == 0) continue;

            builder.AddGlyph(ch, glyph);
            built++;
        }

        if (built == 0) return 0;

        File.WriteAllBytes(path, builder.Build());
        return built;
    }

    /// Convert one StrokeData into a font glyph: stroke each centreline into a
    /// filled outline, union them, then flatten to integer contours in em units.
    private TrueTypeFontBuilder.Glyph BuildGlyph(StrokeData sd)
    {
        double penW = Math.Max(1.0, _genStrokeWidth * sd.Height / 180.0);

        using var raw = new SKPath { FillType = SKPathFillType.Winding };
        foreach (var stroke in sd.Strokes)
        {
            if (stroke.Points.Count == 0) continue;
            AddStrokeOutline(raw, stroke.Points, penW);
        }

        using var solid = new SKPath();
        if (!raw.Simplify(solid) || solid.IsEmpty)
            solid.AddPath(raw);   // fall back to raw if boolean op fails

        double scale = (double)FontCapUnits / sd.Height;
        var glyph = new TrueTypeFontBuilder.Glyph
        {
            AdvanceWidth = (int)Math.Round(sd.Width * scale) + FontSideBearing * 2,
        };

        // flatten contours, scale to em, flip Y (baseline at 0, glyph above)
        foreach (var contour in FlattenContours(solid))
        {
            var pts = new List<(int X, int Y)>(contour.Count);
            int lastX = int.MinValue, lastY = int.MinValue;
            foreach (var p in contour)
            {
                int fx = (int)Math.Round(p.X * scale) + FontSideBearing;
                int fy = (int)Math.Round((sd.Height - p.Y) * scale);
                if (fx == lastX && fy == lastY) continue;   // drop dupes
                pts.Add((fx, fy));
                lastX = fx; lastY = fy;
            }
            if (pts.Count >= 3) glyph.Contours.Add(pts);
        }
        return glyph;
    }

    /// Build a closed filled outline of one centreline stroke; per-point width
    /// follows pen pressure so the exported font reflects how it was drawn.
    private static void AddStrokeOutline(SKPath path, List<PointData> raw, double penW)
    {
        // dedupe nearby points, carrying pressure
        var pts = new List<SKPoint>(raw.Count);
        var prs = new List<float>(raw.Count);
        foreach (var p in raw)
        {
            var sp = new SKPoint((float)p.X, (float)p.Y);
            if (pts.Count == 0 || SKPoint.Distance(pts[^1], sp) > 0.5f)
            {
                pts.Add(sp);
                prs.Add((float)(p.Pressure <= 0 ? 1.0 : p.Pressure));
            }
        }
        int n = pts.Count;
        if (n == 0) return;

        float baseHw = (float)(penW * 0.5);
        if (n == 1)
        {
            path.AddCircle(pts[0].X, pts[0].Y, baseHw * prs[0]);
            return;
        }

        var left = new SKPoint[n];
        var right = new SKPoint[n];
        for (int i = 0; i < n; i++)
        {
            var prev = pts[Math.Max(0, i - 1)];
            var next = pts[Math.Min(n - 1, i + 1)];
            float dx = next.X - prev.X, dy = next.Y - prev.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            float nx = len > 0.001f ? -dy / len : 0f;
            float ny = len > 0.001f ?  dx / len : 0f;
            float hw = baseHw * prs[i];
            left[i]  = new SKPoint(pts[i].X + nx * hw, pts[i].Y + ny * hw);
            right[i] = new SKPoint(pts[i].X - nx * hw, pts[i].Y - ny * hw);
        }

        path.MoveTo(left[0]);
        for (int i = 1; i < n; i++) path.LineTo(left[i]);
        // round cap at the end
        path.LineTo(right[n - 1]);
        for (int i = n - 2; i >= 0; i--) path.LineTo(right[i]);
        path.Close();
    }

    /// Flatten an SKPath into polyline contours (curves subdivided).
    private static List<List<SKPoint>> FlattenContours(SKPath path)
    {
        var result = new List<List<SKPoint>>();
        List<SKPoint>? cur = null;
        var pts = new SKPoint[4];

        using var it = path.CreateRawIterator();
        SKPathVerb verb;
        SKPoint start = default, last = default;
        while ((verb = it.Next(pts)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    cur = new List<SKPoint> { pts[0] };
                    result.Add(cur);
                    start = last = pts[0];
                    break;
                case SKPathVerb.Line:
                    cur?.Add(pts[1]); last = pts[1];
                    break;
                case SKPathVerb.Quad:
                    Subdivide(cur, last, pts[1], pts[2]); last = pts[2];
                    break;
                case SKPathVerb.Conic:
                    Subdivide(cur, last, pts[1], pts[2]); last = pts[2];
                    break;
                case SKPathVerb.Cubic:
                    SubdivideCubic(cur, last, pts[1], pts[2], pts[3]); last = pts[3];
                    break;
                case SKPathVerb.Close:
                    last = start;
                    break;
            }
        }
        return result;
    }

    private static void Subdivide(List<SKPoint>? c, SKPoint p0, SKPoint p1, SKPoint p2)
    {
        if (c == null) return;
        const int steps = 6;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1 - t;
            c.Add(new SKPoint(
                u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
                u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y));
        }
    }

    private static void SubdivideCubic(List<SKPoint>? c, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3)
    {
        if (c == null) return;
        const int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1 - t;
            float a = u * u * u, b = 3 * u * u * t, cc = 3 * u * t * t, d = t * t * t;
            c.Add(new SKPoint(
                a * p0.X + b * p1.X + cc * p2.X + d * p3.X,
                a * p0.Y + b * p1.Y + cc * p2.Y + d * p3.Y));
        }
    }

    private static string SanitizeFontName(string s)
    {
        var clean = new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ').ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "Nottwrite" : clean;
    }

    // ───────────────── TTF import → editable template ─────────────

    private void ImportTtfAsTemplate(string ttfPath)
    {
        SKTypeface? tf = null;
        try { tf = SKTypeface.FromFile(ttfPath); } catch { }
        if (tf == null) { ShowToast("Could not read font file", ToastKind.Error); return; }

        string name = UniqueTemplateName(SanitizeFontName(Path.GetFileNameWithoutExtension(ttfPath)));
        string folder = Path.Combine(TemplatesPath, name);
        Directory.CreateDirectory(folder);

        const float designSize = 200f;
        const double pad = 12;
        using var font = new SKFont(tf, designSize);
        int built = 0;

        foreach (char ch in AllChars.Distinct())
        {
            ushort gid = tf.GetGlyph(ch);
            if (gid == 0) continue;
            using var path = font.GetGlyphPath(gid);
            if (path == null || path.IsEmpty) continue;

            var contours = FlattenContours(path);   // font space, Y-down baseline at 0
            if (contours.Count == 0) continue;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var c in contours)
                foreach (var p in c)
                {
                    minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
                }
            if (maxX - minX < 0.5 && maxY - minY < 0.5) continue;

            var sd = new StrokeData
            {
                Width    = (maxX - minX) + pad * 2,
                Height   = (maxY - minY) + pad * 2,
                Baseline = (maxY - minY) + pad,
            };
            foreach (var c in contours)
            {
                var stroke = new Stroke();
                foreach (var p in c)
                    stroke.Points.Add(new PointData { X = p.X - minX + pad, Y = p.Y - minY + pad });
                if (stroke.Points.Count >= 2) sd.Strokes.Add(stroke);
            }
            if (sd.Strokes.Count == 0) continue;

            File.WriteAllText(Path.Combine(folder, GlyphFileName(ch)),
                System.Text.Json.JsonSerializer.Serialize(sd,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            built++;
        }
        tf.Dispose();

        if (built == 0)
        {
            ShowToast("No usable glyphs found in that font", ToastKind.Warning);
            try { Directory.Delete(folder, true); } catch { }
            return;
        }

        _charCache.Clear();
        _strokeDataCache.Clear();
        RefreshTemplateComboBox(name);
        ShowToast($"Imported '{name}' — {built} glyphs, now editable", ToastKind.Success);
    }

    private static string GlyphFileName(char ch)
    {
        string prefix = char.IsDigit(ch) ? "digit"
                      : char.IsUpper(ch) ? "upper"
                      : char.IsLower(ch) ? "lower"
                      : "special";
        return $"{prefix}_{CharSafeName(ch)}.json";
    }

    private string UniqueTemplateName(string baseName)
    {
        string name = baseName;
        int i = 2;
        while (Directory.Exists(Path.Combine(TemplatesPath, name)))
            name = $"{baseName}_{i++}";
        return name;
    }
}
