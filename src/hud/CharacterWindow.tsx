import { WindowFrame } from "./WindowFrame";
import {
  BASE_STATS,
  DERIVED_ORDER,
  STAT_ICON,
  computeDerived,
  type BaseStats,
  type EquipmentInstance,
  type EquipSlot,
} from "./items";
import { MOCK_BASE, MOCK_EQUIPPED, MOCK_IDENTITY, equippedList } from "./mockSave";

/** Read-only readout. The +/− allocation lives in the existing StatPanel —
 *  this window does NOT duplicate those controls (one statPoints hint only). */
export function CharacterWindow({
  base = MOCK_BASE,
  equipped = MOCK_EQUIPPED,
  identity = MOCK_IDENTITY,
}: {
  base?: BaseStats;
  equipped?: Partial<Record<EquipSlot, EquipmentInstance>>;
  identity?: typeof MOCK_IDENTITY;
}) {
  const derived = computeDerived(base, equippedList(equipped));

  return (
    <WindowFrame title="Character" icon="🧍" width={272}>
      {/* Identity */}
      <div className="flex items-center gap-3 mb-3">
        <div
          className="w-14 h-14 rounded-lg grid place-items-center text-[30px] border border-white/10 shrink-0"
          style={{ background: "radial-gradient(circle at 50% 25%, #232c44, #11151f)" }}
        >
          🧙
        </div>
        <div className="min-w-0 flex-1">
          <div className="font-bold text-[15px] text-white truncate">{identity.name}</div>
          <div className="text-[12px] text-[#97a0b5]">
            Lv. {identity.level} {identity.className}
          </div>
          {/* XP bar */}
          <div className="mt-1.5 h-1.5 rounded-full bg-black/40 overflow-hidden">
            <div
              className="h-full rounded-full"
              style={{ width: `${identity.xpPct}%`, background: "linear-gradient(90deg,#46d17f,#7ef0a8)" }}
            />
          </div>
          <div className="text-[10px] text-[#6b7488] mt-0.5">XP {identity.xpPct}%</div>
        </div>
      </div>

      {/* Base stats */}
      <SectionLabel>Base Stats</SectionLabel>
      <div className="grid grid-cols-2 gap-x-3 gap-y-1 mb-3">
        {BASE_STATS.map((k) => (
          <div key={k} className="flex items-center justify-between text-[13px]">
            <span className="flex items-center gap-1.5 text-[#aeb6c9]">
              <span className="text-[14px]">{STAT_ICON[k]}</span>
              {k}
            </span>
            <span className="text-white font-semibold tabular-nums">{base[k]}</span>
          </div>
        ))}
      </div>
      {identity.statPoints > 0 && (
        <div className="text-[11px] text-[#ffe2a6] mb-3 -mt-1">
          ✦ {identity.statPoints} stat points to allocate
          <span className="text-[#6b7488]"> (use the Stats panel)</span>
        </div>
      )}

      {/* Derived stats */}
      <SectionLabel>Derived Stats</SectionLabel>
      <div className="grid grid-cols-2 gap-x-3 gap-y-1">
        {DERIVED_ORDER.map(({ key, label, suffix }) => (
          <div key={key} className="flex items-center justify-between text-[12.5px]">
            <span className="text-[#97a0b5]">{label}</span>
            <span className="text-white font-semibold tabular-nums">
              {derived[key]}
              {suffix ?? ""}
            </span>
          </div>
        ))}
      </div>
    </WindowFrame>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <div className="text-[10px] uppercase tracking-wider text-[#7f8aa3] mb-1.5 pb-1 border-b border-[#202838]">
      {children}
    </div>
  );
}
