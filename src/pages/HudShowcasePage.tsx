import { InventoryWindow } from "../hud/InventoryWindow";
import { EquipmentWindow } from "../hud/EquipmentWindow";
import { CharacterWindow } from "../hud/CharacterWindow";

/**
 * Full-picture preview: the three on-demand progression windows opened together
 * over a faux game surface. In-game these are draggable, opened on C / E / I —
 * never all-permanent. This composition is just for design review.
 */
export function HudShowcasePage() {
  return (
    <div
      className="min-h-screen w-full font-sans text-[#e7ebf3] p-8"
      style={{
        background:
          "radial-gradient(1200px 600px at 70% -10%, #1a2336 0%, #0a0c12 55%), #0a0c12",
      }}
    >
      <div className="mb-6">
        <h1 className="text-[20px] font-bold text-white tracking-tight">unwritten · Progression Windows</h1>
        <p className="text-[13px] text-[#97a0b5] mt-1 max-w-2xl">
          On-demand, draggable panels (open on <Kbd>I</Kbd> <Kbd>E</Kbd> <Kbd>C</Kbd>). One{" "}
          <span className="text-white">ItemSlot</span> visual language everywhere — border = rarity,{" "}
          <span className="text-[#ffe08a]">+N</span> = enhancement, ◇ pips = cards, icon = identity.
          Hover an inventory item to preview the live stat delta.
        </p>
      </div>

      <div className="flex flex-wrap items-start gap-6">
        <InventoryWindow />
        <EquipmentWindow />
        <CharacterWindow />
      </div>

      <RarityLegend />
    </div>
  );
}

function RarityLegend() {
  const tiers: { label: string; color: string }[] = [
    { label: "Common", color: "#4b5563" },
    { label: "Uncommon", color: "#46d17f" },
    { label: "Rare", color: "#4f8cff" },
    { label: "Epic", color: "#a855f7" },
    { label: "Legendary", color: "#f5b942" },
  ];
  return (
    <div className="mt-8 flex flex-wrap items-center gap-4 text-[12px] text-[#97a0b5]">
      <span className="uppercase tracking-wider text-[10px] text-[#6b7488]">Rarity</span>
      {tiers.map((t) => (
        <span key={t.label} className="flex items-center gap-1.5">
          <span className="w-3.5 h-3.5 rounded-sm border-2" style={{ borderColor: t.color }} />
          {t.label}
        </span>
      ))}
    </div>
  );
}

function Kbd({ children }: { children: React.ReactNode }) {
  return (
    <kbd className="px-1.5 py-0.5 rounded border border-[#2b3142] bg-black/30 text-[11px] text-[#cdd6ea] font-mono">
      {children}
    </kbd>
  );
}
