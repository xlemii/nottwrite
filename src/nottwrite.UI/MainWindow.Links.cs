using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nottwrite.UI;

public partial class MainWindow
{
    // A [[wikilink]] span over _typeChars: [start..end) inclusive of brackets, Title = inner text.
    private readonly record struct LinkSpan(int Start, int End, string Title);

    private List<LinkSpan> ParseLinkSpans(List<CharData> chars)
    {
        var spans = new List<LinkSpan>();
        for (int i = 0; i < chars.Count - 1; i++)
        {
            if (chars[i].Ch != '[' || chars[i + 1].Ch != '[') continue;
            // find closing ]]
            int j = i + 2;
            var sb = new System.Text.StringBuilder();
            bool closed = false;
            for (; j < chars.Count - 1; j++)
            {
                if (chars[j].Ch == '\n') break;
                if (chars[j].Ch == ']' && chars[j + 1].Ch == ']') { closed = true; break; }
                sb.Append(chars[j].Ch);
            }
            if (closed && sb.Length > 0)
            {
                spans.Add(new LinkSpan(i, j + 2, sb.ToString().Trim()));
                i = j + 1;
            }
        }
        return spans;
    }

    // Mask of char indices that belong to any link (for accent colouring).
    private bool[] BuildLinkMask(List<CharData> chars)
    {
        var mask = new bool[chars.Count];
        foreach (var s in ParseLinkSpans(chars))
            for (int k = s.Start; k < s.End && k < mask.Length; k++) mask[k] = true;
        return mask;
    }

    // Title of the link covering char index `idx`, or null.
    private string? LinkTitleAt(int idx)
    {
        foreach (var s in ParseLinkSpans(_typeChars))
            if (idx >= s.Start && idx < s.End) return s.Title;
        return null;
    }

    // Open the note with this title, creating it if none exists.
    private void OpenWikiLink(string title)
    {
        var existing = _notes.FirstOrDefault(n =>
            string.Equals(n.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { ShowEditor(existing.Id); return; }

        // snapshot current before creating
        if (_currentNoteId != null)
            _tabSnapshots[_currentNoteId] = _typeChars.Select(ToSerial).ToList();
        var now = DateTime.Now;
        var note = new NoteEntry(Guid.NewGuid().ToString("N"), title, new(), now, _notes.Count % NoteCovers.Length,
            CreatedAt: now, FolderId: _activeFolderId, Template: CurrentTemplate, FontName: _typeFontName);
        _notes.Insert(0, note);
        PersistNotes();
        ShowEditor(note.Id);
        ShowToast($"Opened '{title}'", ToastKind.Info);
    }

    // Notes linking to the current note's title.
    private List<NoteEntry> ComputeBacklinks()
    {
        if (_currentNoteId == null) return new();
        var cur = _notes.FirstOrDefault(n => n.Id == _currentNoteId);
        if (cur == null || string.IsNullOrWhiteSpace(cur.Title)) return new();
        string title = cur.Title.Trim();

        var result = new List<NoteEntry>();
        foreach (var n in _notes)
        {
            if (n.Id == _currentNoteId) continue;
            var chars = n.Chars.Select(c => new CharData(c.Ch, false, false, default, 0, 0, 0)).ToList();
            if (ParseLinkSpans(chars).Any(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase)))
                result.Add(n);
        }
        return result;
    }

    private void BacklinksBtn_Click(object sender, RoutedEventArgs e)
    {
        var links = ComputeBacklinks();
        BacklinksPanel.Children.Clear();
        if (links.Count == 0)
        {
            BacklinksPanel.Children.Add(new TextBlock
            {
                Text = "No backlinks yet", FontSize = 12,
                Foreground = GetBrush("SecondaryText"), Margin = new Thickness(10, 8, 10, 8),
            });
        }
        foreach (var n in links)
        {
            string id = n.Id;
            var row = new Border
            {
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(n.Title) ? "Untitled" : n.Title,
                    FontSize = 12.5, Foreground = GetBrush("PrimaryText"),
                },
            };
            row.MouseEnter += (_, _) => row.Background = GetBrush("NavActiveBg");
            row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Colors.Transparent);
            row.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; BacklinksPopup.IsOpen = false; ShowEditor(id); };
            BacklinksPanel.Children.Add(row);
        }
        BacklinksCountText.Text = links.Count.ToString();
        BacklinksPopup.IsOpen = true;
    }
}
