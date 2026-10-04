using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EVERY KEY THE GAME ANSWERS TO, by what it does — the one table each feature reads its key from, so a key is
/// typed in exactly one place and "is this key free?" has one honest answer. Reading a key still goes through
/// <see cref="Hotkeys"/> (the typing guard, the input backends); this only says WHICH key.
///
/// <para>The hotbar's first row is F1–F9 and its second the number keys; its third row's keys are the player's own,
/// chosen in Settings and kept on this device (<see cref="GameSettings.HotbarRowThreeKey"/>). The Settings picker
/// offers only keys nothing else here uses (<see cref="IsFree"/>).</para>
/// </summary>
public static class GameKeys
{
    // ---- moving -------------------------------------------------------------------------------------------------
    public const KeyCode MoveForward = KeyCode.W, MoveLeft = KeyCode.A, MoveBack = KeyCode.S, MoveRight = KeyCode.D;
    public const KeyCode Jump = KeyCode.Space;
    public const KeyCode Dash = KeyCode.E;
    public const KeyCode SprintToggle = KeyCode.V;
    /// <summary>Held: walk instead of run. Either Shift.</summary>
    public const KeyCode Walk = KeyCode.LeftShift, WalkAlt = KeyCode.RightShift;
    public const KeyCode Interact = KeyCode.F;
    /// <summary>Switches the auto-pilot, which fights nearby creatures by itself, on and off.</summary>
    public const KeyCode AutoPilot = KeyCode.T;

    // ---- seeing -------------------------------------------------------------------------------------------------
    /// <summary>Held: show the names of items lying on the ground. Either Alt.</summary>
    public const KeyCode ShowDrops = KeyCode.LeftAlt, ShowDropsAlt = KeyCode.RightAlt;

    // ---- windows ------------------------------------------------------------------------------------------------
    public const KeyCode Inventory = KeyCode.B;
    public const KeyCode CharacterWindow = KeyCode.C;
    public const KeyCode Skills = KeyCode.K;
    public const KeyCode Calendar = KeyCode.L;
    /// <summary>The Map window: the zone you stand in, and the world map behind a button. The minimap has no key — it is
    /// always shown.</summary>
    public const KeyCode Map = KeyCode.M;
    public const KeyCode Quests = KeyCode.J;
    public const KeyCode Chat = KeyCode.Return, ChatAlt = KeyCode.KeypadEnter;
    public const KeyCode System = KeyCode.Escape;
    public const KeyCode DialogNext = KeyCode.RightArrow;

    // ---- the performance overlay (moved off F3/F4 when the hotbar took F1–F9) -----------------------------------
    public const KeyCode PerformanceOverlay = KeyCode.F10;
    public const KeyCode PerformancePeakReset = KeyCode.F11;

    // ---- the hotbar ---------------------------------------------------------------------------------------------

    /// <summary>The key slot <paramref name="slot"/> answers to: F1–F9 for the first row, 1–9 for the second, and
    /// the player's own choice (or none) for the third.</summary>
    public static KeyCode HotbarKey(int slot)
    {
        if (!HotbarBindings.IsValidSlot(slot)) return KeyCode.None;
        int col = HotbarBindings.ColumnOf(slot);
        switch (HotbarBindings.RowOf(slot))
        {
            case 0:  return KeyCode.F1 + col;
            case 1:  return KeyCode.Alpha1 + col;
            default: return GameSettings.HotbarRowThreeKey(col);
        }
    }

    // Every fixed key above, once. Kept beside the constants so a new key is added in one place.
    static readonly KeyCode[] Fixed =
    {
        MoveForward, MoveLeft, MoveBack, MoveRight, Jump, Dash, SprintToggle, Walk, WalkAlt, Interact, AutoPilot,
        ShowDrops, ShowDropsAlt, Inventory, CharacterWindow, Skills, Calendar, Map, Quests, Chat, ChatAlt,
        System, DialogNext, PerformanceOverlay, PerformancePeakReset,
    };

    /// <summary>Is <paramref name="key"/> one of the fixed keys above? A third-row key kept on this device from before
    /// a fixed key took that letter is dropped when it is read (<see cref="GameSettings.HotbarRowThreeKey"/>), or both
    /// would fire.</summary>
    public static bool IsFixed(KeyCode key)
    {
        for (int i = 0; i < Fixed.Length; i++) if (Fixed[i] == key) return true;
        return false;
    }

    // Keys that are never offered for the third row: the modifiers (they would fire with every combination) and
    // the mouse.
    static bool NeverAssignable(KeyCode k) =>
        k == KeyCode.None || k == KeyCode.LeftControl || k == KeyCode.RightControl || k == KeyCode.LeftCommand
        || k == KeyCode.RightCommand || k == KeyCode.LeftWindows || k == KeyCode.RightWindows || k == KeyCode.AltGr
        || k >= KeyCode.Mouse0;

    /// <summary>Is <paramref name="key"/> free for third-row slot <paramref name="column"/>: used by nothing in the
    /// table, by neither of the first two rows, and by no other third-row slot.</summary>
    public static bool IsFree(KeyCode key, int column)
    {
        if (NeverAssignable(key)) return false;
        for (int i = 0; i < Fixed.Length; i++) if (Fixed[i] == key) return false;
        if (key >= KeyCode.F1 && key <= KeyCode.F9) return false;
        if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9) return false;
        for (int c = 0; c < HotbarBindings.PerRow; c++)
            if (c != column && GameSettings.HotbarRowThreeKey(c) == key) return false;
        return true;
    }

    // The keys a person might reasonably bind, in keyboard order: letters, the 0 key, F12, the punctuation keys and
    // the number pad. Everything the game already uses is filtered out by IsFree.
    static readonly KeyCode[] Candidates = BuildCandidates();

    static KeyCode[] BuildCandidates()
    {
        var list = new List<KeyCode>();
        for (var k = KeyCode.A; k <= KeyCode.Z; k++) list.Add(k);
        list.Add(KeyCode.Alpha0);
        list.Add(KeyCode.F12);
        list.AddRange(new[]
        {
            KeyCode.BackQuote, KeyCode.Minus, KeyCode.Equals, KeyCode.LeftBracket, KeyCode.RightBracket,
            KeyCode.Backslash, KeyCode.Semicolon, KeyCode.Quote, KeyCode.Comma, KeyCode.Period, KeyCode.Slash,
            KeyCode.Tab, KeyCode.CapsLock, KeyCode.Insert, KeyCode.Delete, KeyCode.Home, KeyCode.End, KeyCode.PageUp,
            KeyCode.PageDown, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow,
        });
        for (var k = KeyCode.Keypad0; k <= KeyCode.Keypad9; k++) list.Add(k);
        list.AddRange(new[] { KeyCode.KeypadPeriod, KeyCode.KeypadDivide, KeyCode.KeypadMultiply, KeyCode.KeypadMinus,
                              KeyCode.KeypadPlus });
        return list.ToArray();
    }

    /// <summary>Fill <paramref name="into"/> with every key third-row slot <paramref name="column"/> may take.</summary>
    public static void FreeKeys(int column, List<KeyCode> into)
    {
        into.Clear();
        for (int i = 0; i < Candidates.Length; i++)
            if (IsFree(Candidates[i], column)) into.Add(Candidates[i]);
    }

    /// <summary>A key as a person reads it on a tile or a button: "F1", "3", "Q", "Num 4", "[".</summary>
    public static string Label(KeyCode key)
    {
        if (key == KeyCode.None) return "";
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
        if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num " + (int)(key - KeyCode.Keypad0);
        switch (key)
        {
            case KeyCode.BackQuote: return "`";
            case KeyCode.Minus: return "-";
            case KeyCode.Equals: return "=";
            case KeyCode.LeftBracket: return "[";
            case KeyCode.RightBracket: return "]";
            case KeyCode.Backslash: return "\\";
            case KeyCode.Semicolon: return ";";
            case KeyCode.Quote: return "'";
            case KeyCode.Comma: return ",";
            case KeyCode.Period: return ".";
            case KeyCode.Slash: return "/";
            case KeyCode.KeypadPeriod: return "Num .";
            case KeyCode.KeypadDivide: return "Num /";
            case KeyCode.KeypadMultiply: return "Num *";
            case KeyCode.KeypadMinus: return "Num -";
            case KeyCode.KeypadPlus: return "Num +";
            case KeyCode.CapsLock: return "Caps";
            case KeyCode.PageUp: return "PgUp";
            case KeyCode.PageDown: return "PgDn";
            case KeyCode.Insert: return "Ins";
            case KeyCode.Delete: return "Del";
            case KeyCode.UpArrow: return "Up";
            case KeyCode.DownArrow: return "Down";
            case KeyCode.LeftArrow: return "Left";
            default: return key.ToString();
        }
    }
}
