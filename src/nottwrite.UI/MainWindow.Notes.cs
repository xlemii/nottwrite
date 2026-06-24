using nottwrite.Core.Models;
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

namespace nottwrite.UI;

public partial class MainWindow
{
    // ─── Notes ───────────────────────────────────────────────────

    private record SerializableChar(
        char Ch, bool Bold, bool Italic,
        byte R, byte G, byte B, byte A,
        double RotDeg, double JitterY, int VariantIdx,
        bool Underline = false, bool Strikethrough = false,
        string? ImageId = null);

    private record NoteEntry(string Id, string Title, List<SerializableChar> Chars, DateTime UpdatedAt, int ColorIndex = 0, bool Favorite = false, string? CustomColorHex = null, List<string>? Tags = null, DateTime CreatedAt = default, string? FolderId = null, DateTime LastOpenedAt = default, string? Template = null, string? FontName = null, Dictionary<string, string>? Images = null)
    {
        public string PreviewText =>
            new string(Chars.Take(120).Select(c => c.Ch).ToArray()).Replace('\n', ' ');

        public List<string> TagList => Tags ?? new List<string>();
    }

    private record NoteFolder(string Id, string Name);

    // Persisted document wrapper (new format). Old format = bare NoteEntry[].
    private record NotesDocument(List<NoteFolder> Folders, List<NoteEntry> Notes);

    private List<NoteFolder> _folders = new();
    private string? _activeFolderId;   // null = Home (all notes + strips)

    private static SerializableChar ToSerial(CharData cd) =>
        new(cd.Ch, cd.Bold, cd.Italic, cd.Color.R, cd.Color.G, cd.Color.B, cd.Color.A,
            cd.RotDeg, cd.JitterY, cd.VariantIdx, cd.Underline, cd.Strikethrough, cd.ImageId);

    private static CharData FromSerial(SerializableChar s) =>
        new(s.Ch, s.Bold, s.Italic, Color.FromArgb(s.A, s.R, s.G, s.B),
            s.RotDeg, s.JitterY, s.VariantIdx, s.Underline, s.Strikethrough, s.ImageId);

    private static readonly string NotesFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "nottwrite", "notes.json");

    // Cover color pairs: (top, bottom) — notebook gradient palette
    private static readonly (string Top, string Bot, string Accent)[] NoteCovers =
    [
        ("#6B48C2", "#3A2A7A", "#8B7DC4"),  // violet
        ("#C24848", "#7A2A2A", "#E07070"),  // red
        ("#2E8B57", "#1A5C38", "#52C88A"),  // green
        ("#2874A6", "#154C72", "#50A0D0"),  // blue
        ("#B7770D", "#7A4F08", "#E0A840"),  // amber
        ("#7D3C98", "#4A2060", "#B070D0"),  // purple
        ("#1A7A7A", "#0F4F4F", "#40B0B0"),  // teal
        ("#8B5A2B", "#5C3A1A", "#C0844A"),  // brown
        ("#1E6B1E", "#103F10", "#48A048"),  // forest
        ("#8B3A62", "#5C1E40", "#C06090"),  // rose
    ];

    private List<NoteEntry>    _notes         = new();
    private string?            _currentNoteId;
    private bool               _notesDirty;
    private List<string>       _openTabs      = new();   // ordered open tab ids
    private HashSet<string>    _dirtyTabs     = new();   // tabs with unsaved changes
    private Dictionary<string, List<SerializableChar>> _tabSnapshots = new(); // saved chars per tab
    private string?            _pendingCloseTabId;
    private string?            _pendingRenameId;

    private const int NotesBackupCount = 5;
    private static string NotesBackupPath(int n) => NotesFilePath + ".bak" + n;
    private DateTime _lastNotesBackup = DateTime.MinValue;

    private static NotesDocument? TryLoadNotesFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path).TrimStart();
            if (string.IsNullOrWhiteSpace(json)) return null;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // old format = bare array of notes; new format = { Folders, Notes }
            if (json[0] == '[')
            {
                var list = JsonSerializer.Deserialize<List<NoteEntry>>(json, opts);
                if (list == null) return null;
                return new NotesDocument(new(), list.Where(n => n.Chars != null).ToList());
            }

            var doc = JsonSerializer.Deserialize<NotesDocument>(json, opts);
            if (doc?.Notes == null) return null;
            return doc with { Notes = doc.Notes.Where(n => n.Chars != null).ToList(),
                              Folders = doc.Folders ?? new() };
        }
        catch { return null; }
    }

    private bool _notesLoaded;

    private void LoadNotes()
    {
        // load once per session — re-entry (e.g. switching back to Notes) keeps state
        if (_notesLoaded) { RefreshFolderList(); RefreshNotesGrid(); return; }

        _notes.Clear();
        _folders.Clear();

        // primary file, then fall back through rotating backups on corruption
        var loaded = TryLoadNotesFrom(NotesFilePath);
        bool recovered = false;
        if (loaded == null)
        {
            for (int i = 1; i <= NotesBackupCount; i++)
            {
                loaded = TryLoadNotesFrom(NotesBackupPath(i));
                if (loaded != null) { recovered = true; break; }
            }
        }

        if (loaded != null)
        {
            _folders = loaded.Folders;
            // normalise legacy notes: missing CreatedAt → UpdatedAt
            _notes = loaded.Notes
                .Select(n => n.CreatedAt == default ? n with { CreatedAt = n.UpdatedAt } : n)
                .ToList();
        }
        _notesLoaded = true;

        if (recovered)
            try { File.WriteAllText(NotesFilePath, JsonSerializer.Serialize(BuildDoc())); } catch { }

        RefreshFolderList();
        RefreshNotesGrid();
    }

    private NotesDocument BuildDoc() => new(_folders, _notes);

    // ── Per-note font binding ─────────────────────────────────────
    // A note renders with the font it was written in, not the global one.
    private void ApplyNoteFont(string? template, string? fontName)
    {
        if (fontName != null)                       // system / installed font
        {
            _typeFontName = fontName;
            _cachedTfName = null;
        }
        else                                        // handwriting template
        {
            _typeFontName = null;
            if (!string.IsNullOrEmpty(template) && template != CurrentTemplate)
            {
                CurrentTemplate = template;
                _charCache.Clear();
                _strokeDataCache.Clear();
                RefreshTemplateComboBox(template);
            }
        }
        PopulateTypeFontCombo();   // ensure built
        SyncTypeFontCombo();       // reflect selection in the toolbar picker
        InvalidateLayout();
        RefreshGeneratedText();
    }

    // Store the active font on the current note (called on font change / save).
    private void BindFontToCurrentNote()
    {
        if (_currentNoteId == null) return;
        int idx = _notes.FindIndex(n => n.Id == _currentNoteId);
        if (idx < 0) return;
        _notes[idx] = _notes[idx] with { Template = CurrentTemplate, FontName = _typeFontName };
    }

    private void SaveCurrentNoteIfOpen()
    {
        if (_currentNoteId == null) return;
        int idx = _notes.FindIndex(n => n.Id == _currentNoteId);
        if (idx < 0) return;
        _notes[idx] = _notes[idx] with
        {
            Title     = string.IsNullOrWhiteSpace(NoteTitleBox.Text) ? "Untitled" : NoteTitleBox.Text,
            Chars     = _typeChars.Select(ToSerial).ToList(),
            UpdatedAt = DateTime.Now,
        };
        PersistNotes();
        NotesSavedLabel.Text = "Saved";
    }

    private void PersistNotes()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NotesFilePath)!);
            RotateNotesBackup();                       // snapshot previous state (time-gated)

            // atomic write: temp file then swap, so a crash mid-write can't corrupt notes.json
            string json = JsonSerializer.Serialize(BuildDoc());
            string tmp  = NotesFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(NotesFilePath))
                File.Replace(tmp, NotesFilePath, null);
            else
                File.Move(tmp, NotesFilePath);
        }
        catch { }
    }

    // Copy the current notes.json into a rotating set of 5 backups.
    // Time-gated to ~every 2 min so the 5 slots span time, not one keystroke burst.
    private void RotateNotesBackup()
    {
        try
        {
            if (!File.Exists(NotesFilePath)) return;
            if (DateTime.Now - _lastNotesBackup < TimeSpan.FromMinutes(2)) return;
            _lastNotesBackup = DateTime.Now;

            // shift bak4->bak5, …, bak1->bak2 (oldest dropped)
            for (int i = NotesBackupCount - 1; i >= 1; i--)
            {
                string src = NotesBackupPath(i);
                if (File.Exists(src)) File.Copy(src, NotesBackupPath(i + 1), true);
            }
            File.Copy(NotesFilePath, NotesBackupPath(1), true);
        }
        catch { }
    }

    private void ShowLibrary()
    {
        if (_currentNoteId != null && _appMode == AppMode.Type)
            SaveCurrentNoteIfOpen();
        _currentNoteId = null;
        RefreshNotesGrid();
    }

    private void ShowEditor(string id)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note == null) return;

        // snapshot current tab + stash its undo/redo before leaving it
        if (_currentNoteId != null && _currentNoteId != id)
        {
            _tabSnapshots[_currentNoteId] = _typeChars.Select(ToSerial).ToList();
            StashHistory(_currentNoteId);
        }

        if (!_openTabs.Contains(id))
            _openTabs.Add(id);

        _currentNoteId = id;

        // track recency for the Home "recently opened" strip
        int ni = _notes.FindIndex(n => n.Id == id);
        if (ni >= 0) _notes[ni] = _notes[ni] with { LastOpenedAt = DateTime.Now };

        // load from in-memory snapshot or persisted note
        List<SerializableChar> chars = _tabSnapshots.TryGetValue(id, out var snap)
            ? snap : note.Chars;

        // restore this note's own undo/redo history (kept across switches)
        LoadHistory(id);
        LoadNoteImages(note);
        _typeChars = chars.Select(FromSerial).ToList();
        _typeCursor = _typeChars.Count;
        _selAnchor = -1;
        _notesDirty = true;
        NoteTitleBox.Text = note.Title;
        _notesDirty = false;
        NotesSavedLabel.Text = "";

        if (_appMode != AppMode.Type)
            SwitchMode(AppMode.Type);
        else
        {
            bool noteActive = _currentNoteId != null;
            NotesBackBtn.Visibility = noteActive ? Visibility.Visible : Visibility.Collapsed;
            NotesBackSep.Visibility = noteActive ? Visibility.Visible : Visibility.Collapsed;
            RefreshTabBar();
            InvalidateLayout();
            RefreshGeneratedText();
        }

        // render the note in the font it was written in
        if (note.Template != null || note.FontName != null)
            ApplyNoteFont(note.Template, note.FontName);
    }

    private void RefreshTabBar()
    {
        NoteTabsPanel.Children.Clear();
        if (_openTabs.Count == 0 || _appMode != AppMode.Type)
        {
            NoteTabBar.Visibility = Visibility.Collapsed;
            return;
        }
        NoteTabBar.Visibility = Visibility.Visible;

        foreach (var tabId in _openTabs)
        {
            var note  = _notes.FirstOrDefault(n => n.Id == tabId);
            if (note == null) continue;
            bool active = tabId == _currentNoteId;
            bool dirty  = _dirtyTabs.Contains(tabId);
            string label = (string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title)
                           + (dirty ? " ●" : "");

            var cover  = NoteCovers[note.ColorIndex % NoteCovers.Length];
            var accent = (Color)ColorConverter.ConvertFromString(cover.Accent);

            // accent dot
            var dot = new Border
            {
                Width = 8, Height = 8,
                CornerRadius = new CornerRadius(4),
                Background   = new SolidColorBrush(accent),
                Margin       = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var titleTb = new TextBlock
            {
                Text         = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title,
                FontSize     = 12,
                FontWeight   = active ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground   = active
                    ? GetBrush("PrimaryText")
                    : GetBrush("SecondaryText"),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth     = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var dirtyDot = new TextBlock
            {
                Text       = "●",
                FontSize   = 7,
                Foreground = GetBrush("AccentBrush"),
                Margin     = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = dirty ? Visibility.Visible : Visibility.Collapsed,
            };

            // X button
            var closeId = tabId;
            var xBtn = new Button
            {
                Content         = "✕",
                FontSize        = 9,
                Width           = 18, Height = 18,
                Padding         = new Thickness(0),
                Background      = Brushes.Transparent,
                Foreground      = GetBrush("SecondaryText"),
                BorderThickness = new Thickness(0),
                Margin          = new Thickness(6, 0, 0, 0),
                Cursor          = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            xBtn.Click += (_, e) => { e.Handled = true; RequestCloseTab(closeId); };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(titleTb);
            row.Children.Add(dirtyDot);
            row.Children.Add(xBtn);

            var switchId = tabId;
            var tab = new Border
            {
                Padding     = new Thickness(12, 0, 10, 0),
                Height      = 36,
                Background  = active
                    ? GetBrush("CardBg")
                    : Brushes.Transparent,
                BorderBrush = GetBrush("AppBorderBrush"),
                BorderThickness = active
                    ? new Thickness(0, 0, 1, 0)
                    : new Thickness(0, 0, 1, 0),
                Cursor      = Cursors.Hand,
                Child       = row,
            };
            tab.MouseLeftButtonUp += (_, _) => { if (switchId != _currentNoteId) ShowEditor(switchId); };

            NoteTabsPanel.Children.Add(tab);
        }
    }

    private void RequestCloseTab(string id)
    {
        if (_dirtyTabs.Contains(id))
        {
            var note = _notes.FirstOrDefault(n => n.Id == id);
            string title = string.IsNullOrWhiteSpace(note?.Title) ? "Untitled" : note!.Title;
            _pendingCloseTabId = id;
            SaveCloseMsg.Text = $"Do you want to save changes to \"{title}\"?";
            SaveCloseOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            CloseTab(id);
        }
    }

    private void CloseTab(string id)
    {
        _tabSnapshots.Remove(id);
        _dirtyTabs.Remove(id);
        _openTabs.Remove(id);
        DropHistory(id);

        if (_currentNoteId == id)
        {
            _currentNoteId = null;
            if (_openTabs.Count > 0)
                ShowEditor(_openTabs[^1]);
            else
            {
                _undoStack = new();
                _redoTypeStack = new();
                _typeChars.Clear();
                _typeCursor = 0;
                InvalidateLayout();
                SwitchMode(AppMode.Notes);
                return;
            }
        }
        RefreshTabBar();
    }

    private void SaveCloseSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCloseOverlay.Visibility = Visibility.Collapsed;
        if (_pendingCloseTabId == null) return;
        string id = _pendingCloseTabId;
        _pendingCloseTabId = null;

        // save snapshot to note
        int idx = _notes.FindIndex(n => n.Id == id);
        if (idx >= 0 && _tabSnapshots.TryGetValue(id, out var snap))
        {
            _notes[idx] = _notes[idx] with { Chars = snap, UpdatedAt = DateTime.Now };
            PersistNotes();
        }
        _dirtyTabs.Remove(id);
        CloseTab(id);
    }

    private void SaveCloseDontSave_Click(object sender, RoutedEventArgs e)
    {
        SaveCloseOverlay.Visibility = Visibility.Collapsed;
        if (_pendingCloseTabId == null) return;
        string id = _pendingCloseTabId;
        _pendingCloseTabId = null;
        _dirtyTabs.Remove(id);
        CloseTab(id);
    }

    private void SaveCloseCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingCloseTabId = null;
        SaveCloseOverlay.Visibility = Visibility.Collapsed;
    }

    // ── Notes search / tag filter ──────────────────────────────────
    private string  _noteSearchQuery = "";
    private string? _activeTagFilter = null;

    private bool NoteMatchesFilter(NoteEntry note)
    {
        // active tag chip filter
        if (_activeTagFilter != null &&
            !note.TagList.Any(t => string.Equals(t, _activeTagFilter, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (string.IsNullOrWhiteSpace(_noteSearchQuery)) return true;

        // tokens prefixed with # match tags; others match title/preview/tags
        foreach (var raw in _noteSearchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.StartsWith('#'))
            {
                var tag = raw[1..];
                if (tag.Length == 0) continue;
                if (!note.TagList.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            else
            {
                bool hit = note.Title.Contains(raw, StringComparison.OrdinalIgnoreCase)
                        || note.PreviewText.Contains(raw, StringComparison.OrdinalIgnoreCase)
                        || note.TagList.Any(t => t.Contains(raw, StringComparison.OrdinalIgnoreCase));
                if (!hit) return false;
            }
        }
        return true;
    }

    private void NotesSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _noteSearchQuery = NotesSearchBox.Text.Trim();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(NotesSearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        SearchClearBtn.Visibility = string.IsNullOrEmpty(NotesSearchBox.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        RefreshNotesGrid();
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        NotesSearchBox.Text = "";
    }

    private void RefreshTagFilterStrip()
    {
        if (TagFilterPanel == null) return;
        TagFilterPanel.Children.Clear();

        var allTags = _notes.SelectMany(n => n.TagList)
                            .Select(t => t.Trim())
                            .Where(t => t.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                            .ToList();

        foreach (var tag in allTags)
        {
            bool active = string.Equals(tag, _activeTagFilter, StringComparison.OrdinalIgnoreCase);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding      = new Thickness(11, 4, 11, 4),
                Margin       = new Thickness(0, 0, 6, 6),
                Cursor       = Cursors.Hand,
                Background    = active ? GetBrush("AccentBrush") : GetBrush("Surface2Bg"),
                BorderBrush   = active ? GetBrush("AccentBrush") : GetBrush("AppBorderBrush"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text       = "#" + tag,
                    FontSize   = 11,
                    Foreground = active ? new SolidColorBrush(Colors.White) : GetBrush("SecondaryText"),
                },
            };
            string captured = tag;
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _activeTagFilter = active ? null : captured;
                RefreshNotesGrid();
            };
            TagFilterPanel.Children.Add(chip);
        }
        TagFilterPanel.Visibility = allTags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private string _noteSort = "manual";

    private IEnumerable<NoteEntry> SortNotes(IEnumerable<NoteEntry> src) => _noteSort switch
    {
        "name"     => src.OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase),
        "created"  => src.OrderByDescending(n => n.CreatedAt),
        "modified" => src.OrderByDescending(n => n.UpdatedAt),
        _          => src,   // manual = _notes list order
    };

    // Build one interactive notebook card (cover + label). Draggable only in
    // the main grid under manual sort with no filter active.
    private FrameworkElement BuildNoteCard(NoteEntry note, bool draggable)
    {
        var id          = note.Id;
        var coverDef    = NoteCovers[note.ColorIndex % NoteCovers.Length];
        var coverBorder = MakeNotebookCard(note, coverDef);
        var label       = MakeNotebookLabel(note);

        var coverHolder = new Grid { Width = 100 };
        coverHolder.Children.Add(coverBorder);
        AttachTiltEffect(coverHolder, coverBorder);

        var wrapper = new StackPanel
        {
            Width     = 100,
            Margin    = new Thickness(0, 0, 18, 18),
            Cursor    = Cursors.Hand,
            AllowDrop = draggable,
            Tag       = id,
        };
        wrapper.Children.Add(coverHolder);
        wrapper.Children.Add(label);

        bool dblPending = false;
        wrapper.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { dblPending = true; e.Handled = true; OpenRenameOverlay(id); return; }
            _noteDragStart = e.GetPosition(null);
            _noteDragId    = id;
            _noteDragging  = false;
        };
        if (draggable)
        {
            wrapper.PreviewMouseMove += Note_PreviewMouseMove;
            wrapper.DragEnter += (s, e) => Note_DragOver(s, e, true);
            wrapper.DragLeave += (s, e) => Note_DragOver(s, e, false);
            wrapper.Drop      += Note_Drop;
        }
        wrapper.MouseLeftButtonUp += (_, e) =>
        {
            if (_noteDragging) { _noteDragging = false; return; }
            if (!dblPending) ShowEditor(id); else dblPending = false;
        };
        wrapper.ContextMenu = BuildNoteContextMenu(id);
        return wrapper;
    }

    public void RefreshNotesGrid()
    {
        if (NotesGridPanel == null) return;
        RefreshTagFilterStrip();

        bool isHome    = _activeFolderId == null;
        bool filtering = !string.IsNullOrWhiteSpace(_noteSearchQuery) || _activeTagFilter != null;
        bool draggable = _noteSort == "manual" && !filtering;

        NotesViewTitle.Text = isHome ? "Home"
            : _folders.FirstOrDefault(f => f.Id == _activeFolderId)?.Name ?? "Folder";

        // notes in scope: Home = all; folder = that folder only
        var scope = _notes.Where(n => isHome || n.FolderId == _activeFolderId);
        var visible = scope.Where(NoteMatchesFilter).ToList();

        // ── Home strips (hidden while searching / filtering) ──
        bool showStrips = isHome && !filtering;
        RecentSection.Visibility    = Visibility.Collapsed;
        FavoritesSection.Visibility = Visibility.Collapsed;
        if (showStrips)
        {
            var recent = _notes.Where(n => n.LastOpenedAt != default)
                               .OrderByDescending(n => n.LastOpenedAt).Take(8).ToList();
            RecentPanel.Children.Clear();
            foreach (var n in recent) RecentPanel.Children.Add(BuildNoteCard(n, false));
            RecentSection.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var favs = _notes.Where(n => n.Favorite).ToList();
            FavoritesPanel.Children.Clear();
            foreach (var n in favs) FavoritesPanel.Children.Add(BuildNoteCard(n, false));
            FavoritesSection.Visibility = favs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── main grid ──
        AllNotesLabel.Text = isHome ? "ALL NOTES" : "NOTES";
        AllNotesLabel.Visibility = (visible.Count > 0 || !filtering) ? Visibility.Visible : Visibility.Collapsed;
        NotesGridPanel.Children.Clear();
        foreach (var note in SortNotes(visible))
            NotesGridPanel.Children.Add(BuildNoteCard(note, draggable));

        // ghost "add notebook" card sits where the next book would go
        if (!filtering)
            NotesGridPanel.Children.Add(BuildAddCard());

        // ── empty state (only for filtered no-results) ──
        bool noResults = filtering && visible.Count == 0
                         && RecentSection.Visibility != Visibility.Visible
                         && FavoritesSection.Visibility != Visibility.Visible;
        NotesEmptyState.Visibility = noResults ? Visibility.Visible : Visibility.Collapsed;
        _emptyStateFiltering = true;
        EmptyStateIcon.Text  = "\U0001F50D";
        EmptyStateTitle.Text = "No matching notes";
        EmptyStateHint.Text  = "No notes match your search or tag filter. Try a different term.";
        EmptyStateBtnIcon.Text = "✕";
        EmptyStateBtnText.Text = "Clear filters";
    }

    // Dashed-outline "add notebook" card — clicking creates a note in the current folder.
    private FrameworkElement BuildAddCard()
    {
        var rect = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 5, RadiusY = 5,
            StrokeThickness = 2,
            Stroke = GetBrush("AppBorderBrush"),
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Colors.Transparent),
        };
        var plus = new TextBlock
        {
            Text = "+", FontSize = 34, FontWeight = FontWeights.Light,
            Foreground = GetBrush("SecondaryText"), Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var coverGrid = new Grid { Width = 100, Height = 110 };
        coverGrid.Children.Add(rect);
        coverGrid.Children.Add(plus);

        var label = new TextBlock
        {
            Text = "New note", FontSize = 11, FontWeight = FontWeights.Medium,
            Foreground = GetBrush("SecondaryText"),
            TextAlignment = TextAlignment.Center, Opacity = 0.8,
            Margin = new Thickness(2, 7, 2, 0),
        };

        var wrapper = new StackPanel
        {
            Width = 100, Margin = new Thickness(0, 0, 18, 18), Cursor = Cursors.Hand,
        };
        wrapper.Children.Add(coverGrid);
        wrapper.Children.Add(label);

        wrapper.MouseEnter += (_, _) =>
        {
            rect.Stroke = GetBrush("AccentBrush");
            plus.Foreground = GetBrush("AccentBrush");
            plus.Opacity = 1.0;
        };
        wrapper.MouseLeave += (_, _) =>
        {
            rect.Stroke = GetBrush("AppBorderBrush");
            plus.Foreground = GetBrush("SecondaryText");
            plus.Opacity = 0.7;
        };
        wrapper.MouseLeftButtonUp += (_, _) => NewNote_Click(this, new RoutedEventArgs());
        return wrapper;
    }

    private void SortCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _noteSort = tag;
            RefreshNotesGrid();
        }
    }

    private bool _emptyStateFiltering;

    private void EmptyStateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_emptyStateFiltering)
        {
            // clear search + active tag filter
            NotesSearchBox.Text = "";
            _activeTagFilter = null;
            RefreshNotesGrid();
        }
        else
        {
            NewNote_Click(sender, e);
        }
    }

    // ── Drag & drop manual reorder ─────────────────────────────────
    private System.Windows.Point _noteDragStart;
    private string? _noteDragId;
    private bool    _noteDragging;

    private void Note_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _noteDragId == null || _noteDragging)
            return;
        // dragging disabled while a search/tag filter is active (order is ambiguous)
        if (!string.IsNullOrWhiteSpace(_noteSearchQuery) || _activeTagFilter != null)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _noteDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _noteDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is not StackPanel wrapper) return;
        _noteDragging = true;
        wrapper.Opacity = 0.4;
        try { DragDrop.DoDragDrop(wrapper, _noteDragId, DragDropEffects.Move); }
        finally { wrapper.Opacity = 1.0; }
    }

    private void Note_DragOver(object sender, DragEventArgs e, bool entering)
    {
        if (sender is not StackPanel wrapper) return;
        bool valid = e.Data.GetDataPresent(DataFormats.StringFormat)
                     && (string?)e.Data.GetData(DataFormats.StringFormat) != (string?)wrapper.Tag;
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        // highlight insertion target
        var cover = wrapper.Children.Count > 0 ? wrapper.Children[0] as UIElement : null;
        if (cover != null) cover.Opacity = entering && valid ? 0.65 : 1.0;
        e.Handled = true;
    }

    private void Note_Drop(object sender, DragEventArgs e)
    {
        if (sender is not StackPanel wrapper) return;
        var cover = wrapper.Children.Count > 0 ? wrapper.Children[0] as UIElement : null;
        if (cover != null) cover.Opacity = 1.0;

        if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
        string sourceId = (string)e.Data.GetData(DataFormats.StringFormat);
        string targetId = (string)wrapper.Tag;
        MoveNoteBefore(sourceId, targetId);
        e.Handled = true;
    }

    private void MoveNoteBefore(string sourceId, string targetId)
    {
        if (sourceId == targetId) return;
        int si = _notes.FindIndex(n => n.Id == sourceId);
        if (si < 0) return;
        var item = _notes[si];
        _notes.RemoveAt(si);
        int ti = _notes.FindIndex(n => n.Id == targetId);
        if (ti < 0) { _notes.Insert(si, item); return; }
        _notes.Insert(ti, item);
        PersistNotes();
        RefreshNotesGrid();
    }

    private static Border MakeNotebookCard(NoteEntry note,
        (string Top, string Bot, string Accent) cover)
    {
        // notebook cover gradient — custom color overrides preset
        var grad = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint   = new System.Windows.Point(0, 1),
        };
        if (note.CustomColorHex != null)
        {
            var baseColor = (Color)ColorConverter.ConvertFromString(note.CustomColorHex);
            var top = Color.FromArgb(255,
                (byte)Math.Min(255, baseColor.R + 40),
                (byte)Math.Min(255, baseColor.G + 40),
                (byte)Math.Min(255, baseColor.B + 40));
            var bot = Color.FromArgb(255,
                (byte)Math.Max(0, baseColor.R - 30),
                (byte)Math.Max(0, baseColor.G - 30),
                (byte)Math.Max(0, baseColor.B - 30));
            grad.GradientStops.Add(new GradientStop(top, 0));
            grad.GradientStops.Add(new GradientStop(bot, 1));
        }
        else
        {
            grad.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString(cover.Top), 0));
            grad.GradientStops.Add(new GradientStop(
                (Color)ColorConverter.ConvertFromString(cover.Bot), 1));
        }

        // decorative lines on cover
        var linesPanel = new StackPanel { Margin = new Thickness(10, 28, 10, 0) };
        for (int i = 0; i < 5; i++)
            linesPanel.Children.Add(new Border
            {
                Height     = 1.5,
                Margin     = new Thickness(0, 4, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                CornerRadius = new CornerRadius(1),
            });

        // spine strip on left
        var spine = new Border
        {
            Width      = 6,
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var coverGrid = new Grid { Height = 110 };
        coverGrid.Children.Add(new Border { Background = grad });
        coverGrid.Children.Add(spine);
        coverGrid.Children.Add(linesPanel);

        // emoji / first char on cover
        string icon = string.IsNullOrWhiteSpace(note.Title) ? "📝" : note.Title[0].ToString().ToUpper();
        coverGrid.Children.Add(new TextBlock
        {
            Text                = icon,
            FontSize            = 28,
            Foreground          = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Margin              = new Thickness(0, -10, 0, 0),
        });

        // favorite star badge
        if (note.Favorite)
            coverGrid.Children.Add(new TextBlock
            {
                Text                = "★",
                FontSize            = 14,
                Foreground          = new SolidColorBrush(Color.FromArgb(230, 255, 215, 0)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin              = new Thickness(0, 5, 7, 0),
            });

        var coverBorder = new Border
        {
            CornerRadius = new CornerRadius(5),
            Child        = coverGrid,
            ClipToBounds = true,
        };

        return coverBorder;
    }

    private FrameworkElement MakeNotebookLabel(NoteEntry note)
    {
        var stack = new StackPanel { Margin = new Thickness(2, 7, 2, 0) };
        stack.Children.Add(new TextBlock
        {
            Text          = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title,
            FontSize      = 11,
            FontWeight    = FontWeights.Medium,
            Foreground    = new SolidColorBrush(Color.FromArgb(220, 240, 240, 240)),
            TextTrimming  = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        });

        var tags = note.TagList;
        if (tags.Count > 0)
        {
            string text = tags.Count <= 2
                ? string.Join(" · ", tags.Select(t => "#" + t))
                : $"#{tags[0]} +{tags.Count - 1}";
            stack.Children.Add(new TextBlock
            {
                Text          = text,
                FontSize      = 9,
                Foreground    = GetBrush("AccentBrush"),
                TextTrimming  = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin        = new Thickness(0, 3, 0, 0),
                Opacity       = 0.85,
            });
        }
        return stack;
    }

    private void AutoSaveCurrentNote()
    {
        if (_currentNoteId == null) return;
        int idx = _notes.FindIndex(n => n.Id == _currentNoteId);
        if (idx < 0) return;
        _notes[idx] = _notes[idx] with
        {
            Title     = string.IsNullOrWhiteSpace(NoteTitleBox.Text) ? "Untitled" : NoteTitleBox.Text,
            Chars     = _typeChars.Select(ToSerial).ToList(),
            UpdatedAt = DateTime.Now,
            Template  = CurrentTemplate,
            FontName  = _typeFontName,
            Images    = CollectNoteImages(),
        };
        PersistNotes();
        NotesSavedLabel.Text = "Saved";
        if (_currentNoteId != null) _dirtyTabs.Remove(_currentNoteId);
        RefreshTabBar();
    }

    private void NewNote_Click(object sender, RoutedEventArgs e) => ShowNewNoteOverlay();

    // Create a note, optionally pre-filled with template content + title.
    private void CreateNote(string title, string body)
    {
        int colorIdx = _notes.Count % NoteCovers.Length;
        var now = DateTime.Now;
        var chars = TextToSerial(body);
        var note = new NoteEntry(Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(title) ? "Untitled" : title,
            chars, now, colorIdx,
            CreatedAt: now, FolderId: _activeFolderId,
            Template: CurrentTemplate, FontName: _typeFontName);
        _notes.Insert(0, note);
        PersistNotes();
        ShowEditor(note.Id);
    }

    // Build serialised chars from a plain string (markers like #, -, [ ] render via the editor).
    private List<SerializableChar> TextToSerial(string text)
    {
        var list = new List<SerializableChar>();
        foreach (char ch in text)
        {
            var variants = GetAllVariantPaths(ch);
            int vi = variants.Count > 0 ? Random.Shared.Next(variants.Count) : 0;
            var c = _typePickerColor;
            list.Add(new SerializableChar(ch, false, false, c.R, c.G, c.B, c.A, 0, 0, vi));
        }
        return list;
    }

    private void NotesBack_Click(object sender, RoutedEventArgs e)
    {
        // snapshot current content + history but keep all tabs open
        if (_currentNoteId != null)
        {
            _tabSnapshots[_currentNoteId] = _typeChars.Select(ToSerial).ToList();
            StashHistory(_currentNoteId);
        }
        SwitchMode(AppMode.Notes);
    }

    private string? _pendingDeleteId;

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNoteId != null) PromptDeleteNote(_currentNoteId);
    }

    private void DeleteNoteById(string id) => PromptDeleteNote(id);

    private void PromptDeleteNote(string id)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note == null) return;
        _pendingDeleteId = id;
        string title = string.IsNullOrWhiteSpace(note.Title) ? "Untitled" : note.Title;
        DeleteConfirmMsg.Text = $"\"{title}\" will be permanently deleted. This cannot be undone.";
        DeleteConfirmOverlay.Visibility = Visibility.Visible;
    }

    private void DeleteCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeleteId = null;
        DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
    }

    private void DeleteConfirm_Click(object sender, RoutedEventArgs e)
    {
        DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        if (_pendingDeleteId == null) return;
        string id = _pendingDeleteId;
        _pendingDeleteId = null;

        // capture for undo (note + its list position)
        int delIdx = _notes.FindIndex(n => n.Id == id);
        NoteEntry? deleted = delIdx >= 0 ? _notes[delIdx] : null;

        _notes.RemoveAll(n => n.Id == id);
        PersistNotes();
        _tabSnapshots.Remove(id);
        _dirtyTabs.Remove(id);
        _openTabs.Remove(id);
        DropHistory(id);
        if (_currentNoteId == id)
        {
            _currentNoteId = null;
            if (_openTabs.Count > 0)
            {
                ShowEditor(_openTabs[^1]);
                return;
            }
            _undoStack = new();
            _redoTypeStack = new();
            _typeChars.Clear();
            _typeCursor = 0;
            _layoutVersion++;
        }
        if (_openTabs.Count == 0)
            SwitchMode(AppMode.Notes);
        else
            RefreshTabBar();

        if (deleted != null)
        {
            int at = delIdx;
            ShowToast("Note deleted", ToastKind.Info, actionLabel: "Undo", action: () =>
            {
                _notes.Insert(Math.Min(at, _notes.Count), deleted);
                PersistNotes();
                if (_appMode == AppMode.Notes) RefreshNotesGrid();
            });
        }
    }

    private DispatcherTimer? _notesSaveTimer;
    private DispatcherTimer? _autoSaveTimer;

    private void NoteTitle_Changed(object sender, TextChangedEventArgs e)
    {
        if (_notesDirty || _currentNoteId == null) return;
        NotesSavedLabel.Text = "";
        ScheduleNoteSave();
    }

    private void NoteContent_Changed(object sender, TextChangedEventArgs e) { }

    private void ScheduleNoteSave()
    {
        if (_notesSaveTimer == null)
        {
            _notesSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _notesSaveTimer.Tick += (_, _) => { _notesSaveTimer.Stop(); AutoSaveCurrentNote(); };
        }
        _notesSaveTimer.Stop();
        _notesSaveTimer.Start();
    }

    public void StartAutoSaveTimer()
    {
        _autoSaveTimer?.Stop();
        if (!AutoSaveEnabled) return;
        _autoSaveTimer = new DispatcherTimer
            { Interval = TimeSpan.FromMinutes(AutoSaveMinutes) };
        _autoSaveTimer.Tick += (_, _) => SaveAllOpenTabs();
        _autoSaveTimer.Start();
    }

    private void SaveAllOpenTabs()
    {
        // snapshot current tab
        if (_currentNoteId != null)
            _tabSnapshots[_currentNoteId] = _typeChars.Select(ToSerial).ToList();

        bool any = false;
        foreach (var id in _openTabs)
        {
            int idx = _notes.FindIndex(n => n.Id == id);
            if (idx < 0) continue;
            if (_tabSnapshots.TryGetValue(id, out var snap))
            {
                _notes[idx] = _notes[idx] with { Chars = snap, UpdatedAt = DateTime.Now };
                _dirtyTabs.Remove(id);
                any = true;
            }
        }
        if (any)
        {
            PersistNotes();
            ShowToast("All notes auto-saved", ToastKind.Info);
        }
    }

    // ─── Rename / color ──────────────────────────────────────────

    private ContextMenu BuildNoteContextMenu(string id)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        var cm = new ContextMenu();

        var fav = new MenuItem { Header = note?.Favorite == true ? "★ Remove from favorites" : "☆ Add to favorites" };
        fav.Click += (_, _) => ToggleFavorite(id);

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) => OpenRenameOverlay(id);
        var tags = new MenuItem { Header = "Edit tags…" };
        tags.Click += (_, _) => OpenTagsOverlay(id);
        var color = new MenuItem { Header = "Change cover color" };
        color.Click += (_, _) => OpenColorPicker(id);

        // Move to folder submenu
        var move = new MenuItem { Header = "Move to folder" };
        var home = new MenuItem { Header = "🏠 Home", IsChecked = note?.FolderId == null };
        home.Click += (_, _) => MoveNoteToFolder(id, null);
        move.Items.Add(home);
        if (_folders.Count > 0) move.Items.Add(new Separator());
        foreach (var f in _folders)
        {
            string fid = f.Id;
            var mi = new MenuItem { Header = "📁 " + f.Name, IsChecked = note?.FolderId == fid };
            mi.Click += (_, _) => MoveNoteToFolder(id, fid);
            move.Items.Add(mi);
        }

        var del = new MenuItem { Header = "Delete" };
        del.Click += (_, _) => PromptDeleteNote(id);

        cm.Items.Add(fav);
        cm.Items.Add(new Separator());
        cm.Items.Add(rename);
        cm.Items.Add(tags);
        cm.Items.Add(color);
        cm.Items.Add(move);
        cm.Items.Add(new Separator());
        cm.Items.Add(del);
        return cm;
    }

    // ── True 3D Tilt hover effect (Viewport3D + Viewport2DVisual3D) ──
    private void AttachTiltEffect(Grid coverHolder, UIElement card)
    {
        if (!TiltEnabled) return;
        if (card is not FrameworkElement fe) return;

        // Fixed size so the 3D scene knows the card dimensions
        fe.Width  = 100;
        fe.Height = 110;
        coverHolder.Height = 110;
        coverHolder.ClipToBounds = true;

        // Remove from coverHolder — Viewport2DVisual3D will host it
        coverHolder.Children.Remove(fe);

        // ── Flat quad mesh sized to fill the 3D scene ──────────────
        const double hw = 1.0, hh = 1.1;   // half-width / half-height
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection(new[]
            {
                new Point3D(-hw, -hh, 0),
                new Point3D( hw, -hh, 0),
                new Point3D( hw,  hh, 0),
                new Point3D(-hw,  hh, 0),
            }),
            TextureCoordinates = new PointCollection(new[]
            {
                new System.Windows.Point(0, 1),
                new System.Windows.Point(1, 1),
                new System.Windows.Point(1, 0),
                new System.Windows.Point(0, 0),
            }),
            TriangleIndices = new Int32Collection(new[] { 0, 1, 2, 0, 2, 3 }),
        };

        // ── Rotation axes ───────────────────────────────────────────
        var rotX = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        var rotY = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var tg3d = new Transform3DGroup();
        tg3d.Children.Add(new RotateTransform3D(rotX));
        tg3d.Children.Add(new RotateTransform3D(rotY));

        // ── Material that hosts 2D content ──────────────────────────
        var hostMat = new DiffuseMaterial(Brushes.White);
        Viewport2DVisual3D.SetIsVisualHostMaterial(hostMat, true);

        var vp2d3d = new Viewport2DVisual3D
        {
            Geometry  = mesh,
            Material  = hostMat,
            Visual    = fe,
            Transform = tg3d,
        };

        // ── Viewport3D ──────────────────────────────────────────────
        var viewport = new Viewport3D
        {
            ClipToBounds = false,
            Camera = new PerspectiveCamera
            {
                Position      = new Point3D(0, 0, 3.8),
                LookDirection = new Vector3D(0, 0, -1),
                UpDirection   = new Vector3D(0, 1,  0),
                FieldOfView   = 32.3,   // 2·atan(1.1/3.8) — mesh fills viewport exactly
            },
        };
        viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        viewport.Children.Add(vp2d3d);

        // ── Shine overlay ───────────────────────────────────────────
        var shine = new Border
        {
            IsHitTestVisible = false,
            CornerRadius     = new CornerRadius(5),
            Opacity          = 0,
        };

        coverHolder.Children.Add(viewport);
        coverHolder.Children.Add(shine);

        // ── Scale on the whole coverHolder ──────────────────────────
        var scale = new ScaleTransform(1.0, 1.0);
        coverHolder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        coverHolder.RenderTransform = scale;

        coverHolder.MouseEnter += (_, _) => TiltAnimateScale(scale, 1.06, 180);

        coverHolder.MouseMove += (s, me) =>
        {
            var pos = me.GetPosition(coverHolder);
            double w = coverHolder.ActualWidth;
            double h = coverHolder.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double nx = (pos.X / w - 0.5) * 2;
            double ny = (pos.Y / h - 0.5) * 2;

            rotX.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            rotY.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            rotX.Angle =  ny * 22.0;
            rotY.Angle = -nx * 28.0;

            shine.Background = new RadialGradientBrush(
                Color.FromArgb(70, 255, 255, 255),
                Color.FromArgb(0,  255, 255, 255))
            {
                Center         = new System.Windows.Point(pos.X / w, pos.Y / h),
                GradientOrigin = new System.Windows.Point(pos.X / w, pos.Y / h),
                RadiusX = 0.65, RadiusY = 0.65,
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
            };
            shine.Opacity = 1.0;
        };

        coverHolder.MouseLeave += (_, _) =>
        {
            var dur  = new Duration(TimeSpan.FromMilliseconds(380));
            var ease = new System.Windows.Media.Animation.CubicEase
                       { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            rotX.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, dur) { EasingFunction = ease });
            rotY.BeginAnimation(AxisAngleRotation3D.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, dur) { EasingFunction = ease });
            TiltAnimateScale(scale, 1.0, 320);
            shine.Opacity = 0;
        };
    }

    private static void TiltAnimateScale(ScaleTransform st, double to, int ms)
    {
        var dur  = new Duration(TimeSpan.FromMilliseconds(ms));
        var ease = new System.Windows.Media.Animation.CubicEase
                   { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        st.BeginAnimation(ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(to, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(to, dur) { EasingFunction = ease });
    }

    private void ToggleFavorite(string id)
    {
        int idx = _notes.FindIndex(n => n.Id == id);
        if (idx < 0) return;
        _notes[idx] = _notes[idx] with { Favorite = !_notes[idx].Favorite };
        ScheduleNoteSave();
        RefreshNotesGrid();
    }

    private void OpenRenameOverlay(string id)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note == null) return;
        _pendingRenameId = id;
        RenameBox.Text = note.Title;
        RenameOverlay.Visibility = Visibility.Visible;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    private void RenameConfirm_Click(object sender, RoutedEventArgs e) => CommitRename();
    private void RenameCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingRenameId = null;
        _pendingRenameFolderId = null;
        RenameOverlay.Visibility = Visibility.Collapsed;
    }
    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) { e.Handled = true; CommitRename(); }
        if (e.Key == Key.Escape) { e.Handled = true; RenameCancel_Click(sender, e); }
    }

    private void CommitRename()
    {
        RenameOverlay.Visibility = Visibility.Collapsed;

        // folder rename takes precedence if pending
        if (_pendingRenameFolderId != null)
        {
            string fid = _pendingRenameFolderId;
            _pendingRenameFolderId = null;
            int fi = _folders.FindIndex(f => f.Id == fid);
            if (fi < 0) return;
            string fname = RenameBox.Text.Trim();
            if (string.IsNullOrEmpty(fname)) fname = "Folder";
            _folders[fi] = _folders[fi] with { Name = fname };
            PersistNotes();
            RefreshFolderList();
            RefreshNotesGrid();
            return;
        }

        if (_pendingRenameId == null) return;
        string id = _pendingRenameId;
        _pendingRenameId = null;
        int idx = _notes.FindIndex(n => n.Id == id);
        if (idx < 0) return;
        string newTitle = RenameBox.Text.Trim();
        if (string.IsNullOrEmpty(newTitle)) newTitle = "Untitled";
        _notes[idx] = _notes[idx] with { Title = newTitle, UpdatedAt = DateTime.Now };
        PersistNotes();
        RefreshNotesGrid();
        RefreshTabBar();
    }

    // ── Tags overlay ──────────────────────────────────────────────
    private string? _pendingTagsId;

    private void OpenTagsOverlay(string id)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        if (note == null) return;
        _pendingTagsId = id;
        TagsBox.Text = string.Join(", ", note.TagList);
        TagsOverlay.Visibility = Visibility.Visible;
        TagsBox.Focus();
        TagsBox.SelectAll();
    }

    private void TagsConfirm_Click(object sender, RoutedEventArgs e) => CommitTags();
    private void TagsCancel_Click(object sender, RoutedEventArgs e)
    {
        _pendingTagsId = null;
        TagsOverlay.Visibility = Visibility.Collapsed;
    }
    private void TagsBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) { e.Handled = true; CommitTags(); }
        if (e.Key == Key.Escape) { e.Handled = true; TagsCancel_Click(sender, e); }
    }

    private void CommitTags()
    {
        TagsOverlay.Visibility = Visibility.Collapsed;
        if (_pendingTagsId == null) return;
        string id = _pendingTagsId;
        _pendingTagsId = null;
        int idx = _notes.FindIndex(n => n.Id == id);
        if (idx < 0) return;

        var tags = TagsBox.Text
            .Split(new[] { ',', '#' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        // if removing the currently filtered tag, clear filter when no note has it
        _notes[idx] = _notes[idx] with { Tags = tags, UpdatedAt = DateTime.Now };
        if (_activeTagFilter != null &&
            !_notes.Any(n => n.TagList.Any(t => string.Equals(t, _activeTagFilter, StringComparison.OrdinalIgnoreCase))))
            _activeTagFilter = null;

        PersistNotes();
        RefreshNotesGrid();
    }

    private void OpenColorPicker(string id)
    {
        var note = _notes.FirstOrDefault(n => n.Id == id);
        Color initialColor = Colors.SlateBlue;
        if (note?.CustomColorHex != null)
            initialColor = (Color)ColorConverter.ConvertFromString(note.CustomColorHex);
        else if (note != null)
            initialColor = (Color)ColorConverter.ConvertFromString(NoteCovers[note.ColorIndex % NoteCovers.Length].Top);

        var picker = new ColorPickerWindow(initialColor) { Owner = this };
        if (picker.ShowDialog() != true) return;

        int idx = _notes.FindIndex(n => n.Id == id);
        if (idx < 0) return;
        var hex = $"#{picker.SelectedColor.R:X2}{picker.SelectedColor.G:X2}{picker.SelectedColor.B:X2}";
        _notes[idx] = _notes[idx] with { CustomColorHex = hex };
        PersistNotes();
        RefreshNotesGrid();
        RefreshTabBar();
    }

    private void ColorPickerCancel_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerOverlay.Visibility = Visibility.Collapsed;
    }

}
