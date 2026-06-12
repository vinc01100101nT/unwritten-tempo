import { useState } from "react";
import { SKILLS, type Skill } from "./graph";
import { SkillTooltip } from "./Tooltip";

interface Props {
  skill: Skill;
  variant: "guaranteed" | "wildcard";
  onPick: (id: string) => void;
}

/** One draftable option in the level-up screen. */
export function SkillCard({ skill, variant, onPick }: Props) {
  const [hover, setHover] = useState(false);
  const guaranteed = variant === "guaranteed";
  const leadsTo = skill.next.map((id) => SKILLS[id]).slice(0, 2);

  const cardStyle = guaranteed
    ? { borderTopColor: "#4f8cff", boxShadow: "0 0 32px rgba(79,140,255,.28)" }
    : { borderTopColor: "#f5b942", boxShadow: "0 0 34px rgba(245,185,66,.32)" };
  const iconStyle = guaranteed
    ? { background: "radial-gradient(circle at 50% 30%, #2a3550, #141a28)" }
    : { background: "radial-gradient(circle at 50% 30%, #3a2f16, #1a1813)" };

  return (
    <div className="relative" onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}>
      <button
        onClick={() => onPick(skill.id)}
        style={cardStyle}
        className={[
          "w-full text-left rounded-2xl p-5 cursor-pointer transition-transform duration-150 hover:-translate-y-1.5",
          "border border-[#2b3142] border-t-[3px] bg-gradient-to-b",
          guaranteed ? "from-[#1d2230] to-[#171b27]" : "from-[#241f16] to-[#1a1813]",
        ].join(" ")}
      >
        <span
          className={[
            "inline-flex items-center gap-1.5 text-[11px] font-bold tracking-wider uppercase px-2.5 py-1 rounded-full mb-3 border",
            guaranteed
              ? "text-[#bcd4ff] bg-[#4f8cff]/15 border-[#4f8cff]/35"
              : "text-[#ffe2a6] bg-[#f5b942]/15 border-[#f5b942]/40",
          ].join(" ")}
        >
          {guaranteed ? "🟦 Guaranteed" : "🎲 Wildcard"}
        </span>

        <div
          className="w-[68px] h-[68px] rounded-2xl grid place-items-center text-[36px] mb-3.5 border border-white/10"
          style={iconStyle}
        >
          {skill.icon}
        </div>

        <div className="text-[18px] font-bold text-white">{skill.name}</div>
        <div className="text-[13px] text-[#97a0b5] mb-4">{skill.stat}</div>

        <div className="border-t border-dashed border-[#2b3142] pt-3">
          {guaranteed ? (
            <>
              <div className="text-[10px] tracking-wider uppercase text-[#97a0b5] mb-2">
                👁 Lookahead — next picks
              </div>
              {leadsTo.length ? (
                leadsTo.map((s) => (
                  <div key={s.id} className="flex items-center gap-2 text-[14px] my-1 text-white">
                    <span>{skill.icon}</span>
                    <span className="text-[#5a6580]">→</span>
                    <span>{s.icon}</span>
                    <span className="text-[#97a0b5]">{s.name}</span>
                  </div>
                ))
              ) : (
                <div className="text-[13px] text-[#97a0b5] italic">Unlocks your job class</div>
              )}
            </>
          ) : (
            <>
              <div className="text-[10px] tracking-wider uppercase text-[#97a0b5] mb-2">
                🔒 Lookahead — locked
              </div>
              <div className="text-[14px] text-[#8a7b52] italic flex items-center gap-2">
                <span className="text-[20px] tracking-widest">❔❔❔</span> hidden until chosen
              </div>
            </>
          )}
        </div>
      </button>

      {hover && (
        <div className="absolute z-20 top-16 left-1/2 -translate-x-1/2 pointer-events-none">
          <SkillTooltip skill={skill} />
        </div>
      )}
    </div>
  );
}
