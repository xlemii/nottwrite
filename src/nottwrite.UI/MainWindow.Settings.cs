using System.IO;
using System.Text.Json;

namespace nottwrite.UI;

public partial class MainWindow
{
    // ── Unified app settings (settings.json) ──────────────────────
    private sealed class AppSettings
    {
        public string Theme           { get; set; } = "dark";
        public bool   Tilt            { get; set; } = true;
        public bool   AutoSave        { get; set; } = true;
        public int    AutoSaveMinutes { get; set; } = 5;
        public bool   OnboardingSeen  { get; set; } = false;
        public string? CharCategories { get; set; } = null;
    }

    public bool OnboardingSeen;

    private static string SettingsDir =>
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;

    // distinct from the pen/brush "settings.json" written elsewhere — avoid collision
    private static string AppSettingsPath => Path.Combine(SettingsDir, "preferences.json");

    private static readonly JsonSerializerOptions _settingsJson =
        new() { WriteIndented = true };

    /// Load settings.json into the live fields. If absent, migrate the
    /// legacy theme.txt / tilt.txt / autosave.txt files, then remove them.
    private void LoadSettings()
    {
        AppSettings s;
        try
        {
            if (File.Exists(AppSettingsPath))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettingsPath)) ?? new();
            else
                s = MigrateLegacySettings();
        }
        catch { s = new(); }

        _currentThemeId = string.IsNullOrWhiteSpace(s.Theme) ? "dark" : s.Theme;
        TiltEnabled     = s.Tilt;
        AutoSaveEnabled = s.AutoSave;
        AutoSaveMinutes = s.AutoSaveMinutes > 0 ? s.AutoSaveMinutes : 5;
        OnboardingSeen  = s.OnboardingSeen;
        SetEnabledCatsCsv(s.CharCategories);
    }

    /// Persist the current settings to settings.json.
    public void SaveSettings()
    {
        var s = new AppSettings
        {
            Theme           = _currentThemeId,
            Tilt            = TiltEnabled,
            AutoSave        = AutoSaveEnabled,
            AutoSaveMinutes = AutoSaveMinutes,
            OnboardingSeen  = OnboardingSeen,
            CharCategories  = EnabledCatsCsv,
        };
        try { File.WriteAllText(AppSettingsPath, JsonSerializer.Serialize(s, _settingsJson)); }
        catch { }
    }

    private static AppSettings MigrateLegacySettings()
    {
        var s = new AppSettings();
        try
        {
            string theme = Path.Combine(SettingsDir, "theme.txt");
            if (File.Exists(theme)) s.Theme = File.ReadAllText(theme).Trim();

            // legacy .txt present = returning user → skip onboarding
            s.OnboardingSeen = File.Exists(theme)
                || File.Exists(Path.Combine(SettingsDir, "tilt.txt"))
                || File.Exists(Path.Combine(SettingsDir, "autosave.txt"));

            string tilt = Path.Combine(SettingsDir, "tilt.txt");
            if (File.Exists(tilt)) s.Tilt = File.ReadAllText(tilt).Trim() != "0";

            string auto = Path.Combine(SettingsDir, "autosave.txt");
            if (File.Exists(auto))
            {
                var parts = File.ReadAllText(auto).Trim().Split(':');
                s.AutoSave = parts[0] != "0";
                if (parts.Length > 1 && int.TryParse(parts[1], out int m) && m > 0)
                    s.AutoSaveMinutes = m;
            }

            // write merged file, then delete legacy ones
            File.WriteAllText(AppSettingsPath, JsonSerializer.Serialize(s, _settingsJson));
            foreach (var f in new[] { theme, tilt, auto })
                if (File.Exists(f)) File.Delete(f);
        }
        catch { }
        return s;
    }
}
