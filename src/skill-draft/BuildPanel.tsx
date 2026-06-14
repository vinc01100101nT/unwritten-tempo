import { SKILLS, headingToward, BRANCH_META } from "./graph";

/** Side panel: skills owned this run + the class achieved (or trending toward). */
export function BuildPanel({ owned, level }: { owned: Set<string>; level: number }) {
  const all = [...owned].filter((id) => id !== "novice").map((id) => SKILLS[id]);
  const learned = all.filter((s) => !s.isClass);
  // Classes earned this run, base → prestige — the path shift, made visible.
  const classChain = all.filter((s) => s.isClass).sort((a, b) => a.tier - b.tier);
  const heading = headingToward(owned);

  return (
    <div className="rounded-2xl border border-[#2b3142] bg-[#12151c] p-5">
      <div className="flex items-center justify-between mb-3">
        <div className="text-[11px] tracking-wider uppercase text-[#97a0b5]">Your build</div>
        <div className="text-[12px] text-[#ffd98a] font-bold">Lv.{level}</div>
      </div>

      <div className="flex flex-wrap gap-2 mb-4 min-h-[2rem]">
        {learned.length ? (
          learned.map((s) => (
            <span
              key={s.id}
              className="inline-flex items-center gap-1.5 text-[13px] px-2.5 py-1 rounded-lg bg-[#1d2230] border border-[#2b3142] text-white"
            >
              <span>{s.icon}</span>
              {s.name}
            </span>
          ))
        ) : (
          <span className="text-[13px] text-[#7f8aa3] italic">No skills yet — make your first pick.</span>
        )}
      </div>

      <div className="border-t border-[#222838] pt-3">
        {classChain.length ? (
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-[11px] tracking-wider uppercase text-[#7f8aa3] flex items-center gap-1">
              Class <span>🏆</span>
            </span>
            <span className="flex items-center gap-1.5 flex-wrap font-extrabold">
              {classChain.map((c, i) => (
                <span key={c.id} className="flex items-center gap-1.5">
                  {i > 0 && <span className="text-[#5a6580] font-normal">▸</span>}
                  <span>{c.icon}</span>
                  <span style={{ color: BRANCH_META[c.branch].color }}>{c.name}</span>
                </span>
              ))}
            </span>
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <span className="text-[11px] tracking-wider uppercase text-[#7f8aa3]">Heading toward</span>
            {heading ? (
              <span className="font-extrabold" style={{ color: heading.color }}>
                {heading.label}
              </span>
            ) : (
              <span className="text-[#97a0b5] italic">Undecided</span>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
