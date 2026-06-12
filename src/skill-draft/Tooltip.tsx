import { SKILLS, classOf, type Skill } from "./graph";

/** Floating skill helper — name, tier, stats, lookahead, and target class. */
export function SkillTooltip({ skill }: { skill: Skill }) {
  const leadsTo = skill.next.map((id) => SKILLS[id]);
  const cls = classOf(skill.branch);

  return (
    <div
      className="w-60 rounded-xl border border-[#38405a] overflow-hidden text-left"
      style={{ background: "rgba(12,14,20,.97)", boxShadow: "0 20px 50px rgba(0,0,0,.6)" }}
    >
      <div className="flex items-center justify-between px-3 py-2.5 border-b border-[#2b3650] bg-gradient-to-b from-[#1c2333] to-[#141a28]">
        <span className="font-bold text-[13px] flex items-center gap-2 text-white">
          <span>{skill.icon}</span>
          {skill.name}
        </span>
        <span className="text-[10px] text-[#ffd98a] whitespace-nowrap">★ Tier {skill.tier}</span>
      </div>

      <div className="px-3 py-2.5 text-[12.5px] text-[#97a0b5]">
        {skill.desc}
        <br />
        <span className="text-white">• {skill.stat}</span>
      </div>

      {leadsTo.length > 0 && (
        <div className="px-3 py-2 border-t border-[#232b3d]">
          <div className="text-[10px] tracking-wider uppercase text-[#7f8aa3] mb-1.5">👁 Leads to</div>
          {leadsTo.map((s) => (
            <div key={s.id} className="flex items-center gap-1.5 text-white text-[12.5px] my-0.5">
              <span>{s.icon}</span>
              {s.name}
            </div>
          ))}
        </div>
      )}

      {cls && !skill.isClass && (
        <div className="px-3 py-2 border-t border-[#232b3d]">
          <div className="text-[10px] tracking-wider uppercase text-[#7f8aa3] mb-1">🎯 On path to</div>
          <div className="text-[#ffd98a] font-semibold flex items-center gap-1.5">
            <span>{cls.icon}</span>
            {cls.name}
          </div>
        </div>
      )}
    </div>
  );
}
