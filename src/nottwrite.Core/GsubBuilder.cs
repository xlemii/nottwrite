namespace nottwrite.Core;

/// <summary>
/// Builds a minimal GSUB table with a `calt` (contextual alternates) feature that
/// rotates through per-glyph variants, so repeated/adjacent letters don't look
/// identical — the classic handwriting-font trick.
///
/// Rotation recipe (chaining contextual substitution): a default glyph preceded
/// by a *default* form becomes variant 1; preceded by a *variant 1* form becomes
/// variant 2; preceded by a *variant 2* form it stays default. Because each
/// substitution's output feeds the next position's backtrack context, a run
/// cycles default → v1 → v2 → default …
/// </summary>
public static class GsubBuilder
{
    /// <param name="defaultGids">All base (cmap'd) glyph ids — the "default" forms.</param>
    /// <param name="sets">(baseGid, [altGid1, altGid2…]) per character that has variants.</param>
    /// <returns>GSUB table bytes, or null if there's nothing to rotate.</returns>
    public static byte[]? Build(IEnumerable<int> defaultGids,
                                List<(int baseGid, List<int> alts)> sets)
    {
        var s1 = sets.Where(s => s.alts.Count >= 1)
                     .Select(s => (s.baseGid, sub: s.alts[0]))
                     .OrderBy(s => s.baseGid).ToList();
        if (s1.Count == 0) return null;

        var s2 = sets.Where(s => s.alts.Count >= 2)
                     .Select(s => (s.baseGid, sub: s.alts[1]))
                     .OrderBy(s => s.baseGid).ToList();

        int[] defaultCov = defaultGids.Distinct().OrderBy(g => g).ToArray();
        int[] var1Cov    = s1.Select(s => s.sub).Distinct().OrderBy(g => g).ToArray();
        bool hasS2        = s2.Count > 0;

        // ── SingleSubst lookups (LookupType 1) ──
        byte[] s1Sub = SingleSubst(s1);
        byte[] lookupS1 = Lookup(1, new[] { s1Sub });

        byte[]? lookupS2 = null;
        if (hasS2) lookupS2 = Lookup(1, new[] { SingleSubst(s2) });

        // ── Chaining contextual lookup (LookupType 6) ──
        // Subtable A: backtrack=default, input=S1 coverage  → apply S1 (lookup 0)
        var subA = ChainSubtable(defaultCov, s1.Select(s => s.baseGid).ToArray(), lookupIndex: 0);
        var subtables = new List<byte[]> { subA };
        if (hasS2)
        {
            // Subtable B: backtrack=var1, input=S2 coverage → apply S2 (lookup 1)
            var subB = ChainSubtable(var1Cov, s2.Select(s => s.baseGid).ToArray(), lookupIndex: 1);
            subtables.Add(subB);
        }
        byte[] chainLookup = Lookup(6, subtables.ToArray());

        int chainIndex = hasS2 ? 2 : 1;
        var lookups = hasS2
            ? new[] { lookupS1, lookupS2!, chainLookup }
            : new[] { lookupS1, chainLookup };
        byte[] lookupList = LookupList(lookups);

        byte[] featureList = FeatureList(chainIndex);
        byte[] scriptList  = ScriptList();

        // ── GSUB header (10 bytes) + the three lists ──
        int scriptOff  = 10;
        int featureOff = scriptOff + scriptList.Length;
        int lookupOff  = featureOff + featureList.Length;

        var w = new Be();
        w.U16(1); w.U16(0);            // version 1.0
        w.U16(scriptOff);
        w.U16(featureOff);
        w.U16(lookupOff);
        w.Raw(scriptList);
        w.Raw(featureList);
        w.Raw(lookupList);
        return w.ToArray();
    }

    private static byte[] Coverage(int[] sortedGids)
    {
        var w = new Be();
        w.U16(1);                       // coverage format 1
        w.U16(sortedGids.Length);
        foreach (int g in sortedGids) w.U16(g);
        return w.ToArray();
    }

    // SingleSubst Format 2: explicit substitute per covered glyph.
    private static byte[] SingleSubst(List<(int baseGid, int sub)> map)
    {
        var ordered = map.OrderBy(m => m.baseGid).ToList();
        int count = ordered.Count;
        byte[] cov = Coverage(ordered.Select(m => m.baseGid).ToArray());
        int covOff = 6 + 2 * count;

        var w = new Be();
        w.U16(2);                       // substFormat 2
        w.U16(covOff);
        w.U16(count);
        foreach (var m in ordered) w.U16(m.sub);
        w.Raw(cov);
        return w.ToArray();
    }

    // ChainedSequenceContext Format 3: 1 backtrack, 1 input, 0 lookahead.
    private static byte[] ChainSubtable(int[] backtrackCov, int[] inputCovGids, int lookupIndex)
    {
        int[] inputSorted = inputCovGids.Distinct().OrderBy(g => g).ToArray();
        byte[] btCov = Coverage(backtrackCov);
        byte[] inCov = Coverage(inputSorted);

        const int header = 18;          // see layout below
        int btOff = header;
        int inOff = header + btCov.Length;

        var w = new Be();
        w.U16(3);                       // format 3
        w.U16(1); w.U16(btOff);         // backtrack: count + coverage offset
        w.U16(1); w.U16(inOff);         // input: count + coverage offset
        w.U16(0);                       // lookahead count
        w.U16(1);                       // seqLookupCount
        w.U16(0); w.U16(lookupIndex);   // SeqLookupRecord: seqIndex, lookupListIndex
        w.Raw(btCov);
        w.Raw(inCov);
        return w.ToArray();
    }

    private static byte[] Lookup(int type, byte[][] subtables)
    {
        int count = subtables.Length;
        int offStart = 6 + 2 * count;
        var w = new Be();
        w.U16(type);
        w.U16(0);                       // lookupFlag
        w.U16(count);
        int running = offStart;
        foreach (var st in subtables) { w.U16(running); running += st.Length; }
        foreach (var st in subtables) w.Raw(st);
        return w.ToArray();
    }

    private static byte[] LookupList(byte[][] lookups)
    {
        int count = lookups.Length;
        int offStart = 2 + 2 * count;
        var w = new Be();
        w.U16(count);
        int running = offStart;
        foreach (var l in lookups) { w.U16(running); running += l.Length; }
        foreach (var l in lookups) w.Raw(l);
        return w.ToArray();
    }

    private static byte[] FeatureList(int chainLookupIndex)
    {
        var w = new Be();
        w.U16(1);                       // featureCount
        w.Tag("calt");
        w.U16(8);                       // feature table offset (2 + 6)
        // Feature table
        w.U16(0);                       // featureParams
        w.U16(1);                       // lookupIndexCount
        w.U16(chainLookupIndex);
        return w.ToArray();
    }

    private static byte[] ScriptList()
    {
        var w = new Be();
        w.U16(1);                       // scriptCount
        w.Tag("DFLT");
        w.U16(8);                       // script table offset (2 + 6)
        // Script table
        w.U16(4);                       // defaultLangSys offset (after this 4-byte header)
        w.U16(0);                       // langSysCount
        // LangSys table
        w.U16(0);                       // lookupOrder (null)
        w.U16(0xFFFF);                  // requiredFeatureIndex (none)
        w.U16(1);                       // featureIndexCount
        w.U16(0);                       // featureIndices[0]
        return w.ToArray();
    }

    private sealed class Be
    {
        private readonly MemoryStream _ms = new();
        public void U16(int v) { _ms.WriteByte((byte)(v >> 8)); _ms.WriteByte((byte)v); }
        public void Tag(string t) { for (int i = 0; i < 4; i++) _ms.WriteByte((byte)(i < t.Length ? t[i] : ' ')); }
        public void Raw(byte[] b) => _ms.Write(b, 0, b.Length);
        public byte[] ToArray() => _ms.ToArray();
    }
}
