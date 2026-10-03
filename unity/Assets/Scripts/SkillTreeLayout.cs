using System.Collections.Generic;

/// <summary>
/// WHICH ROW each skill of one weapon sits on in the Skills window's timeline — screen layout only, from authored
/// data. Plain numbers in, rows out, so it can be checked by calling it.
///
/// <para>A skill that other skills need starts a FAMILY, and its family keeps rows of its own so every wire between
/// a skill and what it needs stays inside them. The first skill that needs it shares its row when there is room
/// for its box after the parent's, so the wire runs straight across; every later one gets a row of its own below,
/// in the order they open. Skills that need nothing and that nothing needs share rows, each placed on the first row
/// where it fits after the skill before it. Families and the shared rows run top to bottom in the order of the
/// mastery level their first skill opens at.</para>
///
/// <para>A skill whose requirements loop back round to itself (which the skill-data check refuses) still gets a
/// row of its own at the bottom, so nothing is ever drawn on top of something else.</para>
/// </summary>
public static class SkillTreeLayout
{
    /// <summary>Fill <paramref name="rowOut"/> with each skill's row and return how many rows there are.
    /// <paramref name="opensAt"/> is each skill's opening mastery level; <paramref name="parent"/> the index of the skill
    /// it needs, or −1; <paramref name="widthInLevels"/> how many mastery levels one skill's box covers, so two boxes on
    /// one row never overlap.</summary>
    public static int Rows(int[] opensAt, int[] parent, float widthInLevels, int[] rowOut)
    {
        int n = opensAt != null ? opensAt.Length : 0;
        if (n == 0) return 0;

        var kids = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            int p = parent[i];
            if (p < 0 || p >= n || p == i) continue;
            (kids[p] ??= new List<int>()).Add(i);
        }
        for (int i = 0; i < n; i++)
            kids[i]?.Sort((a, b) => opensAt[a] != opensAt[b] ? opensAt[a].CompareTo(opensAt[b]) : a.CompareTo(b));

        var families = new List<int>();
        var loners = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (parent[i] >= 0 && parent[i] < n && parent[i] != i) continue;
            if (kids[i] != null) families.Add(i); else loners.Add(i);
        }
        loners.Sort((a, b) => opensAt[a] != opensAt[b] ? opensAt[a].CompareTo(opensAt[b]) : a.CompareTo(b));

        var blocks = new List<(int start, int root)>();
        foreach (int f in families) blocks.Add((opensAt[f], f));
        if (loners.Count > 0) blocks.Add((opensAt[loners[0]], -1));
        blocks.Sort((a, b) => a.start != b.start ? a.start.CompareTo(b.start) : a.root.CompareTo(b.root));

        var placed = new bool[n];
        int rows = 0;
        foreach (var block in blocks)
        {
            if (block.root >= 0)
            {
                Seat(block.root, rows, opensAt, widthInLevels, kids, rowOut, placed);
                rows += Span(block.root, opensAt, widthInLevels, kids);
                continue;
            }
            var ends = new List<float>();
            foreach (int i in loners)
            {
                float x = opensAt[i];
                int row = -1;
                for (int r = 0; r < ends.Count; r++) if (ends[r] <= x) { row = r; break; }
                if (row < 0) { row = ends.Count; ends.Add(0f); }
                ends[row] = x + widthInLevels;
                rowOut[i] = rows + row;
                placed[i] = true;
            }
            rows += ends.Count;
        }
        for (int i = 0; i < n; i++)
            if (!placed[i]) rowOut[i] = rows++;
        return rows;
    }

    // Does the first skill that needs `i` fit on i's own row, after i's box?
    static bool FirstKidSharesRow(int i, int[] opensAt, float width, List<int>[] kids)
        => kids[i] != null && opensAt[kids[i][0]] >= opensAt[i] + width;

    static void Seat(int i, int row, int[] opensAt, float width, List<int>[] kids, int[] rowOut, bool[] placed)
    {
        rowOut[i] = row;
        placed[i] = true;
        if (kids[i] == null) return;
        int r = FirstKidSharesRow(i, opensAt, width, kids) ? row : row + 1;
        foreach (int c in kids[i])
        {
            Seat(c, r, opensAt, width, kids, rowOut, placed);
            r += Span(c, opensAt, width, kids);
        }
    }

    static int Span(int i, int[] opensAt, float width, List<int>[] kids)
    {
        if (kids[i] == null) return 1;
        int span = FirstKidSharesRow(i, opensAt, width, kids) ? 0 : 1;
        foreach (int c in kids[i]) span += Span(c, opensAt, width, kids);
        return span;
    }
}
