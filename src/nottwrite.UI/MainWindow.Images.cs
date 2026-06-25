using System.IO;
using System.Windows;
using System.Windows.Input;
using SkiaSharp;

namespace nottwrite.UI;

public partial class MainWindow
{
    private const char ImageChar = '￼';   // OBJECT REPLACEMENT CHARACTER

    // current note's images: id -> raw bytes (persisted as base64) + decoded cache
    private Dictionary<string, byte[]> _noteImages = new();
    private readonly Dictionary<string, SKImage> _imageCache = new();

    // Load a note's images from their files (referenced by id in the note's chars).
    private void LoadNoteImages(NoteEntry note)
    {
        _noteImages = new();
        _imageCache.Clear();
        var ids = (note.Chars ?? new())
            .Where(c => c.ImageId != null).Select(c => c.ImageId!).Distinct();
        foreach (var id in ids)
        {
            try
            {
                string p = NoteImagePath(id);
                if (!File.Exists(p)) continue;
                var bytes = File.ReadAllBytes(p);
                _noteImages[id] = bytes;
                var img = SKImage.FromEncodedData(bytes);
                if (img != null) _imageCache[id] = img;
            }
            catch { }
        }
    }

    private (double W, double H) ImageDisplaySize(string id, double lineW)
    {
        if (!_imageCache.TryGetValue(id, out var img) || img.Width == 0)
            return (Math.Min(lineW, _fontSize * 5), _fontSize * 3);
        double maxW = Math.Min(lineW, _fontSize * 9);
        double maxH = _fontSize * 6;
        double s = Math.Min(maxW / img.Width, maxH / img.Height);
        if (s > 1) s = 1; // don't upscale
        return (img.Width * s, img.Height * s);
    }

    private void DrawImageBlock(SKCanvas canvas, CharLayout item, double baseY)
    {
        var id = item.Cd.ImageId!;
        double top = baseY + item.Y - _fontSize * 0.6;
        var dest = new SKRect((float)item.X, (float)top,
                              (float)(item.X + item.W), (float)(top + ImageBlockHeight(item)));
        if (_imageCache.TryGetValue(id, out var img))
            using (var ip = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true })
                canvas.DrawImage(img, dest, ip);
        else
            using (var p = new SKPaint { Color = new SKColor(0x33, 0x33, 0x44), Style = SKPaintStyle.Fill })
                canvas.DrawRect(dest, p);
    }

    // image's display height (W stored in layout, recompute H from aspect)
    private double ImageBlockHeight(CharLayout item)
    {
        var id = item.Cd.ImageId!;
        if (_imageCache.TryGetValue(id, out var img) && img.Width > 0)
            return item.W * img.Height / img.Width;
        return _fontSize * 3;
    }

    // ── Insert ────────────────────────────────────────────────────
    private void InsertImageBytes(byte[] bytes)
    {
        SKImage? img = null;
        try { img = SKImage.FromEncodedData(bytes); } catch { }
        if (img == null) { ShowToast("Could not read image", ToastKind.Error); return; }

        string id = Guid.NewGuid().ToString("N");
        _noteImages[id] = bytes;
        _imageCache[id] = img;
        // persist the image as a file immediately; the note only stores its id
        try { Directory.CreateDirectory(NotesImageDir); File.WriteAllBytes(NoteImagePath(id), bytes); }
        catch (Exception ex) { ShowToast("Couldn't save image: " + ex.Message, ToastKind.Warning); }

        PushUndo();
        // isolate image on its own line
        if (_typeCursor > 0 && _typeChars[_typeCursor - 1].Ch != '\n')
            InsertRawChar('\n');
        InsertRawChar(ImageChar, id);
        InsertRawChar('\n');
        InvalidateLayout(); RefreshGeneratedText(); ResetCursorBlink(); ScrollToCursor();
        ShowToast("Image added", ToastKind.Success);
    }

    private void InsertRawChar(char ch, string? imageId = null)
    {
        _typeChars.Insert(_typeCursor, new CharData(ch, false, false, _typePickerColor, 0, 0, 0,
            false, false, imageId));
        _typeCursor++;
    }

    private void InsertImageFromDialog()
    {
        if (_appMode != AppMode.Type) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Insert image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
        };
        if (dlg.ShowDialog() != true) return;
        try { InsertImageBytes(File.ReadAllBytes(dlg.FileName)); }
        catch (Exception ex) { ShowToast("Image insert failed: " + ex.Message, ToastKind.Error); }
    }

    // ── Right-click / drag-drop entry points ──────────────────────
    private void TypeCanvas_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_appMode != AppMode.Type) return;
        e.Handled = true;
        InsertImageFromDialog();
    }

    private void TypeCanvas_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void TypeCanvas_Drop(object sender, DragEventArgs e)
    {
        if (_appMode != AppMode.Type) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var f in files)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp")
            {
                try { InsertImageBytes(File.ReadAllBytes(f)); } catch { }
                break; // one image per drop keeps it simple
            }
        }
        e.Handled = true;
    }
}
