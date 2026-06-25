using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace nottwrite.UI;

public partial class MainWindow
{
    public string _lang = "en";

    // Remember each element's original (authored, English) text so switching
    // language is idempotent and reversible.
    private readonly ConditionalWeakTable<DependencyObject, string> _origText = new();

    private static readonly Dictionary<string, string> _pl = new()
    {
        // nav
        ["Notes"] = "Notatki", ["Type"] = "Pisz", ["Edit"] = "Edytuj",
        ["personal library"] = "biblioteka",
        ["write on paper"] = "pisz na kartce",
        ["draw characters"] = "rysuj znaki",
        ["Export"] = "Eksport",
        // section labels
        ["TEMPLATE"] = "SZABLON", ["BRUSH"] = "PĘDZEL", ["BRUSH SETTINGS"] = "USTAWIENIA PĘDZLA",
        ["TYPOGRAPHY"] = "TYPOGRAFIA", ["NATURAL FEEL"] = "NATURALNOŚĆ", ["PAPER"] = "PAPIER",
        ["PAGE SIZE"] = "ROZMIAR STRONY", ["STYLE"] = "STYL", ["CHAR COLOR"] = "KOLOR ZNAKU",
        ["LIBRARY"] = "BIBLIOTEKA", ["FAVORITES"] = "ULUBIONE", ["RECENTLY OPENED"] = "OSTATNIO OTWARTE",
        ["ALL NOTES"] = "WSZYSTKIE NOTATKI", ["EXPORT PAGE AS"] = "EKSPORTUJ STRONĘ JAKO",
        ["FONT"] = "FONT", ["CHARACTERS IN YOUR FONT"] = "ZNAKI W TWOIM FONCIE",
        ["LINKED FROM"] = "LINKOWANE Z",
        // brush
        ["Round"] = "Okrągły", ["Flat"] = "Płaski", ["Callig."] = "Kaligr.",
        ["Triangle"] = "Trójkąt", ["Ink"] = "Atrament", ["Chisel"] = "Dłuto",
        ["More options"] = "Więcej opcji", ["Fewer options"] = "Mniej opcji",
        ["Eraser"] = "Gumka", ["Ghost"] = "Wzorzec", ["Guides"] = "Linie", ["Clear"] = "Wyczyść",
        ["Focus"] = "Skupienie",
        // sliders / fields
        ["Size"] = "Rozmiar", ["Opacity"] = "Krycie", ["Taper"] = "Zwężenie",
        ["Tip Roundness"] = "Krągłość", ["Smoothing"] = "Wygładzanie", ["Pressure"] = "Nacisk",
        ["Font Size"] = "Rozmiar", ["Letter Spacing"] = "Odstęp liter",
        ["Word Spacing"] = "Odstęp słów", ["Line Height"] = "Wysokość linii",
        ["Stroke Width"] = "Grubość", ["Rotation"] = "Obrót", ["Jitter Y"] = "Drżenie Y",
        ["Line Spacing"] = "Odstęp linii", ["Thickness"] = "Grubość",
        ["Dot Spacing"] = "Odstęp kropek", ["Dot Size"] = "Rozmiar kropki",
        ["Format"] = "Format", ["Progress"] = "Postęp", ["Callig Angle"] = "Kąt kaligrafii",
        // template / scan
        ["Export .ttf"] = "Eksport .ttf", ["Import"] = "Import",
        ["Sheet"] = "Arkusz", ["Scan"] = "Skan", ["New"] = "Nowy",
        ["Lines"] = "Linie", ["Dots"] = "Kropki",
        // paper buttons
        ["Voice"] = "Głos",
        // overlays / dialogs
        ["Save changes?"] = "Zapisać zmiany?", ["Delete note?"] = "Usunąć notatkę?",
        ["Rename note"] = "Zmień nazwę", ["Edit tags"] = "Edytuj tagi", ["New note"] = "Nowa notatka",
        ["Cancel"] = "Anuluj", ["Save"] = "Zapisz", ["Delete"] = "Usuń", ["Rename"] = "Zmień nazwę",
        ["Don't save"] = "Nie zapisuj", ["Export font"] = "Eksportuj font",
        ["Start blank or from a template"] = "Zacznij od zera lub z szablonu",
        ["Separate tags with commas"] = "Oddziel tagi przecinkami",
        ["Cover color"] = "Kolor okładki",
        // empty / search / status
        ["No notes yet"] = "Brak notatek",
        ["Create your first notebook to start writing by hand."] =
            "Utwórz pierwszy notes, aby pisać ręcznie.",
        ["Saved"] = "Zapisano", ["Saving…"] = "Zapisywanie…",
        // settings
        ["Settings"] = "Ustawienia", ["Hotkeys"] = "Skróty", ["Themes"] = "Motywy",
        ["General"] = "Ogólne", ["Language"] = "Język",
        ["VISUAL EFFECTS"] = "EFEKTY WIZUALNE", ["AUTO-SAVE"] = "AUTOZAPIS",
        // font range overlay
        ["Choose which characters to include. Only drawn ones are exported."] =
            "Wybierz znaki do eksportu. Tylko narysowane trafią do fontu.",
        // mode titles + dynamic
        ["ALPHABET EDIT"] = "EDYCJA ALFABETU", ["MY NOTES"] = "MOJE NOTATKI",
        ["Ctrl+scroll to zoom"] = "Ctrl+scroll = powiększenie",
        ["font complete ✓"] = "font gotowy ✓",
        // settings window
        ["Interface language"] = "Język interfejsu",
        ["3D card tilt effect"] = "Efekt przechyłu kart 3D",
        ["Notebooks tilt in 3D as you hover over them"] =
            "Notesy przechylają się w 3D pod kursorem",
        ["Auto-save notes"] = "Autozapis notatek",
        ["Automatically save all open notes periodically"] =
            "Automatycznie zapisuj otwarte notatki co jakiś czas",
        ["Save interval"] = "Częstotliwość zapisu",
        // onboarding
        ["Welcome to nottwrite"] = "Witaj w nottwrite",
        ["nottwrite turns your handwriting into a font — and lets you write notes and pages in it."] =
            "nottwrite zamienia Twoje pismo w font — i pozwala pisać nim notatki i strony.",
        ["What would you like to do?"] = "Co chcesz zrobić?",
        ["You can switch anytime from the sidebar. Pick where to start:"] =
            "Możesz przełączać w panelu bocznym. Wybierz, od czego zacząć:",
        ["Write by hand"] = "Pisz ręcznie",
        ["Take notes and pages in your own handwriting"] =
            "Twórz notatki i strony własnym pismem",
        ["Make a font"] = "Stwórz font",
        ["Draw your letters once, export an installable .ttf"] =
            "Narysuj litery raz, wyeksportuj instalowalny .ttf",
        ["Next"] = "Dalej", ["Get started"] = "Zaczynamy", ["Skip"] = "Pomiń",
        // theme names
        ["Dark"] = "Ciemny", ["Light"] = "Jasny", ["Pink"] = "Różowy", ["Blue"] = "Niebieski",
        ["High Contrast"] = "Wysoki kontrast",
        // command palette (names + hints)
        ["Go to Notes"] = "Przejdź do Notatek", ["Go to Type"] = "Przejdź do Pisania",
        ["Go to Edit"] = "Przejdź do Edycji", ["Personal library"] = "Biblioteka",
        ["Write on paper"] = "Pisz na kartce", ["Draw characters"] = "Rysuj znaki",
        ["New folder"] = "Nowy folder",
        ["Create a notebook"] = "Utwórz notes", ["Create a category"] = "Utwórz kategorię",
        ["Export font (.ttf)"] = "Eksportuj font (.ttf)", ["Build installable font"] = "Zbuduj instalowalny font",
        ["Export as PNG"] = "Eksportuj jako PNG", ["Export as PDF"] = "Eksportuj jako PDF",
        ["Export as SVG"] = "Eksportuj jako SVG", ["Save current page"] = "Zapisz bieżącą stronę",
        ["Import font / template"] = "Importuj font / szablon", ["Edit an existing font"] = "Edytuj istniejący font",
        ["Hotkeys, themes, general"] = "Skróty, motywy, ogólne",
        ["Bold"] = "Pogrubienie", ["Italic"] = "Kursywa", ["Toggle bold"] = "Przełącz pogrubienie",
        ["Toggle italic"] = "Przełącz kursywę", ["Undo"] = "Cofnij", ["Undo last change"] = "Cofnij ostatnią zmianę",
        ["Voice input"] = "Dyktowanie", ["Dictate text"] = "Dyktuj tekst",
        // common toasts
        ["Image added"] = "Dodano obraz", ["Could not read image"] = "Nie można odczytać obrazu",
        ["Note deleted"] = "Notatka usunięta",
        ["Template sheet saved — print it, write each letter, then Scan"] =
            "Arkusz zapisany — wydrukuj, wypisz litery, potem Skan",
        ["Processing scan…"] = "Przetwarzanie skanu…", ["Working…"] = "Pracuję…",
        ["No characters in the set"] = "Brak znaków w zestawie",
        ["No drawn characters in the selected range"] = "Brak narysowanych znaków w wybranym zakresie",
        // keyboard shortcuts screen
        ["Keyboard shortcuts"] = "Skróty klawiszowe", ["Show all shortcuts"] = "Pokaż wszystkie skróty",
        ["GENERAL"] = "OGÓLNE", ["EDIT"] = "EDYCJA", ["TYPE & NOTES"] = "PISANIE I NOTATKI",
        ["Command palette"] = "Paleta komend", ["Switch mode"] = "Przełącz tryb",
        ["Previous character"] = "Poprzedni znak", ["Next character"] = "Następny znak",
        ["Zoom canvas"] = "Powiększ płótno", ["Find in note"] = "Szukaj w notatce",
        ["Underline"] = "Podkreślenie", ["Open note"] = "Otwórz notatkę", ["Redo"] = "Ponów",
    };

    public string T(string en) => _lang == "pl" && _pl.TryGetValue(en, out var v) ? v : en;

    public void SetLanguage(string lang)
    {
        _lang = lang == "pl" ? "pl" : "en";
        SaveSettings();
        ApplyLanguage();
    }

    public void ApplyLanguage()
    {
        LocalizeTree(this);
        // dynamic, code-set strings
        UpdateAlphabetProgress();   // re-applies EditNavSubtitle via T()
        RefreshModeTitle();
        _commands.Clear();          // command palette rebuilds with new language
        if (_appMode == AppMode.Notes) RefreshNotesGrid();   // note cards / labels
    }

    // Localize another window's tree (e.g. Settings) with the same dictionary.
    public void LocalizeWindow(DependencyObject root) => LocalizeTree(root);

    private void RefreshModeTitle()
    {
        if (CenterPanelTitle == null) return;
        CenterPanelTitle.Text = _appMode switch
        {
            AppMode.Edit  => T("ALPHABET EDIT") + "  ·  " + T("Ctrl+scroll to zoom"),
            AppMode.Type  => T("PAPER"),
            _             => T("MY NOTES"),
        };
    }

    private void LocalizeTree(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            LocalizeElement(d);
            LocalizeTree(d);
        }
        // Popups aren't always walked as logical children of their content host.
        if (root is Popup pop && pop.Child is DependencyObject pc)
        {
            LocalizeElement(pc);
            LocalizeTree(pc);
        }
    }

    private void LocalizeElement(DependencyObject d)
    {
        switch (d)
        {
            case TextBlock tb:
                tb.Text = Translate(tb, tb.Text);
                break;
            case ContentControl cc when cc.Content is string s:
                cc.Content = Translate(cc, s);
                break;
        }
    }

    private string Translate(DependencyObject el, string current)
    {
        if (!_origText.TryGetValue(el, out var orig))
        {
            orig = current ?? "";
            _origText.Add(el, orig);
        }
        return T(orig);
    }
}
