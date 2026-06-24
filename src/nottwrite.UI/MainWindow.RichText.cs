using System.Windows;

namespace nottwrite.UI;

public partial class MainWindow
{
    // Lightweight line-level lists & checkboxes for handwriting notes.
    private const string BulletPrefix      = "- ";
    private const string CheckUncheckedPre = "[ ] ";
    private const string CheckCheckedPre   = "[x] ";

    // index of the first char of the line that contains the cursor
    private int CurrentLineStart()
    {
        int i = Math.Clamp(_typeCursor, 0, _typeChars.Count);
        while (i > 0 && _typeChars[i - 1].Ch != '\n') i--;
        return i;
    }

    private string LineTextFrom(int start, int max = 8)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = start; i < _typeChars.Count && sb.Length < max && _typeChars[i].Ch != '\n'; i++)
            sb.Append(_typeChars[i].Ch);
        return sb.ToString();
    }

    private string? LinePrefixAt(int start)
    {
        string s = LineTextFrom(start);
        if (s.StartsWith(CheckUncheckedPre)) return CheckUncheckedPre;
        if (s.StartsWith(CheckCheckedPre))   return CheckCheckedPre;
        if (s.StartsWith(BulletPrefix))      return BulletPrefix;
        return null;
    }

    // Insert literal characters at the cursor (no per-char randomisation needed for markers)
    private void InsertStringAt(int index, string text)
    {
        foreach (char ch in text)
        {
            var variants = GetAllVariantPaths(ch);
            int vi = variants.Count > 0 ? Random.Shared.Next(variants.Count) : 0;
            _typeChars.Insert(index++, new CharData(ch, _typeBold, _typeItalic, _typePickerColor, 0, 0, vi,
                _typeUnderline, _typeStrike));
        }
    }

    private void RemoveRange(int index, int count)
    {
        for (int i = 0; i < count && index < _typeChars.Count; i++)
            _typeChars.RemoveAt(index);
    }

    // Toolbar action: toggle a list/checkbox prefix on the current line
    private void ToggleLinePrefix(string prefix)
    {
        if (_appMode != AppMode.Type) return;
        PushUndo();
        int start = CurrentLineStart();
        string? existing = LinePrefixAt(start);

        if (existing == prefix)                       // remove same prefix
        {
            RemoveRange(start, existing.Length);
            _typeCursor = Math.Max(start, _typeCursor - existing.Length);
        }
        else
        {
            if (existing != null)                     // swap a different prefix
            {
                RemoveRange(start, existing.Length);
                if (_typeCursor >= start) _typeCursor -= existing.Length;
            }
            InsertStringAt(start, prefix);
            if (_typeCursor >= start) _typeCursor += prefix.Length;
        }
        InvalidateLayout(); RefreshGeneratedText(); ResetCursorBlink();
    }

    private void InsertBullet_Click(object s, RoutedEventArgs e)   => ToggleLinePrefix(BulletPrefix);
    private void InsertCheckbox_Click(object s, RoutedEventArgs e) => ToggleLinePrefix(CheckUncheckedPre);

    private string? HeadingPrefixAt(int start)
    {
        string s = LineTextFrom(start, 5);
        if (s.StartsWith("### ")) return "### ";
        if (s.StartsWith("## "))  return "## ";
        if (s.StartsWith("# "))   return "# ";
        return null;
    }

    // Cycle the current line: normal → H1 → H2 → H3 → normal
    private void CycleHeading_Click(object s, RoutedEventArgs e)
    {
        if (_appMode != AppMode.Type) return;
        PushUndo();
        int start = CurrentLineStart();
        string? cur = HeadingPrefixAt(start);
        string next = cur == null ? "# " : cur == "# " ? "## " : cur == "## " ? "### " : "";

        if (cur != null)
        {
            RemoveRange(start, cur.Length);
            if (_typeCursor >= start) _typeCursor -= cur.Length;
        }
        if (next.Length > 0)
        {
            InsertStringAt(start, next);
            if (_typeCursor >= start) _typeCursor += next.Length;
        }
        InvalidateLayout(); RefreshGeneratedText(); ResetCursorBlink();
    }

    // Enter behaviour: continue the current list/checkbox item, or exit on an empty one.
    // Returns true if it handled the key (caller should not insert a plain newline).
    private bool HandleListEnter()
    {
        if (HasSelection) return false;
        int start = CurrentLineStart();
        string? prefix = LinePrefixAt(start);
        if (prefix == null) return false;

        // is the line empty apart from its prefix? -> exit the list
        int contentLen = 0;
        for (int i = start + prefix.Length; i < _typeChars.Count && _typeChars[i].Ch != '\n'; i++) contentLen++;
        if (contentLen == 0 && _typeCursor >= start + prefix.Length)
        {
            PushUndo();
            RemoveRange(start, prefix.Length);
            _typeCursor = start;
            InvalidateLayout(); RefreshGeneratedText(); ResetCursorBlink(); ScrollToCursor();
            return true;
        }

        // continue with a fresh item (checkboxes always continue unchecked)
        PushUndo();
        string cont = prefix == CheckCheckedPre ? CheckUncheckedPre : prefix;
        InsertStringAt(_typeCursor, "\n" + cont);
        _typeCursor += 1 + cont.Length;
        InvalidateLayout(); RefreshGeneratedText(); ResetCursorBlink(); ScrollToCursor();
        return true;
    }

    // Toggle the checkbox state of the line starting at `start` ( [ ] <-> [x] )
    private bool ToggleCheckboxLine(int start)
    {
        string? prefix = LinePrefixAt(start);
        if (prefix != CheckUncheckedPre && prefix != CheckCheckedPre) return false;
        // inner state char is at index start+1
        if (start + 1 >= _typeChars.Count) return false;
        PushUndo();
        var cur = _typeChars[start + 1];
        char now = cur.Ch == 'x' ? ' ' : 'x';
        _typeChars[start + 1] = cur with { Ch = now, VariantIdx = 0 };
        InvalidateLayout(); RefreshGeneratedText();
        return true;
    }
}
