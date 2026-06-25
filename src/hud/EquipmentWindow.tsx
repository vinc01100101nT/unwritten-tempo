import { useState } from "react";
import { WindowFrame } from "./WindowFrame";
import { ItemSlot } from "./ItemSlot";
import {
  CARDS,
  ITEMS,
  RARITY,
  VISIBLE_SLOTS,
  computeDerived,
  type BaseStats,
  type EquipmentInstance,
  type EquipSlot,
} from "./items";
import { MOCK_BASE, MOCK_EQUIPPED, equippedList } from "./mockSave";

function toStack(inst?: EquipmentInstance) {
  return inst ? { defId: inst.defId, amount: 1, instance: inst } : null;
}

// Paperdoll arrangement: left column / center portrait / right column.
const LEFT: EquipSlot[] = ["head", "armor", "garment"];
const RIGHT: EquipSlot[] = ["weapon", "shield"];
const ACCESSORIES: EquipSlot[] = ["acc1", "acc2"];

export function EquipmentWindow({
  base = MOCK_BASE,
  equipped = MOCK_EQUIPPED,
}: {
  base?: BaseStats;
  equipped?: Partial<Record<EquipSlot, EquipmentInstance>>;
}) {
  const [selected, setSelected] = useState<EquipSlot>("weapon");
  const meta = VISIBLE_SLOTS.find((s) => s.slot === selected)!;
  const inst = equipped[selected];
  const def = inst ? ITEMS[inst.defId] : undefined;
  const r = def ? RARITY[def.rarity] : undefined;

  const derived = computeDerived(base, equippedList(equipped));

  const slotCell = (slot: EquipSlot) => {
    const m = VISIBLE_SLOTS.find((s) => s.slot === slot)!;
    return (
      <div key={slot} className="flex flex-col items-center gap-1">
        <ItemSlot
          stack={toStack(equipped[slot])}
          size={50}
          emptyGlyph={m.empty}
          selected={selected === slot}
          label={m.label}
          onClick={() => setSelected(slot)}
        />
        <span className="text-[9px] uppercase tracking-wide text-[#6b7488]">{m.label}</span>
      </div>
    );
  };

  return (
    <WindowFrame title="Equipment" icon="🧰" width={300}>
      {/* Paperdoll */}
      <div
        className="grid grid-cols-3 gap-2 p-3 rounded-lg mb-3"
        style={{ background: "rgba(0,0,0,.25)", gridTemplateColumns: "1fr 1.1fr 1fr" }}
      >
        <div className="flex flex-col gap-2 items-center">{LEFT.map(slotCell)}</div>

        <div className="flex flex-col items-center justify-start gap-1 pt-1">
          <div
            className="w-16 h-24 rounded-lg grid place-items-center text-[34px] border border-white/10"
            style={{ background: "radial-gradient(circle at 50% 25%, #232c44, #11151f)" }}
          >
            🧍
          </div>
          <span className="text-[10px] text-[#7f8aa3]">Lv.15 Swordsman</span>
        </div>

        <div className="flex flex-col gap-2 items-center">{RIGHT.map(slotCell)}</div>

        <div className="col-span-3 flex justify-center gap-6 pt-1 border-t border-[#202838] mt-1">
          {ACCESSORIES.map(slotCell)}
        </div>
      </div>

      {/* Selected item detail */}
      <div className="rounded-lg border p-2.5" style={{ borderColor: def ? r!.border : "#2b3142", background: "rgba(255,255,255,.02)" }}>
        {def ? (
          <>
            <div className="flex items-center gap-2">
              <span className="text-[22px]">{def.icon}</span>
              <div className="min-w-0">
                <div className="font-bold text-[13px] truncate" style={{ color: r!.text }}>
                  {inst!.upgrade > 0 && <span className="text-[#ffe08a]">+{inst!.upgrade} </span>}
                  {def.name}
                </div>
                <div className="text-[10.5px] font-semibold" style={{ color: r!.border }}>
                  {r!.label} · {meta.label}
                </div>
              </div>
              <div className="ml-auto text-right text-[12px] text-white font-semibold whitespace-nowrap">
                {def.atk ? `${def.atk + inst!.upgrade * 3} ATK` : def.def ? `${def.def} DEF` : ""}
              </div>
            </div>

            {/* Card sockets — managed via dialog, NOT drag-onto-socket */}
            {def.cardSlots ? (
              <div className="mt-2.5">
                <div className="text-[10px] uppercase tracking-wider text-[#7f8aa3] mb-1">
                  Cards {inst!.cards.length}/{def.cardSlots}
                </div>
                <div className="flex gap-1.5">
                  {Array.from({ length: def.cardSlots }).map((_, i) => {
                    const cid = inst!.cards[i];
                    const c = cid ? CARDS[cid] : undefined;
                    return (
                      <div
                        key={i}
                        title={c ? `${c.name} — ${c.blurb}` : "Insert card"}
                        className="flex-1 h-9 rounded-md grid place-items-center text-[16px] border cursor-pointer transition-colors"
                        style={{
                          borderColor: c ? "#46d17f" : "#2b3142",
                          borderStyle: c ? "solid" : "dashed",
                          background: c ? "rgba(70,209,127,.06)" : "transparent",
                        }}
                      >
                        {c ? c.icon : <span className="text-[#5a6580] text-[18px] leading-none">＋</span>}
                      </div>
                    );
                  })}
                </div>
                <button className="mt-2 w-full py-1.5 rounded-md text-[11px] font-semibold text-[#bcd4ff] border border-[#46557f] bg-[#2a3550] hover:bg-[#324066] cursor-pointer">
                  Insert Card…
                </button>
              </div>
            ) : (
              <div className="mt-2 text-[11px] text-[#5f6b85] italic">No card slots.</div>
            )}
          </>
        ) : (
          <div className="text-[12px] text-[#7f8aa3] italic py-3 text-center">
            Empty {meta.label.toLowerCase()} slot — drag an item here to equip.
          </div>
        )}
      </div>

      {/* Quick derived readout so equip changes feel connected */}
      <div className="mt-2.5 flex justify-between text-[11px] text-[#97a0b5] tabular-nums">
        <span>ATK <span className="text-white font-semibold">{derived.ATK}</span></span>
        <span>DEF <span className="text-white font-semibold">{derived.DEF}</span></span>
        <span>CRIT <span className="text-white font-semibold">{derived.CRIT}%</span></span>
        <span>ASPD <span className="text-white font-semibold">{derived.ASPD}</span></span>
      </div>
    </WindowFrame>
  );
}
