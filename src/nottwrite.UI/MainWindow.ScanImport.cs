using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using nottwrite.Core.Models;
using SkiaSharp;

namespace nottwrite.UI;

public partial class MainWindow
{
    // ── Printable template sheet + scan import ────────────────────────────
    // Shared grid geometry (A4 @ 300 DPI portrait). The Python importer detects
    // the three corner fiducials to recover this grid from a photo, so exact px
    // need not match — but COLS and the character order must.
    private const int SheetW = 2480, SheetH = 3508;
    private const int SheetCols = 7;
    private const int SheetFiducial = 70;

    private static readonly string ScanScriptPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "scan_import.py");

    private void ExportTemplateSheet_Click(object sender, RoutedEventArgs e)
    {
        var chars = AllChars;
        if (chars.Length == 0) { ShowToast("No characters in the set", ToastKind.Warning); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Save template sheet",
            FileName   = $"{SanitizeFontName(CurrentTemplate)}-template.png",
            DefaultExt = ".png",
            Filter     = "PNG image (*.png)|*.png",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            RenderTemplateSheet(chars, dlg.FileName);
            ShowToast("Template sheet saved — print it, write each letter, then Scan", ToastKind.Success);
        }
        catch (Exception ex) { ShowToast("Sheet export failed: " + ex.Message, ToastKind.Error); }
    }

    private void RenderTemplateSheet(string chars, string path)
    {
        int margin = 170;
        int gridW  = SheetW - 2 * margin;
        int cellW  = gridW / SheetCols;
        int cellH  = 360;
        int gridLeft = (SheetW - cellW * SheetCols) / 2;
        int gridTop  = 360;
        int rows = (chars.Length + SheetCols - 1) / SheetCols;

        using var bmp = new SKBitmap(SheetW, SheetH);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.White);

        using var header = new SKPaint { Color = SKColors.Black, IsAntialias = true, TextSize = 46 };
        c.DrawText("nottwrite — write one letter inside each box, then photograph this sheet",
            margin, 150, header);
        using var sub = new SKPaint { Color = new SKColor(0x66, 0x66, 0x66), IsAntialias = true, TextSize = 30 };
        c.DrawText($"Template: {CurrentTemplate}", margin, 200, sub);

        // Fiducials: filled squares at grid TL, TR, BL (L-shape = orientation).
        using var fid = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        void Fiducial(int x, int y) => c.DrawRect(x, y, SheetFiducial, SheetFiducial, fid);
        int gridRight = gridLeft + cellW * SheetCols;
        int gridBottom = gridTop + cellH * rows;
        Fiducial(gridLeft - SheetFiducial - 12, gridTop - SheetFiducial - 12);
        Fiducial(gridRight + 12,                gridTop - SheetFiducial - 12);
        Fiducial(gridLeft - SheetFiducial - 12, gridBottom + 12);

        using var cell = new SKPaint { Color = new SKColor(0xBB, 0xBB, 0xBB), IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var baseLine = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD), IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = 2,
            PathEffect = SKPathEffect.CreateDash(new[] { 10f, 10f }, 0) };
        using var lbl = new SKPaint { Color = new SKColor(0xCC, 0xCC, 0xCC), IsAntialias = true, TextSize = 54 };

        for (int i = 0; i < chars.Length; i++)
        {
            int col = i % SheetCols, row = i / SheetCols;
            int x = gridLeft + col * cellW, y = gridTop + row * cellH;
            c.DrawRect(x, y, cellW, cellH, cell);
            // dashed baseline at 0.75·h (matches StrokeData.Baseline)
            float by = y + cellH * 0.75f;
            c.DrawLine(x + 12, by, x + cellW - 12, by, baseLine);
            // faint reference label top-left
            c.DrawText(chars[i].ToString(), x + 14, y + 56, lbl);
        }

        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 95);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }

    // ── Import from a photographed sheet (experimental, OpenCV via Python) ──
    private record ScanGlyph(int cp, double w, double h, List<List<double[]>> strokes);
    private record ScanResult(List<ScanGlyph> glyphs);

    private void ImportScan_Click(object sender, RoutedEventArgs e)
    {
        string script = Path.GetFullPath(ScanScriptPath);
        if (!File.Exists(script))
        {
            ShowToast("scan_import.py not found", ToastKind.Error);
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Import from scan / photo",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
        };
        if (dlg.ShowDialog() != true) return;

        string img     = dlg.FileName;
        string chars   = AllChars;
        string jobPath = Path.Combine(Path.GetTempPath(), "nottwrite_scan_job.json");
        string outPath = Path.Combine(Path.GetTempPath(), "nottwrite_scan_out.json");
        try
        {
            File.WriteAllText(jobPath, JsonSerializer.Serialize(new { cols = SheetCols, chars }));
        }
        catch (Exception ex) { ShowToast("Scan setup failed: " + ex.Message, ToastKind.Error); return; }

        ShowBusy("Processing scan…");
        Task.Run(() =>
        {
            string err = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"-u \"{script}\" \"{img}\" \"{jobPath}\" \"{outPath}\"",
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                };
                using var p = Process.Start(psi) ?? throw new Exception("python not started");
                err = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                if (!p.HasExited) { try { p.Kill(); } catch { } throw new Exception("scan timed out"); }
                if (p.ExitCode != 0) throw new Exception(LastLine(err));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Dispatcher.Invoke(() => { HideBusy(); ShowToast(
                    "Scan needs Python + packages: pip install opencv-python scikit-image numpy",
                    ToastKind.Error); });
                return;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => { HideBusy(); ShowToast("Scan failed: " + ex.Message, ToastKind.Error); });
                return;
            }

            int imported = 0;
            try { imported = ApplyScanResult(outPath); }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => { HideBusy(); ShowToast("Scan parse failed: " + ex.Message, ToastKind.Error); });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                HideBusy();
                _charCache.Clear(); _strokeDataCache.Clear(); InvalidateVariantCache();
                LoadAllCharsCache();
                LoadEditActiveChar();
                AlphabetEditCanvas?.InvalidateVisual();
                FontPreviewCanvas?.InvalidateVisual();
                CreateCharacterGrid();
                ShowToast(imported > 0 ? $"Imported {imported} letters from scan"
                                       : "No letters detected — check lighting and fiducial corners",
                          imported > 0 ? ToastKind.Success : ToastKind.Warning);
            });
        });
    }

    // Convert the importer's JSON into per-character StrokeData files.
    private int ApplyScanResult(string outPath)
    {
        var res = JsonSerializer.Deserialize<ScanResult>(File.ReadAllText(outPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (res?.glyphs == null) return 0;

        Directory.CreateDirectory(GetTemplateFolder());
        int n = 0;
        foreach (var g in res.glyphs)
        {
            if (g.strokes == null || g.strokes.Count == 0 || g.w < 1 || g.h < 1) continue;
            var data = new StrokeData { Width = g.w, Height = g.h, Baseline = g.h * 0.75 };
            foreach (var s in g.strokes)
            {
                if (s.Count < 2) continue;
                data.Strokes.Add(new Stroke
                {
                    Points = s.Select(p => new PointData { X = p[0], Y = p[1], Pressure = 1.0 }).ToList()
                });
            }
            if (data.Strokes.Count == 0) continue;
            string path = GetCharacterFilePath((char)g.cp, 1);
            File.WriteAllText(path, JsonSerializer.Serialize(data));
            n++;
        }
        return n;
    }

    // Indeterminate busy indicator for long, non-streaming batch ops.
    private void ShowBusy(string message)
    {
        if (BusyOverlay == null) return;
        BusyLabel.Text = T(message);
        BusyOverlay.Visibility = Visibility.Visible;
    }

    private void HideBusy()
    {
        if (BusyOverlay != null) BusyOverlay.Visibility = Visibility.Collapsed;
    }

    private static string LastLine(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unknown error";
        var lines = s.Trim().Split('\n');
        return lines[^1].Trim();
    }
}
