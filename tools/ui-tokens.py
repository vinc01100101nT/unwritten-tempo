#!/usr/bin/env python3
"""Write the game's real interface colours and fonts out for the mock pages to use.

The game is the owner of every one of these values; this script only READS them and copies them
somewhere a browser can see. Nothing here invents a colour, and nothing here writes to the game.

Two outputs, both regenerated from scratch every run:

  prototypes/ui-tokens.css   the shared palette and the rarity table as CSS variables, plus the
                             game's own three font files loaded from where the game keeps them
  prototypes/ui-tokens.json  the same values, plus which colours each individual screen declares
                             for itself, for check-ui-prototypes.py to read

Run it after any change to the game's colours:  tools/ui-tokens.py
"""

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, "unity", "Assets", "Scripts")
OUT_DIR = os.path.join(ROOT, "prototypes")
FONT_DIR_REL = "../unity/Assets/3_Data/Resources/Fonts"

# The shared palette and the rarity table live in these two, and they are the only files whose
# colours belong to the whole interface rather than to one screen.
SHARED = "UiKit.cs"
RARITY = "Rarity.cs"

# The game's three faces, as the game loads them.
FONTS = [
    ("title", "Cinzel", "Cinzel.ttf"),
    ("ui", "Inter", "Inter.ttf"),
    ("pixel", "PixelFont", "PixelFont.ttf"),
]

# What each screen is CALLED, in the words a person would use. A mock page names its screen with
# one of these, never with a file name, so renaming code never breaks a mock.
SCREEN_NAMES = {
    "ProgressionWindows.cs": "Inventory",
    "InventoryList.cs": "Inventory",
    "ItemActionMenu.cs": "Inventory",
    "ItemSlot.cs": "Inventory",
    "ItemTooltip.cs": "Item Tooltip",
    "SkillBookPanel.cs": "Skill Book",
    "SkillDraftPanel.cs": "Skill Draft",
    "SkillTooltip.cs": "Skill Tooltip",
    "WeaponSkillDetailsPanel.cs": "Weapon Skills",
    "HotbarPanel.cs": "Hotbar",
    "StatPanel.cs": "Character Sheet",
    "StatGlyph.cs": "Character Sheet",
    "CharacterWardrobePanel.cs": "Wardrobe",
    "CharacterSelectScreen.cs": "Character Select",
    "SignInScreen.cs": "Sign In",
    "DeathScreen.cs": "Death Screen",
    "CalendarPanel.cs": "Calendar",
    "CalendarWeatherHUD.cs": "Calendar",
    "ChatController.cs": "Chat",
    "WorldMapHUD.cs": "World Map",
    "Minimap.cs": "Minimap",
    "BuffBarPanel.cs": "Buff Bar",
    "StateInfoCard.cs": "Buff Bar",
    "CastBar.cs": "Cast Bar",
    "StaminaBar.cs": "Stamina Bar",
    "XpBarHUD.cs": "Xp Bar",
    "UnitStatusPanel.cs": "Unit Status",
    "MobNameplate.cs": "Mob Nameplate",
    "DamagePopup.cs": "Damage Numbers",
    "SpeechBubble.cs": "Speech Bubble",
    "DialogBox.cs": "Dialog Box",
    "SettingsWindow.cs": "Settings",
    "SystemWindow.cs": "System Menu",
    "BattleNotice.cs": "Battle Notice",
    "ReconnectNotice.cs": "Reconnect Notice",
    "AutoCombatIndicator.cs": "Auto Combat Indicator",
    "TouchHud.cs": "Touch Controls",
}


def hexof(r, g, b):
    return "#%02X%02X%02X" % (round(r), round(g), round(b))


def colours_in(src):
    """Every colour literal in one file, as hex plus the alpha it was written with.

    Three spellings are in use across the codebase and all three count: floats 0..1, the 0..255
    helper the rarity table uses, and a parsed html string.
    """
    found = []
    for m in re.finditer(r"new Color\(\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*(?:,\s*([\d.]+)f?\s*)?\)", src):
        r, g, b = (float(m.group(i)) * 255 for i in (1, 2, 3))
        a = float(m.group(4)) if m.group(4) else 1.0
        found.append((hexof(r, g, b), round(a, 3)))
    for m in re.finditer(r"Rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)f?\s*)?\)", src):
        r, g, b = (int(m.group(i)) for i in (1, 2, 3))
        a = float(m.group(4)) if m.group(4) else 1.0
        found.append((hexof(r, g, b), round(a, 3)))
    for m in re.finditer(r'TryParseHtmlString\(\s*"(#[0-9a-fA-F]{6})', src):
        found.append((m.group(1).upper(), 1.0))
    # A hex written into a rich-text tag the game shows on screen is a colour too.
    for m in re.finditer(r"<color=(#[0-9a-fA-F]{6})>", src):
        found.append((m.group(1).upper(), 1.0))
    return found


def named_colours(src):
    """The shared palette's colours WITH their names, so the CSS can carry the game's own words."""
    out = {}
    for m in re.finditer(
        r"public static (?:readonly )?Color (\w+)\s*=\s*new Color\(\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*,\s*([\d.]+)f?\s*(?:,\s*([\d.]+)f?\s*)?\)",
        src,
    ):
        name = m.group(1)
        r, g, b = (float(m.group(i)) * 255 for i in (2, 3, 4))
        a = float(m.group(5)) if m.group(5) else 1.0
        out[name] = {"hex": hexof(r, g, b), "alpha": round(a, 3)}
    return out


def rarity_table(src):
    """The five tiers, each with the three colour channels the game gives it."""
    out = {}
    for m in re.finditer(
        r'new Style\(\s*"(\w+)"\s*,\s*Rgb\(([^)]*)\)\s*,\s*Rgb\(([^)]*)\)\s*,\s*Rgb\(([^)]*)\)',
        src,
    ):
        tier = m.group(1)
        chans = {}
        for key, raw in (("border", m.group(2)), ("text", m.group(3)), ("glow", m.group(4))):
            parts = [p.strip().rstrip("f") for p in raw.split(",")]
            r, g, b = (int(float(p)) for p in parts[:3])
            a = float(parts[3]) if len(parts) > 3 else 1.0
            chans[key] = {"hex": hexof(r, g, b), "alpha": round(a, 3)}
        out[tier] = chans
    return out


def kebab(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "-", name).lower()


def main():
    shared_src = open(os.path.join(SCRIPTS, SHARED), encoding="utf-8").read()
    rarity_src = open(os.path.join(SCRIPTS, RARITY), encoding="utf-8").read()

    palette = named_colours(shared_src)
    rarity = rarity_table(rarity_src)

    # Every other screen's own colours, so a checker can say WHICH screen a borrowed colour came from.
    screens = {}
    for dirpath, dirnames, filenames in os.walk(SCRIPTS):
        if os.sep + "Editor" in dirpath:
            continue
        for fn in filenames:
            if not fn.endswith(".cs") or fn in (SHARED, RARITY):
                continue
            src = open(os.path.join(dirpath, fn), encoding="utf-8", errors="replace").read()
            found = colours_in(src)
            if not found:
                continue
            name = SCREEN_NAMES.get(fn, os.path.splitext(fn)[0])
            entry = screens.setdefault(name, {})
            for hexv, alpha in found:
                entry.setdefault(hexv, set()).add(alpha)

    screens_out = {n: {h: sorted(a) for h, a in sorted(c.items())} for n, c in sorted(screens.items())}

    shared_hexes = sorted({v["hex"] for v in palette.values()})
    rarity_hexes = sorted({c["hex"] for t in rarity.values() for c in t.values()})

    os.makedirs(OUT_DIR, exist_ok=True)

    data = {
        "generated_by": "tools/ui-tokens.py — do not hand-edit; re-run it instead",
        "palette": palette,
        "rarity": rarity,
        "shared_hexes": shared_hexes,
        "rarity_hexes": rarity_hexes,
        "fonts": [{"var": v, "family": fam, "file": f} for v, fam, f in FONTS],
        "screens": screens_out,
        "screen_names": sorted(set(SCREEN_NAMES.values())),
    }
    json_path = os.path.join(OUT_DIR, "ui-tokens.json")
    with open(json_path, "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=2)
        fh.write("\n")

    lines = [
        "/* Generated by tools/ui-tokens.py — do not hand-edit, re-run it.",
        "   Every value below is READ from the game's own code. The game owns them; this file is a copy",
        "   a browser can read, so a mock page never types a colour of its own. */",
        "",
    ]
    for v, fam, f in FONTS:
        lines += [
            "@font-face {",
            '  font-family: "%s";' % fam,
            '  src: url("%s/%s") format("truetype");' % (FONT_DIR_REL, f),
            "  font-display: swap;",
            "}",
        ]
    lines += ["", ":root {"]
    lines.append("  /* the shared palette */")
    for name in sorted(palette):
        c = palette[name]
        if c["alpha"] >= 1.0:
            lines.append("  --%s: %s;" % (kebab(name), c["hex"].lower()))
        else:
            r, g, b = (int(c["hex"][i:i + 2], 16) for i in (1, 3, 5))
            lines.append("  --%s: rgba(%d, %d, %d, %s);" % (kebab(name), r, g, b, c["alpha"]))
    lines.append("")
    lines.append("  /* the five tiers: border, text, glow */")
    for tier in rarity:
        for chan in ("border", "text", "glow"):
            c = rarity[tier][chan]
            key = "--rarity-%s-%s" % (tier.lower(), chan)
            if c["alpha"] >= 1.0:
                lines.append("  %s: %s;" % (key, c["hex"].lower()))
            else:
                r, g, b = (int(c["hex"][i:i + 2], 16) for i in (1, 3, 5))
                lines.append("  %s: rgba(%d, %d, %d, %s);" % (key, r, g, b, c["alpha"]))
    lines.append("")
    lines.append("  /* the game's own three faces */")
    for v, fam, f in FONTS:
        lines.append('  --font-%s: "%s";' % (v, fam))
    lines.append("}")
    lines.append("")
    css_path = os.path.join(OUT_DIR, "ui-tokens.css")
    with open(css_path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines))

    print("wrote %s" % os.path.relpath(css_path, ROOT))
    print("wrote %s" % os.path.relpath(json_path, ROOT))
    print("  %d shared colours, %d rarity colours, %d fonts, %d screens with colours of their own"
          % (len(shared_hexes), len(rarity_hexes), len(FONTS), len(screens_out)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
