using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;

namespace nottwrite.UI;

public partial class MainWindow
{
    // ── Word / line selection ─────────────────────────────────────
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void SelectWordAt(int idx)
    {
        if (_typeChars.Count == 0) { ClearSel(); return; }
        idx = Math.Clamp(idx, 0, _typeChars.Count - 1);
        if (!IsWordChar(_typeChars[idx].Ch))
        {
            _selAnchor = idx; _typeCursor = Math.Min(idx + 1, _typeChars.Count);
            return;
        }
        int l = idx, r = idx + 1;
        while (l > 0 && IsWordChar(_typeChars[l - 1].Ch)) l--;
        while (r < _typeChars.Count && IsWordChar(_typeChars[r].Ch)) r++;
        _selAnchor = l; _typeCursor = r;
    }

    private void SelectLineAt(int idx)
    {
        if (_typeChars.Count == 0) { ClearSel(); return; }
        idx = Math.Clamp(idx, 0, _typeChars.Count);
        int l = idx; while (l > 0 && _typeChars[l - 1].Ch != '\n') l--;
        int r = idx; while (r < _typeChars.Count && _typeChars[r].Ch != '\n') r++;
        _selAnchor = l; _typeCursor = r;
    }

    // ── Find in note (Ctrl+F) ─────────────────────────────────────
    private readonly List<(int Start, int Len)> _findMatches = new();
    private int _findIdx = -1;

    private void ToggleFind()
    {
        if (_appMode != AppMode.Type) return;
        if (FindBar.Visibility == Visibility.Visible) { CloseFind(); return; }
        FindBar.Visibility = Visibility.Visible;
        FindBox.Text = HasSelection ? GetSelectedText() : "";
        FindBox.Focus();
        FindBox.SelectAll();
        RecomputeFind();
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        _findMatches.Clear();
        _findIdx = -1;
        RefreshGeneratedText();
        Keyboard.Focus(this);
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FindPlaceholder.Visibility = string.IsNullOrEmpty(FindBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        RecomputeFind();
    }

    private void RecomputeFind()
    {
        _findMatches.Clear();
        _findIdx = -1;
        string q = FindBox.Text;
        if (!string.IsNullOrEmpty(q))
        {
            var text = new string(_typeChars.Select(c => c.Ch).ToArray());
            int from = 0;
            while (from <= text.Length - q.Length)
            {
                int at = text.IndexOf(q, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                _findMatches.Add((at, q.Length));
                from = at + 1;   // allow overlapping starts
            }
            if (_findMatches.Count > 0) { _findIdx = 0; GoToMatch(0); }
        }
        FindCountText.Text = _findMatches.Count == 0
            ? (string.IsNullOrEmpty(q) ? "" : "0/0")
            : $"{_findIdx + 1}/{_findMatches.Count}";
        RefreshGeneratedText();
    }

    private void GoToMatch(int i)
    {
        if (i < 0 || i >= _findMatches.Count) return;
        _findIdx = i;
        var (s, len) = _findMatches[i];
        _selAnchor = s; _typeCursor = s + len;
        FindCountText.Text = $"{_findIdx + 1}/{_findMatches.Count}";
        ScrollToCursor();
        RefreshGeneratedText();
    }

    private void FindNext() { if (_findMatches.Count > 0) GoToMatch((_findIdx + 1) % _findMatches.Count); }
    private void FindPrev() { if (_findMatches.Count > 0) GoToMatch((_findIdx - 1 + _findMatches.Count) % _findMatches.Count); }

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext();
    private void FindPrev_Click(object sender, RoutedEventArgs e) => FindPrev();
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) FindPrev(); else FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) { CloseFind(); e.Handled = true; }
    }

    // draw match highlights (called from the type paint, after selection)
    private void DrawFindHighlights(SKCanvas canvas, List<CharLayout> layout, double baseY)
    {
        if (_findMatches.Count == 0) return;
        var cxs = _cachedCursorXs;
        using var all = new SKPaint { Color = new SKColor(0xFB, 0xBF, 0x24, 70), IsAntialias = true };
        using var cur = new SKPaint { Color = new SKColor(0xFB, 0xBF, 0x24, 150), IsAntialias = true };

        for (int m = 0; m < _findMatches.Count; m++)
        {
            var (s, len) = _findMatches[m];
            var paint = m == _findIdx ? cur : all;
            int lineKey = int.MinValue;
            float lx1 = 0, lx2 = 0, lly = 0, lsc = 1f;
            void Flush()
            {
                if (lineKey == int.MinValue) return;
                float hH = (float)(_fontSize * lsc * 1.05);
                float hy = (float)(baseY + lly - _fontSize * lsc * 0.83);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(lx1 - 1, hy, lx2 + 1, hy + hH), 3, 3), paint);
            }
            for (int k = s; k < s + len && k < layout.Count; k++)
            {
                if (layout[k].Cd.Ch == '\n') continue;
                int lk = (int)Math.Round(layout[k].Y);
                float x1 = k     < cxs.Length ? cxs[k]     : (float)layout[k].X;
                float x2 = k + 1 < cxs.Length ? cxs[k + 1] : x1 + 8f;
                if (lk != lineKey) { Flush(); lineKey = lk; lx1 = x1; lx2 = x2; lly = (float)layout[k].Y; lsc = (float)layout[k].Scale; }
                else { if (x1 < lx1) lx1 = x1; if (x2 > lx2) lx2 = x2; }
            }
            Flush();
        }
    }
}
