import { useCallback, useEffect, useState } from "react";
import { getDraft, type Draft } from "../skill-draft/graph";
import { SkillCard } from "../skill-draft/SkillCard";
import { SkillTree } from "../skill-draft/SkillTree";
import { BuildPanel } from "../skill-draft/BuildPanel";

const START = ["novice"];

const PAGE_BG =
  "radial-gradient(1200px 600px at 50% -10%, #1b2336 0%, transparent 60%)," +
  "radial-gradient(900px 500px at 50% 120%, #1a1230 0%, transparent 55%)," +
  "#0a0c12";

/** Interactive level-up skill draft + skill tree, driven by one shared graph. */
export function SkillDraftPage() {
  const [owned, setOwned] = useState<Set<string>>(() => new Set(START));
  const [level, setLevel] = useState(1);
  const [draft, setDraft] = useState<Draft | null>(() => getDraft(new Set(START)));
  const [lastPicked, setLastPicked] = useState<string | null>(null);

  // Re-draw the 3 options whenever the owned set changes (i.e. on a pick).
  useEffect(() => {
    setDraft(getDraft(owned));
  }, [owned]);

  const pick = useCallback((id: string) => {
    setOwned((prev) => {
      const next = new Set(prev);
      next.add(id);
      return next;
    });
    setLevel((l) => l + 1);
    setLastPicked(id);
  }, []);

  const reset = () => {
    setOwned(new Set(START));
    setLevel(1);
    setLastPicked(null);
  };

  return (
    <div className="min-h-screen text-[#e7ebf3] font-sans" style={{ background: PAGE_BG }}>
      <div className="max-w-6xl mx-auto px-6 py-12">
        <header className="text-center mb-2">
          <div className="text-[12px] tracking-[4px] uppercase text-[#97a0b5]">
            Skill Progression — Roguelike Draft
          </div>
        </header>

        {draft ? (
          <>
            <h1 className="text-center text-[40px] font-extrabold leading-tight mb-1 bg-gradient-to-b from-white via-[#ffd98a] to-[#f5b942] bg-clip-text text-transparent">
              LEVEL UP! &nbsp;Lv.{level}
            </h1>
            <p className="text-center text-[14px] text-[#97a0b5] mb-9">
              Choose <b className="text-white">1 of 3</b> skills
            </p>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-5 mb-6">
              {draft.guaranteed.map((s) => (
                <SkillCard key={s.id} skill={s} variant="guaranteed" onPick={pick} />
              ))}
              {draft.wildcard && (
                <SkillCard key={draft.wildcard.id} skill={draft.wildcard} variant="wildcard" onPick={pick} />
              )}
            </div>
          </>
        ) : (
          <div className="text-center my-12">
            <h1 className="text-[32px] font-extrabold text-[#ffd98a] mb-2">All skills learned!</h1>
            <p className="text-[#97a0b5]">You've drafted the entire tree.</p>
          </div>
        )}

        <div className="flex justify-center mb-10">
          <button
            onClick={reset}
            className="text-[13px] px-4 py-2 rounded-lg border border-[#2b3142] bg-[#12151c] text-[#97a0b5] hover:text-white hover:border-[#3a4258] transition-colors"
          >
            ↺ Reset run
          </button>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-[1fr_280px] gap-6 items-start">
          <div className="rounded-2xl border border-[#2b3142] bg-gradient-to-b from-[#171b27] to-[#0f1219] p-6">
            <div className="text-center text-[12px] tracking-[3px] uppercase text-[#97a0b5] mb-4">
              🌳 Skill tree — path → job class
            </div>
            <SkillTree owned={owned} lastPicked={lastPicked} />
          </div>
          <BuildPanel owned={owned} level={level} />
        </div>
      </div>
    </div>
  );
}
