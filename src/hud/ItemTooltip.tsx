import {
  CARDS,
  ITEMS,
  RARITY,
  BASE_STATS,
  type BaseStatKey,
  type Derived,
  type InvStack,
} from "./items";

/** Green/red stat-delta rows — the highest-value modern convenience.
 *  `ATK  220 → 248  (+28)`  computed from the pure derived engine. */
export function StatDelta({ from, to }: { from: Derived; to: Derived }) {
  const rows = (Object.keys(from) as (keyof Derived)[]).filter((k) => from[k] !== to[k]);
  if (rows.length === 0) {
    return <div className="text-[12px] text-[#7f8aa3] italic">No stat change.</div>;
  }
  return (
    <div className="flex flex-col gap-0.5">
      {rows.map((k) => {
        const diff = (to[k] as number) - (from[k] as number);
        const up = diff > 0;
        return (
          <div key={k} className="flex items-center justify-between text-[12px] tabular-nums">
            <span className="text-[#97a0b5]">{k}</span>
            <span className="flex items-center gap-1.5">
              <span style={{ color: "#6b7488" }}>{from[k]}</span>
              <span className="text-[#5a6580]">→</span>
              <span className="text-white">{to[k]}</span>
              <span
                className="font-bold w-12 text-right"
                style={{ color: up ? "#46d17f" : "#ff6b6b" }}
              >
                {up ? "+" : ""}
                {Math.round(diff * 10) / 10}
              </span>
            </span>
          </div>
        );
      })}
    </div>
  );
}

/** The big item tooltip — does ~90% of the information work.
 *  Header border carries rarity; everything else is plain. */
export function ItemTooltip({ stack, compareDelta }: { stack: InvStack; compareDelta?: { from: Derived; to: Derived } }) {
  const def = ITEMS[stack.defId];
  if (!def) return null;
  const r = RARITY[def.rarity];
  const inst = stack.instance;

  const statBonuses = (Object.keys(def.bonuses ?? {}) as (keyof NonNullable<typeof def.bonuses>)[])
    .filter((k) => (BASE_STATS as string[]).includes(k as string))
    .map((k) => `+${def.bonuses![k]} ${k}`);
  const otherBonuses: string[] = [];
  if (def.bonuses?.aspd) otherBonuses.push(`${def.bonuses.aspd > 0 ? "+" : ""}${def.bonuses.aspd}% ASPD`);
  if (def.bonuses?.crit) otherBonuses.push(`+${def.bonuses.crit}% CRIT`);

  return (
    <div
      className="w-64 rounded-xl overflow-hidden text-left font-sans"
      style={{ background: "rgba(10,12,18,.98)", border: `1px solid ${r.border}`, boxShadow: `0 18px 50px rgba(0,0,0,.6), 0 0 22px ${r.glow}` }}
    >
      <div className="px-3 py-2.5 border-b" style={{ borderColor: "#222a3b", background: "linear-gradient(rgba(255,255,255,.04),transparent)" }}>
        <div className="flex items-center gap-2">
          <span className="text-[20px]">{def.icon}</span>
          <span className="font-bold text-[14px]" style={{ color: r.text }}>
            {inst && inst.upgrade > 0 && <span className="text-[#ffe08a]">+{inst.upgrade} </span>}
            {def.name}
          </span>
        </div>
        <div className="text-[11px] font-semibold mt-0.5" style={{ color: r.border }}>
          {r.label}
          {def.slot && <span className="text-[#6b7488]"> · {def.type}</span>}
        </div>
      </div>

      <div className="px-3 py-2.5 flex flex-col gap-1.5">
        {(def.atk || def.def || def.matk) && (
          <div className="flex gap-3 text-[13px] text-white font-semibold">
            {def.atk ? <span>ATK {def.atk + (inst?.upgrade ?? 0) * 3}</span> : null}
            {def.matk ? <span>MATK {def.matk}</span> : null}
            {def.def ? <span>DEF {def.def}</span> : null}
          </div>
        )}

        {(statBonuses.length > 0 || otherBonuses.length > 0) && (
          <div className="text-[12px] text-[#8fe0b0]">
            {[...statBonuses, ...otherBonuses].join("  ·  ")}
          </div>
        )}

        {def.desc && <div className="text-[12px] text-[#97a0b5] italic leading-snug">{def.desc}</div>}

        {/* Cards socketed in this instance */}
        {def.cardSlots ? (
          <div className="mt-1 pt-2 border-t border-[#202838]">
            <div className="text-[10px] uppercase tracking-wider text-[#7f8aa3] mb-1">
              Cards {inst?.cards.length ?? 0}/{def.cardSlots}
            </div>
            <div className="flex flex-col gap-0.5">
              {Array.from({ length: def.cardSlots }).map((_, i) => {
                const cid = inst?.cards[i];
                const c = cid ? CARDS[cid] : undefined;
                return (
                  <div key={i} className="flex items-center gap-1.5 text-[12px]">
                    {c ? (
                      <>
                        <span>{c.icon}</span>
                        <span className="text-white">{c.name}</span>
                        <span className="text-[#8fe0b0]">({c.blurb})</span>
                      </>
                    ) : (
                      <span className="text-[#5a6580] italic">□ Empty socket</span>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        ) : null}

        {/* Equip-compare / stat-delta preview */}
        {compareDelta && (
          <div className="mt-1 pt-2 border-t border-[#202838]">
            <div className="text-[10px] uppercase tracking-wider text-[#7f8aa3] mb-1.5">
              ⇄ If equipped
            </div>
            <StatDelta from={compareDelta.from} to={compareDelta.to} />
          </div>
        )}

        <div className="mt-1 pt-2 border-t border-[#202838] flex items-center justify-between text-[11px] text-[#7f8aa3]">
          <span>Weight {def.weight}</span>
          <span>💰 {def.value.toLocaleString()} z</span>
        </div>
      </div>
    </div>
  );
}

/** Tiny helper used by the character window header. */
export function statKeyLabel(k: BaseStatKey) {
  return k;
}
