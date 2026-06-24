using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nottwrite.UI;

public partial class MainWindow
{
    private string? _pendingRenameFolderId;

    // ── Sidebar folder list ───────────────────────────────────────
    private void RefreshFolderList()
    {
        if (FolderListPanel == null) return;
        FolderListPanel.Children.Clear();

        FolderListPanel.Children.Add(MakeFolderRow(
            id: null, "\U0001F3E0", "Home", _notes.Count));

        foreach (var f in _folders)
        {
            int count = _notes.Count(n => n.FolderId == f.Id);
            FolderListPanel.Children.Add(MakeFolderRow(f.Id, "\U0001F4C1", f.Name, count));
        }
    }

    private FrameworkElement MakeFolderRow(string? id, string icon, string name, int count)
    {
        bool active = _activeFolderId == id;

        var iconTb = new TextBlock { Text = icon, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
        var nameTb = new TextBlock
        {
            Text = name, FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = active ? GetBrush("PrimaryText") : GetBrush("SecondaryText"),
            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
        };
        var countTb = new TextBlock
        {
            Text = count.ToString(), FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Foreground = GetBrush("SecondaryText"), Opacity = 0.7,
        };

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(iconTb, Dock.Left);
        DockPanel.SetDock(countTb, Dock.Right);
        dock.Children.Add(iconTb);
        dock.Children.Add(countTb);
        dock.Children.Add(nameTb);

        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding      = new Thickness(10, 7, 10, 7),
            Margin       = new Thickness(0, 1, 0, 1),
            Cursor       = Cursors.Hand,
            Background    = active ? GetBrush("NavActiveBg") : new SolidColorBrush(Colors.Transparent),
            BorderBrush   = active ? GetBrush("AccentBrush") : new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(active ? 1 : 0),
            Child        = dock,
            AllowDrop    = id != null,    // drop a note onto a folder to move it
            Tag          = id,
        };

        row.MouseLeftButtonUp += (_, _) => SelectFolder(id);

        if (id != null)
        {
            string fid = id;
            // context menu: rename / delete
            var cm = new ContextMenu();
            var rn = new MenuItem { Header = "Rename" };
            rn.Click += (_, _) => OpenFolderRename(fid);
            var del = new MenuItem { Header = "Delete folder" };
            del.Click += (_, _) => DeleteFolder(fid);
            cm.Items.Add(rn);
            cm.Items.Add(del);
            row.ContextMenu = cm;

            // accept dropped notes
            row.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
                    ? DragDropEffects.Move : DragDropEffects.None;
                row.Background = GetBrush("NavActiveBg");
                e.Handled = true;
            };
            row.DragLeave += (_, _) =>
                row.Background = active ? GetBrush("NavActiveBg") : new SolidColorBrush(Colors.Transparent);
            row.Drop += (_, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.StringFormat))
                    MoveNoteToFolder((string)e.Data.GetData(DataFormats.StringFormat), fid);
                e.Handled = true;
            };
        }
        return row;
    }

    private void SelectFolder(string? id)
    {
        _activeFolderId = id;
        RefreshFolderList();
        RefreshNotesGrid();
    }

    // ── Folder CRUD ───────────────────────────────────────────────
    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string baseName = "New folder";
        string name = baseName;
        int i = 2;
        while (_folders.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {i++}";

        var folder = new NoteFolder(Guid.NewGuid().ToString("N"), name);
        _folders.Add(folder);
        PersistNotes();
        RefreshFolderList();
        OpenFolderRename(folder.Id);   // let the user name it right away
    }

    private void OpenFolderRename(string folderId)
    {
        var f = _folders.FirstOrDefault(x => x.Id == folderId);
        if (f == null) return;
        _pendingRenameFolderId = folderId;
        _pendingRenameId = null;
        RenameBox.Text = f.Name;
        RenameOverlay.Visibility = Visibility.Visible;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    private void DeleteFolder(string folderId)
    {
        int fIdx = _folders.FindIndex(f => f.Id == folderId);
        if (fIdx < 0) return;
        var removed = _folders[fIdx];
        var affected = _notes.Where(n => n.FolderId == folderId).Select(n => n.Id).ToList();
        bool wasActive = _activeFolderId == folderId;

        // notes inside become unfiled (move to Home), folder removed
        for (int i = 0; i < _notes.Count; i++)
            if (_notes[i].FolderId == folderId)
                _notes[i] = _notes[i] with { FolderId = null };
        _folders.RemoveAt(fIdx);
        if (wasActive) _activeFolderId = null;
        PersistNotes();
        RefreshFolderList();
        RefreshNotesGrid();

        ShowToast("Folder deleted", ToastKind.Info, actionLabel: "Undo", action: () =>
        {
            _folders.Insert(Math.Min(fIdx, _folders.Count), removed);
            foreach (var nid in affected)
            {
                int idx = _notes.FindIndex(n => n.Id == nid);
                if (idx >= 0) _notes[idx] = _notes[idx] with { FolderId = folderId };
            }
            if (wasActive) _activeFolderId = folderId;
            PersistNotes();
            RefreshFolderList();
            RefreshNotesGrid();
        });
    }

    private void MoveNoteToFolder(string noteId, string? folderId)
    {
        int idx = _notes.FindIndex(n => n.Id == noteId);
        if (idx < 0) return;
        if (_notes[idx].FolderId == folderId) return;
        _notes[idx] = _notes[idx] with { FolderId = folderId };
        PersistNotes();
        RefreshFolderList();
        RefreshNotesGrid();
        string dest = folderId == null ? "Home"
            : _folders.FirstOrDefault(f => f.Id == folderId)?.Name ?? "folder";
        ShowToast($"Moved to {dest}", ToastKind.Info);
    }
}
