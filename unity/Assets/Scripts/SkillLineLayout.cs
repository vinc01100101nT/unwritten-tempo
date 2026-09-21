using UnityEngine;

/// <summary>Where the Skills window's line is filled to — screen layout only, from a level the server sent. Plain
/// numbers in, a height out, so it can be checked by calling it.</summary>
public static class SkillLineLayout
{
    /// <summary>The height the fill reaches. Level 0 sits at <paramref name="bottom"/>; skill i (sorted lowest level
    /// first, <paramref name="needs"/> holding each one's level) sits at <paramref name="firstNode"/> + i ×
    /// <paramref name="step"/>. The fill runs to the last skill reached, then along the gap to the next one in
    /// proportion to how many of the levels between them are already earned.</summary>
    public static float FillTop(int level, int[] needs, float bottom, float firstNode, float step)
    {
        float top = bottom;
        int prevLevel = 0; float prevY = bottom;
        for (int i = 0; needs != null && i < needs.Length; i++)
        {
            float y = firstNode + i * step;
            if (level >= needs[i]) { top = y; prevLevel = needs[i]; prevY = y; continue; }
            if (needs[i] > prevLevel) top = prevY + (y - prevY) * Mathf.Clamp01((level - prevLevel) / (float)(needs[i] - prevLevel));
            break;
        }
        return top;
    }
}
