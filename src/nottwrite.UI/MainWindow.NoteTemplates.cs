using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace nottwrite.UI;

public partial class MainWindow
{
    private record NoteTemplate(string Key, string Icon, string Name, string Desc, PaperStyle? Paper = null);

    private NoteTemplate[] NoteTemplates => new[]
    {
        new NoteTemplate("blank",   "📄", "Blank",       "Empty note"),
        new NoteTemplate("daily",   "📅", "Daily note",  "Date, tasks, notes",     PaperStyle.Lines),
        new NoteTemplate("meeting", "👥", "Meeting",     "Agenda + action items",  PaperStyle.Lines),
        new NoteTemplate("todo",    "✅", "To-do list",  "Checklist",              PaperStyle.Lines),
        new NoteTemplate("planner", "🗓", "Planner",      "Grid + day sections",    PaperStyle.Dots),
        new NoteTemplate("journal", "✍",  "Journal",     "Free-writing prompt",    PaperStyle.Clear),
    };

    private string BuildTemplate(string key, out string title)
    {
        string d = DateTime.Now.ToString("dddd, d MMMM yyyy");
        switch (key)
        {
            case "daily":
                title = DateTime.Now.ToString("yyyy-MM-dd");
                return $"# {d}\n\n## Tasks\n[ ] \n[ ] \n\n## Notes\n- ";
            case "meeting":
                title = "Meeting";
                return $"# Meeting\n{d}\n\nAttendees: \n\n## Agenda\n- \n- \n\n## Action items\n[ ] \n[ ] ";
            case "todo":
                title = "To-do";
                return "# To-do\n[ ] \n[ ] \n[ ] ";
            case "planner":
                title = DateTime.Now.ToString("yyyy-MM-dd") + " Plan";
                return $"# {d}\n\n## Morning\n- \n\n## Afternoon\n- \n\n## Evening\n- \n\n## Top priority\n[ ] ";
            case "journal":
                title = DateTime.Now.ToString("yyyy-MM-dd") + " Journal";
                return $"# {d}\n\nToday I... ";
            default:
                title = "Untitled";
                return "";
        }
    }

    private void ShowNewNoteOverlay()
    {
        NewNotePanel.Children.Clear();
        foreach (var t in NoteTemplates)
        {
            string key = t.Key;
            var icon = new TextBlock { Text = t.Icon, FontSize = 22,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            var name = new TextBlock { Text = t.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("PrimaryText"), HorizontalAlignment = HorizontalAlignment.Center };
            var desc = new TextBlock { Text = t.Desc, FontSize = 11,
                Foreground = GetBrush("SecondaryText"), HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(icon); stack.Children.Add(name); stack.Children.Add(desc);

            var tile = new Border
            {
                Width = 124, Height = 110, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(6),
                Background = GetBrush("Surface2Bg"),
                BorderBrush = GetBrush("AppBorderBrush"), BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Child = stack,
            };
            tile.MouseEnter += (_, _) => { tile.BorderBrush = GetBrush("AccentBrush"); tile.Background = GetBrush("NavActiveBg"); };
            tile.MouseLeave += (_, _) => { tile.BorderBrush = GetBrush("AppBorderBrush"); tile.Background = GetBrush("Surface2Bg"); };
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; PickNoteTemplate(key); };
            NewNotePanel.Children.Add(tile);
        }
        NewNoteOverlay.Visibility = Visibility.Visible;
    }

    private void PickNoteTemplate(string key)
    {
        NewNoteOverlay.Visibility = Visibility.Collapsed;
        string body = BuildTemplate(key, out string title);
        var tpl = NoteTemplates.FirstOrDefault(t => t.Key == key);
        CreateNote(title, body);
        if (tpl?.Paper is { } ps) SetPaperStyle(ps);   // template sets the paper style
    }

    private void NewNoteCancel_Click(object sender, RoutedEventArgs e)
        => NewNoteOverlay.Visibility = Visibility.Collapsed;
}
