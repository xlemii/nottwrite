# nottwrite

Turn your handwriting into a real, installable font. Draw each character once, then export a `.ttf` you can use in Word, the browser, or anywhere else — or write notes and pages directly inside the app.

---

## Features

**Fonts**
- Draw every character on a grid canvas with a pressure-sensitive brush
- Export a genuine TrueType `.ttf` font built from your strokes — install it system-wide and type your own handwriting
- Up to 3 variants per character for natural-looking repetition
- Six brush shapes: Round, Flat, Calligraphy, Triangle, Ink, Chisel
- Per-character ink color, adjustable taper, size, opacity and smoothing

**Type & Notes**
- Type mode renders text using your drawn characters (or any installed system font) with realistic randomisation
- Notes library with multi-tab editing, favorites, tags, full-text search and drag-and-drop ordering
- Auto-save with rotating backups; per-note undo/redo history
- Paper styles (Lines / Dots / Clear), alignment, bold / italic / underline / strikethrough
- Voice dictation via `faster-whisper`

**Export & UI**
- Export pages as PNG, JPG, PDF, SVG, or a font (`.ttf` / `.otf` desktop, `.woff2` web)
- Import / export full template bundles (`.nwt`)
- Four built-in themes, frameless dark UI, toast notifications

---

## Tech stack

| Layer | Technology |
|---|---|
| UI | WPF (.NET 10, C#) |
| Rendering | SkiaSharp 2.88 (`SkiaSharp.Views.WPF`) |
| Font export | Custom pure-C# TrueType writer (`Core/TrueTypeFontBuilder`) |
| Voice input | Python 3 + `faster-whisper` |
| Data | JSON per character (`templates/<name>/<char>.json`) |

---

## Requirements

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- *Optional (voice input)* — Python 3.10+ with `pip install faster-whisper sounddevice scipy numpy`
- *Optional (scan import)* — Python 3.10+ with `pip install opencv-python scikit-image numpy`

---

## Getting started

```bash
git clone https://github.com/lemiis/nottwrite.git
cd nottwrite
dotnet run --project src/nottwrite.UI
```

---

## Usage

**Draw characters** — Edit mode: select a cell, draw on the canvas (strokes save automatically), tune the brush in the left panel. Right-click a cell to cycle variants.

**Export a font** — Save As → **Font (.ttf)**. Install the file and use your handwriting in any app.

**Write text** — Type mode: type on the keyboard to compose with your characters; adjust size, spacing, paper style and formatting; export via Save As.

**Take notes** — Notes mode: create notebooks, organise with tags and favorites, search across all notes, reorder by dragging.

**Dictate** — Type mode → microphone button: pick duration and language, then speak.

---

## Project structure

```
nottwrite/
├── src/
│   ├── nottwrite.UI/      # WPF app (XAML + partial-class C#)
│   └── nottwrite.Core/    # Models + TrueType font writer
├── templates/                          # Per-template character JSON
├── stt.py                              # Voice-input script
├── tests/
└── nottwrite.slnx
```

---

## Character file format

Each character is a JSON file at `templates/<template>/<char>.json`:

```json
{
  "width": 120,
  "height": 200,
  "baseline": 160,
  "color": "#E8E8E8",
  "strokes": [
    { "points": [ { "x": 30, "y": 40 }, { "x": 60, "y": 80 } ] }
  ]
}
```

Full templates export as a `.nwt` bundle from the Template panel in Edit mode.

---

## License

MIT
