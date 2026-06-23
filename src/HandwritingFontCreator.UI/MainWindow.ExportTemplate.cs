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

        ShowToast("PNG exported", ToastKind.Success);
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
        ShowToast("JPG exported", ToastKind.Success);
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
        ShowToast("PDF exported", ToastKind.Success);
    }

    private void ExportSvgButton_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
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
        ShowToast("SVG exported", ToastKind.Success);
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

    private void RefreshTemplateComboBox(string selectName)
    {
        TemplateComboBox.SelectionChanged -= TemplateComboBox_SelectionChanged;
        TemplateComboBox.Items.Clear();

        var builtIn = new[] { "Default", "School", "Fancy", "Messy" };
        IEnumerable<string> onDisk = Directory.Exists(TemplatesPath)
            ? Directory.GetDirectories(TemplatesPath)
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
        FontPreviewCanvas?.InvalidateVisual();
        UpdateAlphabetProgress();
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
            ShowToast("Nothing to export — draw some letters first", ToastKind.Warning);
            return;
        }

        var files = Directory.GetFiles(folder, "*.json");
        if (files.Length == 0)
        {
            ShowToast("This template has no characters yet", ToastKind.Warning);
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
            ShowToast($"Template exported — {chars.Count} characters", ToastKind.Success);
        }
    }

    private void ImportTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Import font or template",
            Filter = "Font or template (*.ttf;*.otf;*.nwt)|*.ttf;*.otf;*.nwt"
                   + "|TrueType font (*.ttf;*.otf)|*.ttf;*.otf"
                   + "|Nottwrite Template (*.nwt)|*.nwt|JSON (*.json)|*.json"
        };

        if (dlg.ShowDialog() != true) return;

        string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
        if (ext is ".ttf" or ".otf")
        {
            ImportTtfAsTemplate(dlg.FileName);
            return;
        }

        TemplateBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<TemplateBundle>(File.ReadAllText(dlg.FileName));
        }
        catch
        {
            ShowToast("Invalid template file", ToastKind.Error);
            return;
        }

        if (bundle == null || bundle.Characters.Count == 0)
        {
            ShowToast("File is empty or corrupted", ToastKind.Error);
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

        string targetFolder = System.IO.Path.Combine(TemplatesPath, name);
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
        ShowToast($"Imported '{name}' — {bundle.Characters.Count} characters", ToastKind.Success);
    }

}
