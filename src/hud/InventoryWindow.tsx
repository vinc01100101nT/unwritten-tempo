import { useState } from "react";
import { WindowFrame } from "./WindowFrame";
import { ItemSlot } from "./ItemSlot";
import { ItemTooltip } from "./ItemTooltip";
import {
  ITEMS,
  computeDerived,
  totalWeight,
  type BaseStats,
  type EquipmentInstance,
  type EquipSlot,
  type InvStack,
  type ItemType,
} from "./items";
import {
  MOCK_BASE,
  MOCK_EQUIPPED,
  MOCK_GOLD,
  MOCK_INVENTORY,
  MOCK_WEIGHT_CAP,
  equippedList,
} from "./mockSave";

const TABS: { key: ItemType | "all"; label: string }[] = [
  { key: "all", label: "All" },
  { key: "equip", label: "Equip" },
  { key: "use", label: "Use" },
  { key: "material", label: "Material" },
  { key: "etc", label: "Etc" },
];

const COLS = 7;
const ROWS = 5;

export function InventoryWindow({
  stacks = MOCK_INVENTORY,
  gold = MOCK_GOLD,
  weightCap = MOCK_WEIGHT_CAP,
  base = MOCK_BASE,
  equipped = MOCK_EQUIPPED,
}: {
  stacks?: InvStack[];
  gold?: number;
  weightCap?: number;
  base?: BaseStats;
  equipped?: Partial<Record<EquipSlot, EquipmentInstance>>;
}) {
  const [tab, setTab] = useState<ItemType | "all">("all");
  const [hovered, setHovered] = useState<number | null>(null);

  const filtered = stacks.filter((s) => tab === "all" || ITEMS[s.defId]?.type === tab);
  const cells: (InvStack | null)[] = Array.from({ length: COLS * ROWS }, (_, i) => filtered[i] ?? null);

  const weight = totalWeight(stacks);
  const heavy = weight > weightCap;

  // Hover delta: if an equippable is hovered, preview equipping it.
  const liveDerived = computeDerived(base, equippedList(equipped));
  function deltaFor(stack: InvStack) {
    const def = ITEMS[stack.defId];
    if (!def || def.type !== "equip" || !def.slot || !stack.instance) return undefined;
    const next = { ...equipped, [def.slot]: stack.instance };
    return { from: liveDerived, to: computeDerived(base, equippedList(next)) };
  }

  const hoveredStack = hovered != null ? cells[hovered] : null;

  return (
    <div className="relative inline-block">
      <WindowFrame
        title="Inventory"
        icon="🎒"
        footer={
          <div className="flex items-center justify-between">
            <span className={heavy ? "text-[#ff6b6b] font-semibold" : ""}>
              Weight {weight} / {weightCap}
            </span>
            <span className="text-[#ffe2a6]">💰 {gold.toLocaleString()} z</span>
          </div>
        }
      >
        {/* Category tabs */}
        <div className="flex gap-1 mb-2.5">
          {TABS.map((t) => (
            <button
              key={t.key}
              onClick={() => setTab(t.key)}
              className={[
                "px-2.5 py-1 rounded-md text-[11px] font-semibold cursor-pointer transition-colors",
                tab === t.key
                  ? "bg-[#2a3550] text-white border"
                  : "text-[#8a93ab] hover:text-white hover:bg-white/5 border border-transparent",
              ].join(" ")}
              style={tab === t.key ? { borderColor: "#46557f" } : undefined}
            >
              {t.label}
            </button>
          ))}
        </div>

        {/* Grid */}
        <div
          className="grid gap-1.5 p-1.5 rounded-lg"
          style={{ gridTemplateColumns: `repeat(${COLS}, minmax(0,1fr))`, background: "rgba(0,0,0,.25)" }}
        >
          {cells.map((s, i) => (
            <ItemSlot
              key={i}
              stack={s}
              emptyGlyph=""
              label={s ? ITEMS[s.defId]?.name : undefined}
              onHover={(h) => setHovered(h ? i : null)}
            />
          ))}
        </div>

        <div className="mt-2 text-[10.5px] text-[#5f6b85] leading-snug">
          L-click select · double-click equip · R-click use · drag ⇄ equipment
        </div>
      </WindowFrame>

      {/* Tooltip w/ live stat-delta preview */}
      {hoveredStack && (
        <div className="absolute left-full top-2 ml-2 z-30 pointer-events-none">
          <ItemTooltip stack={hoveredStack} compareDelta={deltaFor(hoveredStack)} />
        </div>
      )}
    </div>
  );
}
