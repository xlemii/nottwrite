using System.IO;
using System.Linq;
using System.Text;

namespace HandwritingFontCreator.Core;

/// <summary>
/// Minimal, spec-correct TrueType (.ttf) writer. Glyph outlines are supplied as
/// integer contours of on-curve points (straight segments) in font units with
/// Y pointing up and the baseline at y=0. Produces the required table set
/// (cmap, glyf, head, hhea, hmtx, loca, maxp, name, post, OS/2).
/// </summary>
public sealed class TrueTypeFontBuilder
{
    public sealed class Glyph
    {
        // Each contour is a closed loop of on-curve points (font units).
        public List<List<(int X, int Y)>> Contours { get; } = new();
        public int AdvanceWidth { get; set; } = 600;
    }

    private readonly int _unitsPerEm;
    private readonly int _ascender;
    private readonly int _descender;
    private readonly string _family;
    private readonly string _style;

    // glyph 0 must be .notdef
    private readonly List<Glyph> _glyphs = new();
    private readonly Dictionary<int, int> _cmap = new();   // unicode -> glyph index

    public TrueTypeFontBuilder(string family, string style = "Regular",
        int unitsPerEm = 1000, int ascender = 800, int descender = -200)
    {
        _family = family;
        _style = style;
        _unitsPerEm = unitsPerEm;
        _ascender = ascender;
        _descender = descender;
        _glyphs.Add(new Glyph { AdvanceWidth = unitsPerEm / 2 }); // .notdef (empty)
    }

    public void AddGlyph(int unicode, Glyph glyph)
    {
        _glyphs.Add(glyph);
        _cmap[unicode] = _glyphs.Count - 1;
    }

    public int GlyphCount => _glyphs.Count;

    // ───────────────────────── building ──────────────────────────

    public byte[] Build()
    {
        var glyf = BuildGlyfAndLoca(out var loca, out int maxPoints, out int maxContours);

        var tables = new Dictionary<string, byte[]>
        {
            ["head"] = BuildHead(),                 // checkSumAdjustment patched later
            ["hhea"] = BuildHhea(),
            ["maxp"] = BuildMaxp(maxPoints, maxContours),
            ["OS/2"] = BuildOs2(),
            ["hmtx"] = BuildHmtx(),
            ["cmap"] = BuildCmap(),
            ["loca"] = loca,
            ["glyf"] = glyf,
            ["name"] = BuildName(),
            ["post"] = BuildPost(),
        };

        return Assemble(tables);
    }

    // ───────────────────────── glyf / loca ───────────────────────

    private byte[] BuildGlyfAndLoca(out byte[] loca, out int maxPoints, out int maxContours)
    {
        maxPoints = 0;
        maxContours = 0;
        var glyfStream = new MemoryStream();
        var offsets = new List<uint> { 0 };

        foreach (var g in _glyphs)
        {
            var contours = g.Contours.Where(c => c.Count >= 2).ToList();
            if (contours.Count == 0)
            {
                offsets.Add((uint)glyfStream.Length);   // empty glyph: zero-length
                continue;
            }

            int totalPts = contours.Sum(c => c.Count);
            maxPoints = Math.Max(maxPoints, totalPts);
            maxContours = Math.Max(maxContours, contours.Count);

            int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
            foreach (var c in contours)
                foreach (var (x, y) in c)
                {
                    xMin = Math.Min(xMin, x); yMin = Math.Min(yMin, y);
                    xMax = Math.Max(xMax, x); yMax = Math.Max(yMax, y);
                }

            var w = new BeWriter();
            w.I16((short)contours.Count);
            w.I16((short)xMin); w.I16((short)yMin);
            w.I16((short)xMax); w.I16((short)yMax);

            int running = -1;
            foreach (var c in contours) { running += c.Count; w.U16((ushort)running); }
            w.U16(0); // instructionLength

            // flags — all on-curve (0x01), no compression
            foreach (var c in contours)
                foreach (var _ in c) w.U8(0x01);

            // x coords as int16 deltas
            int prev = 0;
            foreach (var c in contours)
                foreach (var (x, _) in c) { w.I16((short)(x - prev)); prev = x; }
            // y coords as int16 deltas
            prev = 0;
            foreach (var c in contours)
                foreach (var (_, y) in c) { w.I16((short)(y - prev)); prev = y; }

            var bytes = w.ToArray();
            glyfStream.Write(bytes, 0, bytes.Length);
            Pad4(glyfStream);
            offsets.Add((uint)glyfStream.Length);
        }

        // loca — long format
        var lw = new BeWriter();
        foreach (var off in offsets) lw.U32(off);
        loca = lw.ToArray();

        return glyfStream.ToArray();
    }

    // ───────────────────────── tables ────────────────────────────

    private byte[] BuildHead()
    {
        var w = new BeWriter();
        w.U32(0x00010000);            // version 1.0
        w.U32(0x00010000);            // fontRevision 1.0
        w.U32(0);                     // checkSumAdjustment (patched later)
        w.U32(0x5F0F3CF5);            // magicNumber
        w.U16(0b0000_0000_0000_1011); // flags
        w.U16((ushort)_unitsPerEm);
        w.I64(0); w.I64(0);           // created / modified
        w.I16(0); w.I16((short)_descender); // xMin/yMin (loose)
        w.I16((short)_unitsPerEm); w.I16((short)_ascender); // xMax/yMax
        w.U16(0);                     // macStyle
        w.U16(8);                     // lowestRecPPEM
        w.I16(2);                     // fontDirectionHint
        w.I16(1);                     // indexToLocFormat = long
        w.I16(0);                     // glyphDataFormat
        return w.ToArray();
    }

    private byte[] BuildHhea()
    {
        var w = new BeWriter();
        w.U32(0x00010000);
        w.I16((short)_ascender);
        w.I16((short)_descender);
        w.I16((short)(_unitsPerEm / 10)); // lineGap
        w.U16((ushort)_glyphs.Max(g => g.AdvanceWidth)); // advanceWidthMax
        w.I16(0);                     // minLeftSideBearing
        w.I16(0);                     // minRightSideBearing
        w.I16((short)_unitsPerEm);    // xMaxExtent
        w.I16(1); w.I16(0);           // caretSlopeRise/Run
        w.I16(0);                     // caretOffset
        w.I16(0); w.I16(0); w.I16(0); w.I16(0); // reserved
        w.I16(0);                     // metricDataFormat
        w.U16((ushort)_glyphs.Count); // numberOfHMetrics
        return w.ToArray();
    }

    private byte[] BuildMaxp(int maxPoints, int maxContours)
    {
        var w = new BeWriter();
        w.U32(0x00010000);
        w.U16((ushort)_glyphs.Count);
        w.U16((ushort)Math.Max(1, maxPoints));
        w.U16((ushort)Math.Max(1, maxContours));
        w.U16(0); w.U16(0);           // composite max
        w.U16(2);                     // maxZones
        w.U16(0); w.U16(0); w.U16(0); // twilight/storage/func
        w.U16(0); w.U16(0);           // instr defs
        w.U16(0); w.U16(0);           // component depth/elements
        return w.ToArray();
    }

    private byte[] BuildOs2()
    {
        var w = new BeWriter();
        w.U16(4);                     // version
        w.I16((short)(_unitsPerEm / 2)); // xAvgCharWidth
        w.U16(400);                   // usWeightClass (normal)
        w.U16(5);                     // usWidthClass (medium)
        w.U16(0);                     // fsType (installable)
        // subscript/superscript/strikeout (8 int16)
        short s = (short)(_unitsPerEm / 5);
        w.I16(s); w.I16(s); w.I16(0); w.I16((short)(_unitsPerEm / 7));
        w.I16(s); w.I16(s); w.I16(0); w.I16((short)(_unitsPerEm / 2));
        w.I16((short)(_unitsPerEm / 20)); // yStrikeoutSize
        w.I16((short)(_unitsPerEm / 4));  // yStrikeoutPosition
        w.I16(0);                     // sFamilyClass
        for (int i = 0; i < 10; i++) w.U8(0); // PANOSE
        w.U32(0); w.U32(0); w.U32(0); w.U32(0); // ulUnicodeRange1-4
        w.Tag("HFCG");                // achVendID
        w.U16(0x0040);                // fsSelection (REGULAR)
        int min = _cmap.Count > 0 ? _cmap.Keys.Min() : 0x20;
        int max = _cmap.Count > 0 ? _cmap.Keys.Max() : 0x20;
        w.U16((ushort)Math.Min(0xFFFF, min));   // usFirstCharIndex
        w.U16((ushort)Math.Min(0xFFFF, max));   // usLastCharIndex
        w.I16((short)_ascender);      // sTypoAscender
        w.I16((short)_descender);     // sTypoDescender
        w.I16((short)(_unitsPerEm / 10)); // sTypoLineGap
        w.U16((ushort)_ascender);     // usWinAscent
        w.U16((ushort)(-_descender)); // usWinDescent
        w.U32(0); w.U32(0);           // ulCodePageRange1-2
        w.I16((short)(_ascender * 7 / 10)); // sxHeight
        w.I16((short)(_ascender * 9 / 10)); // sCapHeight
        w.U16(0);                     // usDefaultChar
        w.U16(0x20);                  // usBreakChar
        w.U16(0);                     // usMaxContext
        return w.ToArray();
    }

    private byte[] BuildHmtx()
    {
        var w = new BeWriter();
        foreach (var g in _glyphs)
        {
            w.U16((ushort)g.AdvanceWidth);
            w.I16(0); // leftSideBearing
        }
        return w.ToArray();
    }

    private byte[] BuildCmap()
    {
        // format 4, platform 3 (Windows) encoding 1 (Unicode BMP)
        var entries = _cmap.Where(kv => kv.Key <= 0xFFFF)
                           .OrderBy(kv => kv.Key).ToList();

        // build segments: one per contiguous run, plus terminating 0xFFFF
        var segs = new List<(int start, int end, int delta)>();
        int i = 0;
        while (i < entries.Count)
        {
            int start = entries[i].Key;
            int gi = entries[i].Value;
            int delta = (gi - start) & 0xFFFF;
            int j = i;
            while (j + 1 < entries.Count
                   && entries[j + 1].Key == entries[j].Key + 1
                   && ((entries[j + 1].Value - entries[j + 1].Key) & 0xFFFF) == delta)
                j++;
            segs.Add((start, entries[j].Key, delta));
            i = j + 1;
        }
        segs.Add((0xFFFF, 0xFFFF, 1)); // required final segment

        int segCount = segs.Count;
        var sub = new BeWriter();
        sub.U16(4);                       // format
        sub.U16(0);                       // length (patched below)
        sub.U16(0);                       // language
        sub.U16((ushort)(segCount * 2));  // segCountX2
        int searchRange = 2 * (int)Math.Pow(2, Math.Floor(Math.Log2(segCount)));
        sub.U16((ushort)searchRange);
        sub.U16((ushort)Math.Log2(searchRange / 2));
        sub.U16((ushort)(segCount * 2 - searchRange));
        foreach (var s in segs) sub.U16((ushort)s.end);     // endCode
        sub.U16(0);                                          // reservedPad
        foreach (var s in segs) sub.U16((ushort)s.start);   // startCode
        foreach (var s in segs) sub.U16((ushort)s.delta);   // idDelta
        foreach (var _ in segs) sub.U16(0);                 // idRangeOffset (0 = use delta)
        var subBytes = sub.ToArray();
        // patch length
        subBytes[2] = (byte)(subBytes.Length >> 8);
        subBytes[3] = (byte)(subBytes.Length & 0xFF);

        // Mac (1,0) format 0 subtable — required by strict loaders (Font Viewer)
        var mac = new BeWriter();
        mac.U16(0);     // format 0
        mac.U16(262);   // length
        mac.U16(0);     // language
        for (int code = 0; code < 256; code++)
        {
            int g = (_cmap.TryGetValue(code, out var gi) && gi < 256) ? gi : 0;
            mac.U8(g);
        }
        var macBytes = mac.ToArray();

        // header: 2 encoding records
        const int headerLen = 4 + 2 * 8;        // version+numTables + 2 records
        int winOffset = headerLen;
        int macOffset = headerLen + subBytes.Length;

        var w = new BeWriter();
        w.U16(0);                     // version
        w.U16(2);                     // numTables
        w.U16(3); w.U16(1); w.U32((uint)winOffset);  // Windows Unicode BMP
        w.U16(1); w.U16(0); w.U32((uint)macOffset);  // Macintosh Roman
        var head = w.ToArray();

        var outBuf = new byte[head.Length + subBytes.Length + macBytes.Length];
        Buffer.BlockCopy(head, 0, outBuf, 0, head.Length);
        Buffer.BlockCopy(subBytes, 0, outBuf, head.Length, subBytes.Length);
        Buffer.BlockCopy(macBytes, 0, outBuf, head.Length + subBytes.Length, macBytes.Length);
        return outBuf;
    }

    private byte[] BuildName()
    {
        string full = $"{_family} {_style}".Trim();
        var records = new (int id, string val)[]
        {
            (1, _family), (2, _style), (3, $"{_family}-{_style}-HFCG"),
            (4, full), (6, $"{_family}-{_style}".Replace(" ", "")),
        };

        // each name record stored under both Macintosh (1,0,0) and Windows (3,1,0x409);
        // strict loaders require the Macintosh set. NameRecords sorted by
        // platform, encoding, language, nameID.
        var stringData = new MemoryStream();

        // (platformID, encodingID, languageID, nameID, len, off)
        var nameRecs = new List<(int plat, int enc, int lang, int id, int len, int off)>();

        foreach (var (id, val) in records)   // Mac records first (platform 1 < 3)
        {
            byte[] b = Encoding.ASCII.GetBytes(val);
            nameRecs.Add((1, 0, 0, id, b.Length, (int)stringData.Length));
            stringData.Write(b, 0, b.Length);
        }
        foreach (var (id, val) in records)   // Windows records
        {
            byte[] b = Encoding.BigEndianUnicode.GetBytes(val);
            nameRecs.Add((3, 1, 0x0409, id, b.Length, (int)stringData.Length));
            stringData.Write(b, 0, b.Length);
        }

        var w = new BeWriter();
        w.U16(0);                                  // format
        w.U16((ushort)nameRecs.Count);             // count
        w.U16((ushort)(6 + nameRecs.Count * 12));  // stringOffset
        foreach (var (plat, enc, lang, id, len, off) in nameRecs)
        {
            w.U16((ushort)plat); w.U16((ushort)enc); w.U16((ushort)lang);
            w.U16((ushort)id);
            w.U16((ushort)len);
            w.U16((ushort)off);
        }
        var sd = stringData.ToArray();
        var head = w.ToArray();
        var outBuf = new byte[head.Length + sd.Length];
        Buffer.BlockCopy(head, 0, outBuf, 0, head.Length);
        Buffer.BlockCopy(sd, 0, outBuf, head.Length, sd.Length);
        return outBuf;
    }

    private static byte[] BuildPost()
    {
        var w = new BeWriter();
        w.U32(0x00030000);   // version 3.0 (no glyph names)
        w.U32(0);            // italicAngle
        w.I16(-100);         // underlinePosition
        w.I16(50);           // underlineThickness
        w.U32(0);            // isFixedPitch
        w.U32(0); w.U32(0); w.U32(0); w.U32(0); // mem usage
        return w.ToArray();
    }

    // ───────────────────────── assembly ──────────────────────────

    private static byte[] Assemble(Dictionary<string, byte[]> tables)
    {
        // sfnt order recommended; tag order in directory must be alphabetical
        var tags = tables.Keys.OrderBy(t => t, StringComparer.Ordinal).ToList();
        int numTables = tags.Count;

        int searchRange = (int)Math.Pow(2, Math.Floor(Math.Log2(numTables))) * 16;
        int entrySelector = (int)Math.Floor(Math.Log2(numTables));
        int rangeShift = numTables * 16 - searchRange;

        var header = new BeWriter();
        header.U32(0x00010000);       // sfnt version (TrueType)
        header.U16((ushort)numTables);
        header.U16((ushort)searchRange);
        header.U16((ushort)entrySelector);
        header.U16((ushort)rangeShift);

        int offset = 12 + numTables * 16;
        var dir = new BeWriter();
        var bodyOffsets = new Dictionary<string, int>();
        var padded = new Dictionary<string, byte[]>();

        foreach (var tag in tags)
        {
            var data = tables[tag];
            var p = PadTo4(data);
            padded[tag] = p;
            dir.Tag(tag);
            dir.U32(CheckSum(p));
            dir.U32((uint)offset);
            dir.U32((uint)data.Length);   // original (unpadded) length
            bodyOffsets[tag] = offset;
            offset += p.Length;
        }

        var ms = new MemoryStream();
        var h = header.ToArray(); ms.Write(h, 0, h.Length);
        var d = dir.ToArray();    ms.Write(d, 0, d.Length);
        foreach (var tag in tags) { var p = padded[tag]; ms.Write(p, 0, p.Length); }
        var font = ms.ToArray();

        // patch head.checkSumAdjustment
        uint total = CheckSum(font);
        uint adjustment = 0xB1B0AFBA - total;
        int headOff = bodyOffsets["head"];
        font[headOff + 8] = (byte)(adjustment >> 24);
        font[headOff + 9] = (byte)(adjustment >> 16);
        font[headOff + 10] = (byte)(adjustment >> 8);
        font[headOff + 11] = (byte)(adjustment);
        return font;
    }

    private static byte[] PadTo4(byte[] data)
    {
        int pad = (4 - (data.Length & 3)) & 3;
        if (pad == 0) return data;
        var r = new byte[data.Length + pad];
        Buffer.BlockCopy(data, 0, r, 0, data.Length);
        return r;
    }

    private static void Pad4(MemoryStream ms)
    {
        int pad = (int)((4 - (ms.Length & 3)) & 3);
        for (int i = 0; i < pad; i++) ms.WriteByte(0);
    }

    private static uint CheckSum(byte[] data)
    {
        uint sum = 0;
        int n = data.Length;
        for (int i = 0; i < n; i += 4)
        {
            uint v = (uint)(data[i] << 24);
            if (i + 1 < n) v |= (uint)(data[i + 1] << 16);
            if (i + 2 < n) v |= (uint)(data[i + 2] << 8);
            if (i + 3 < n) v |= data[i + 3];
            sum += v;
        }
        return sum;
    }

    // ───────────────────────── big-endian writer ─────────────────
    private sealed class BeWriter
    {
        private readonly MemoryStream _ms = new();
        public void U8(int v) => _ms.WriteByte((byte)v);
        public void U16(int v) { _ms.WriteByte((byte)(v >> 8)); _ms.WriteByte((byte)v); }
        public void I16(short v) => U16((ushort)v);
        public void U32(uint v)
        {
            _ms.WriteByte((byte)(v >> 24)); _ms.WriteByte((byte)(v >> 16));
            _ms.WriteByte((byte)(v >> 8));  _ms.WriteByte((byte)v);
        }
        public void I64(long v)
        {
            for (int i = 7; i >= 0; i--) _ms.WriteByte((byte)(v >> (i * 8)));
        }
        public void Tag(string t)
        {
            for (int i = 0; i < 4; i++) _ms.WriteByte((byte)(i < t.Length ? t[i] : ' '));
        }
        public byte[] ToArray() => _ms.ToArray();
    }
}
