using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace HandwritingFontCreator.UI;

public record HotkeyDef(string Id, string Name, string Description, Key DefaultKey, ModifierKeys DefaultMods);

public class HotkeyManager
{
    private readonly Dictionary<string, (Key Key, ModifierKeys Mods)> _bindings = [];
    private static readonly string SettingsPath = Path.Combine(
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
        "hotkeys.json");

    public static readonly IReadOnlyList<HotkeyDef> Definitions =
    [
        new("SwitchMode",   "Switch Mode",     "Toggle Edit / Type mode",              Key.Tab,    ModifierKeys.None),
        new("ToggleEraser", "Eraser",          "Toggle eraser on/off (Edit mode)",     Key.E,      ModifierKeys.None),
        new("NavLeft",      "Navigate Left",   "Move to previous character (Edit mode)", Key.Left,  ModifierKeys.None),
        new("NavRight",     "Navigate Right",  "Move to next character (Edit mode)",   Key.Right,  ModifierKeys.None),
        new("Undo",         "Undo",            "Undo last action",                     Key.Z,      ModifierKeys.Control),
        new("Redo",         "Redo",            "Redo last action",                     Key.Y,      ModifierKeys.Control),
        new("Bold",         "Bold",            "Toggle bold (Type mode)",              Key.B,      ModifierKeys.Control),
        new("Italic",       "Italic",          "Toggle italic (Type mode)",            Key.I,      ModifierKeys.Control),
        new("ClearCanvas",  "Clear Canvas",    "Erase all strokes (Edit mode)",        Key.Delete, ModifierKeys.Control),
        new("Save",         "Save",            "Save current template",                Key.S,      ModifierKeys.Control),
        new("VoiceInput",   "Voice Input",     "Start / stop voice dictation",         Key.M,      ModifierKeys.Control),
        new("ZoomIn",       "Zoom In",         "Increase font size (Type mode)",       Key.OemPlus,  ModifierKeys.Control),
        new("ZoomOut",      "Zoom Out",        "Decrease font size (Type mode)",       Key.OemMinus, ModifierKeys.Control),
    ];

    public HotkeyManager() => ResetAll();

    public void ResetAll()
    {
        _bindings.Clear();
        foreach (var d in Definitions)
            _bindings[d.Id] = (d.DefaultKey, d.DefaultMods);
    }

    public void Set(string id, Key key, ModifierKeys mods)
    {
        _bindings[id] = (key, mods);
        Save();
    }

    public bool Matches(string id, Key key, ModifierKeys mods)
    {
        if (!_bindings.TryGetValue(id, out var b)) return false;
        return b.Key == key && b.Mods == mods;
    }

    public string Label(string id)
    {
        if (!_bindings.TryGetValue(id, out var b)) return "—";
        return FormatLabel(b.Key, b.Mods);
    }

    public static HotkeyDef GetDef(string id) =>
        Definitions.First(d => d.Id == id);

    public static string FormatLabel(Key key, ModifierKeys mods)
    {
        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Shift)   != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Alt)     != 0) parts.Add("Alt");
        parts.Add(KeyToString(key));
        return string.Join("+", parts);
    }

    private static string KeyToString(Key key) => key switch
    {
        Key.OemPlus  => "+",
        Key.OemMinus => "-",
        Key.OemPeriod => ".",
        Key.OemComma  => ",",
        Key.Space     => "Space",
        Key.Return    => "Enter",
        Key.Back      => "Backspace",
        _             => key.ToString()
    };

    public void Load()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            var json = File.ReadAllText(SettingsPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
            if (dict == null) return;
            foreach (var (id, parts) in dict)
            {
                if (parts.Length == 2
                    && Enum.TryParse<Key>(parts[0], out var key)
                    && Enum.TryParse<ModifierKeys>(parts[1], out var mods))
                    _bindings[id] = (key, mods);
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var dict = _bindings.ToDictionary(
                kv => kv.Key,
                kv => new[] { kv.Value.Key.ToString(), kv.Value.Mods.ToString() });
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
