# nottwrite

A desktop application for creating handwriting-style fonts from your own handwriting. Draw each letter of the alphabet, then type text that renders in your custom font — complete with natural variations, paper styles, and voice input.

![nottwrite screenshot](docs/screenshot.png)

---

## Features

- **Edit mode** — draw every character of the alphabet on a grid canvas using a pressure-sensitive brush
- **Type mode** — compose text that renders using your drawn characters, with realistic handwriting randomisation
- **Multiple brush shapes** — Round, Flat, Calligraphy, Triangle, Ink, Chisel
- **Per-character color** — assign a unique ink color to each drawn character
- **Paper styles** — Lined, Dotted, or Clear background in Type mode
- **Character variants** — store up to 3 variants per letter for natural-looking repetition
- **Voice input (STT)** — dictate text using `faster-whisper`; choose language per session
- **Export** — save your composition as PNG, JPG, PDF, or SVG; export/import full template bundles (`.nwt`)
- **Undo / Redo** — full stroke-level history in Edit mode
- **Templates** — create, rename and switch between multiple font templates
- **Fluent-style dark UI** — frameless window, hover animations, Material-inspired cards

---

## Tech stack

| Layer | Technology |
|---|---|
| UI | WPF (.NET 10, C#) |
| Rendering | SkiaSharp 2.88 (`SkiaSharp.Views.WPF`) |
| Voice input | Python 3 + `faster-whisper` |
| Data | JSON per character (`templates/<name>/<char>.json`) |

---

## Requirements

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- *(Optional — voice input)* Python 3.10+ with:
  ```
  pip install faster-whisper sounddevice scipy numpy
  ```

---

## Getting started

```bash
git clone https://github.com/lemiis/nottwrite.git
cd nottwrite
dotnet run --project src/HandwritingFontCreator.UI
```

---

## How to use

### Drawing characters (Edit mode)

1. Click a character cell in the grid to select it
2. Draw on the canvas — strokes are saved automatically
3. Adjust brush shape, size, taper, opacity and smoothing in the left panel
4. Use the inline color picker to assign a custom ink color per character
5. Right-click a cell to cycle through variants (up to 3 per character)

### Writing text (Type mode)

1. Click the **Type** tab
2. Type on your keyboard — characters are placed using your drawn templates
3. Use sliders to adjust font size, letter spacing, word spacing, line height
4. Choose a paper style (Lines / Dots / Clear) and configure its density
5. Use **Bold** / **Italic** toggles for thicker / slanted rendering
6. Click **Save As** → PNG / JPG / PDF to export your page

### Voice input

1. In Type mode, click the **🎤 Voice Input** button
2. Select duration and language from the dropdowns below
3. Speak — transcribed words are inserted at the cursor position

---

## Project structure

```
nottwrite/
├── src/
│   ├── HandwritingFontCreator.UI/      # WPF app (XAML + C#)
│   └── HandwritingFontCreator.Core/    # Models (StrokeData, etc.)
├── templates/                          # Per-template character JSON files
│   └── Default/
├── stt.py                              # Python voice-input script
├── tests/
└── HandwritingFontCreator.slnx
```

---

## Template file format

Each character is stored as a JSON file at `templates/<template>/<char>.json`:

```json
{
  "width": 120,
  "height": 200,
  "baseline": 160,
  "color": "#E8E8E8",
  "strokes": [
    {
      "points": [
        { "x": 30, "y": 40 },
        { "x": 60, "y": 80 }
      ]
    }
  ]
}
```

Full templates can be exported as a `.nwt` bundle (JSON) via **Export** in the top bar.

---

## License

MIT
