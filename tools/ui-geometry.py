#!/usr/bin/env python3
"""Measure a real in-game screen and write its sizes down, so a mock can be checked against them.

The colour check reads the game's SOURCE. Sizes cannot be read that way — a column's width is what
the layout works out at the window's real size, not the number typed in the file. So this stands the
screen up inside the running editor, at the size the window really is, measures every part, and
writes the result to prototypes/ui-geometry.json for check-ui-prototypes.py to compare a mock with.

Needs the Unity editor open on this project (the same live link `unity status` reports).

  tools/ui-geometry.py              measure every screen with a recipe below
  tools/ui-geometry.py Inventory    just this one

A screen appears here only once someone writes its RECIPE: a short piece of C# that builds that
screen offline and prints its parts. Adding a screen is adding a recipe, nothing else.
"""

import json
import os
import re
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "prototypes", "ui-geometry.json")

# Every recipe prints the same shape, so the comparison never needs to know which screen it is:
#   window=<px>|row=<px>|header_size=<pt>|cell_size=<pt>|search=<px>|list=<px>|col:<Name>=<px>|…
# It runs in a throwaway preview scene, so nothing touches the scenes the user has open.
COMMON = r"""
var sb = new System.Text.StringBuilder();
var prev = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
try {
  var canvasGo = new GameObject("C", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
  UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, prev);
  canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
  canvasGo.GetComponent<RectTransform>().sizeDelta = new Vector2(WIDTH, HEIGHT);
  var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
  panel.SetParent(canvasGo.transform, false);
  panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one;
  panel.offsetMin = Vector2.zero; panel.offsetMax = Vector2.zero;
  BUILD
  Canvas.ForceUpdateCanvases();
  UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
  Canvas.ForceUpdateCanvases();
  UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
  sb.Append("window=").Append(panel.rect.width.ToString("0.##")).Append("|");
  MEASURE
} finally {
  UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(prev);
}
return sb.ToString();
"""

# The bag: a 742x528 window whose frame leaves 710x470 inside (2px border + 14px padding each side,
# and 40px off the top for the title bar) — read from the window builder, not guessed.
INVENTORY_BUILD = r"""
  var list = new InventoryList();
  list.Build(panel, null);
  var entries = new System.Collections.Generic.List<BagEntry>();
  var fish = Resources.Load<Item>("Items/Grilled Fish");
  var sword = Resources.Load<Item>("Items/Sword");
  if (fish != null) entries.Add(new BagEntry { def = fish, count = 3 });
  if (sword != null) entries.Add(new BagEntry { def = sword, count = 1, instance = new EquipmentInstance(sword) });
  list.Refresh(entries);
"""

INVENTORY_MEASURE = r"""
  var all = panel.GetComponentsInChildren<RectTransform>(true);
  foreach (var rt in all) {
    if (rt.parent == null || rt.parent.name != "Header") continue;
    if (!rt.gameObject.activeInHierarchy) continue;
    var t = rt.GetComponentInChildren<TMPro.TMP_Text>(true);
    sb.Append("col:").Append(rt.name).Append("=").Append(rt.rect.width.ToString("0.##")).Append("|");
    if (t != null) sb.Append("header_size=").Append(t.fontSize.ToString("0.##")).Append("|");
  }
  foreach (var rt in all) {
    if (rt.name != "Row" || !rt.gameObject.activeInHierarchy) continue;
    sb.Append("row=").Append(rt.rect.height.ToString("0.##")).Append("|");
    for (int i = 0; i < rt.childCount; i++) {
      var c = rt.GetChild(i) as RectTransform;
      var t = c.GetComponent<TMPro.TMP_Text>();
      if (t == null || !c.gameObject.activeInHierarchy) continue;
      sb.Append("cell_size:").Append(c.name).Append("=").Append(t.fontSize.ToString("0.##")).Append("|");
    }
    break;
  }
  foreach (var rt in all) {
    if (rt.name == "Search") sb.Append("search=").Append(rt.rect.height.ToString("0.##")).Append("|");
    if (rt.name == "Viewport") sb.Append("list=").Append(rt.rect.height.ToString("0.##")).Append("|");
  }
"""

RECIPES = {
    "Inventory": {
        "width": 710, "height": 470,
        "window_note": "the bag window is 742x528; its frame leaves 710x470 inside",
        "build": INVENTORY_BUILD,
        "measure": INVENTORY_MEASURE,
    },
}


def measure(name, recipe):
    code = (COMMON
            .replace("WIDTH", str(recipe["width"]))
            .replace("HEIGHT", str(recipe["height"]))
            .replace("BUILD", recipe["build"])
            .replace("MEASURE", recipe["measure"]))
    fh = tempfile.NamedTemporaryFile("w", suffix=".cs", delete=False, encoding="utf-8")
    fh.write(code)
    fh.close()
    try:
        p = subprocess.run(["unity", "cmd", "eval_file", "--file", fh.name, "--timeout", "120"],
                           capture_output=True, text=True, timeout=180)
    finally:
        os.unlink(fh.name)

    m = re.search(r'"result":"(.*?)","executionTimeMs"', p.stdout, re.S)
    if not m:
        err = (p.stdout + p.stderr).strip().split("\n")[0][:200]
        return None, "the editor did not answer — %s" % (err or "no result")
    raw = m.group(1).replace("\\n", "").replace('\\"', '"')

    parts, cells, cols = {}, {}, {}
    for chunk in raw.split("|"):
        if "=" not in chunk:
            continue
        k, v = chunk.split("=", 1)
        try:
            val = float(v)
        except ValueError:
            continue
        if k.startswith("col:"):
            cols[k[4:]] = val
        elif k.startswith("cell_size:"):
            cells[k[10:]] = val
        else:
            parts[k] = val
    if not cols and not parts:
        return None, "the editor answered but measured nothing"
    return {"parts": parts, "columns": cols, "cell_sizes": cells,
            "note": recipe.get("window_note", "")}, None


def main():
    wanted = sys.argv[1:] or sorted(RECIPES)
    out = {}
    if os.path.exists(OUT):
        out = json.load(open(OUT, encoding="utf-8"))
    out.pop("generated_by", None)

    bad = 0
    measured = 0
    for name in wanted:
        if name not in RECIPES:
            print("no recipe for %r — the screens with one are: %s" % (name, ", ".join(sorted(RECIPES))))
            bad += 1
            continue
        data, err = measure(name, RECIPES[name])
        if err:
            print("%s: %s" % (name, err))
            bad += 1
            continue
        out[name] = data
        measured += 1
        print("%s: window %gpx, %d columns, row %gpx"
              % (name, data["parts"].get("window", 0), len(data["columns"]),
                 data["parts"].get("row", 0)))

    # Written only when something was actually measured: rewriting the old numbers after a failed measurement
    # printed "wrote", which read as a fresh measurement that never happened.
    if measured:
        out = {"generated_by": "tools/ui-geometry.py — measured from the running editor; re-run it",
               **{k: v for k, v in sorted(out.items()) if k != "generated_by"}}
        with open(OUT, "w", encoding="utf-8") as fh:
            json.dump(out, fh, indent=2)
            fh.write("\n")
        print("wrote %s" % os.path.relpath(OUT, ROOT))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
