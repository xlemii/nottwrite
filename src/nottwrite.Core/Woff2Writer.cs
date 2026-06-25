using System.IO.Compression;

namespace nottwrite.Core;

/// <summary>
/// Wraps an already-built sfnt (TrueType/OpenType) byte array into a WOFF2 file.
/// Uses the "null transform" for every table (including glyf/loca, transform
/// version 3) and Brotli-compresses the concatenated table data, which is a
/// fully valid — if not maximally compact — WOFF2 per the W3C spec.
/// </summary>
public static class Woff2Writer
{
    // Known table tags, indexed per the WOFF2 spec (index 63 = arbitrary tag).
    private static readonly string[] KnownTags =
    {
        "cmap","head","hhea","hmtx","maxp","name","OS/2","post","cvt ","fpgm",
        "glyf","loca","prep","CFF ","VORG","EBDT","EBLC","gasp","hdmx","kern",
        "LTSH","PCLT","VDMX","vhea","vmtx","BASE","GDEF","GPOS","GSUB","EBSC",
        "JSTF","MATH","CBDT","CBLC","COLR","CPAL","SVG ","sbix","acnt","avar",
        "bdat","bloc","bsln","cvar","fdsc","feat","fmtx","fvar","gvar","hsty",
        "just","lcar","mort","morx","opbd","prop","trak","Zapf","Silf","Glat",
        "Gloc","Feat","Sill",
    };

    private readonly record struct Table(string Tag, byte[] Data, uint OrigLength);

    public static byte[] Wrap(byte[] sfnt)
    {
        uint flavor   = ReadU32(sfnt, 0);
        int  numTables = ReadU16(sfnt, 4);

        // Parse the sfnt table directory (16 bytes per record after the 12-byte header).
        var tables = new List<Table>(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int rec   = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(sfnt, rec, 4);
            uint off  = ReadU32(sfnt, rec + 8);
            uint len  = ReadU32(sfnt, rec + 12);
            var data  = new byte[len];
            Array.Copy(sfnt, (int)off, data, 0, (int)len);
            tables.Add(new Table(tag, data, len));
        }

        // Uncompressed reconstructed size: header + records + 4-padded table data.
        uint totalSfntSize = (uint)(12 + numTables * 16);
        foreach (var t in tables) totalSfntSize += Pad4(t.OrigLength);

        // Compressed block = Brotli(concatenated raw table data, no padding).
        using var raw = new MemoryStream();
        foreach (var t in tables) raw.Write(t.Data, 0, t.Data.Length);
        byte[] compressed = Brotli(raw.ToArray());

        // Table directory (compact form).
        using var dir = new MemoryStream();
        foreach (var t in tables)
        {
            int known = Array.IndexOf(KnownTags, t.Tag);
            // glyf/loca use null transform version 3; all others version 0 (= null).
            int transform = t.Tag is "glyf" or "loca" ? 3 : 0;
            byte flags = (byte)(((transform & 3) << 6) | (known >= 0 ? known : 63));
            dir.WriteByte(flags);
            if (known < 0)
                dir.Write(System.Text.Encoding.ASCII.GetBytes(t.Tag), 0, 4);
            WriteBase128(dir, t.OrigLength);
            // transformLength omitted: every table here uses the null transform.
        }
        byte[] dirBytes = dir.ToArray();

        const int headerSize = 48;
        uint length = (uint)(headerSize + dirBytes.Length + compressed.Length);
        length = Pad4(length);

        using var ms = new MemoryStream();
        WriteU32(ms, 0x774F4632);          // "wOF2"
        WriteU32(ms, flavor);              // original sfnt version
        WriteU32(ms, length);             // total file length (padded)
        WriteU16(ms, (ushort)numTables);
        WriteU16(ms, 0);                  // reserved
        WriteU32(ms, totalSfntSize);
        WriteU32(ms, (uint)compressed.Length);
        WriteU16(ms, 0); WriteU16(ms, 0); // major/minor version
        WriteU32(ms, 0); WriteU32(ms, 0); WriteU32(ms, 0); // meta off/len/origLen
        WriteU32(ms, 0); WriteU32(ms, 0);                  // priv off/len

        ms.Write(dirBytes, 0, dirBytes.Length);
        ms.Write(compressed, 0, compressed.Length);
        while (ms.Length % 4 != 0) ms.WriteByte(0);
        return ms.ToArray();
    }

    private static byte[] Brotli(byte[] input)
    {
        using var outMs = new MemoryStream();
        using (var bs = new BrotliStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
            bs.Write(input, 0, input.Length);
        return outMs.ToArray();
    }

    // UIntBase128: big-endian, 7 bits/byte, high bit = continuation.
    private static void WriteBase128(Stream s, uint value)
    {
        Span<byte> tmp = stackalloc byte[5];
        int n = 0;
        do { tmp[n++] = (byte)(value & 0x7F); value >>= 7; } while (value != 0);
        for (int i = n - 1; i >= 0; i--)
            s.WriteByte((byte)(tmp[i] | (i > 0 ? 0x80 : 0x00)));
    }

    private static uint Pad4(uint v) => (v + 3u) & ~3u;

    private static int  ReadU16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    private static uint ReadU32(byte[] b, int o) =>
        (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    private static void WriteU16(Stream s, ushort v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
    private static void WriteU32(Stream s, uint v)
    { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
}
