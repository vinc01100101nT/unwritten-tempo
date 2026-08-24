using UnityEditor;
using UnityEngine;

/// <summary>
/// The swatch grid that drops out of the pencil beside a weapon group in the Skill Composer. Offers exactly the
/// colours in <see cref="WeaponColors.Palette"/> — a fixed set rather than a free colour wheel, so twelve weapons
/// stay as far apart on screen as twelve colours can be instead of drifting into twelve shades of the same one.
/// </summary>
public class WeaponColorPalette : PopupWindowContent
{
    readonly System.Action<Color> _onPick;

    public WeaponColorPalette(System.Action<Color> onPick) { _onPick = onPick; }

    const int Columns = 6;
    const float Cell = 24f;
    const float Pad = 6f;

    static int Rows => Mathf.CeilToInt(WeaponColors.Palette.Length / (float)Columns);

    public override Vector2 GetWindowSize()
        => new Vector2(Columns * Cell + Pad * 2f, Rows * Cell + Pad * 2f);

    public override void OnGUI(Rect rect)
    {
        var palette = WeaponColors.Palette;
        for (int i = 0; i < palette.Length; i++)
        {
            var swatch = new Rect(Pad + (i % Columns) * Cell, Pad + (i / Columns) * Cell, Cell - 3f, Cell - 3f);
            EditorGUI.DrawRect(swatch, palette[i]);
            if (!GUI.Button(swatch, GUIContent.none, GUIStyle.none)) continue;
            _onPick?.Invoke(palette[i]);
            editorWindow.Close();
        }
    }
}
