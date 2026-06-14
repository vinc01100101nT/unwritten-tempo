import { useCallback, useLayoutEffect, useRef, useState } from "react";
import { SKILLS, TIERS, neighborsOf, BRANCH_META } from "./graph";
import { SkillTooltip } from "./Tooltip";

interface Line {
  id: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  owned: boolean;
}

interface Props {
  owned: Set<string>;
  lastPicked: string | null;
  /** Skills the player can draft right now (lit). Defaults to all reachable. */
  available?: Set<string>;
  /** Skills offered before but passed over — permanently missed (dimmed). */
  burned?: Set<string>;
}

/**
 * Renders the skill graph as tier rows. Connector lines are computed from the
 * ACTUAL rendered node positions (refs + getBoundingClientRect) and recomputed
 * on resize — never hardcoded — so every line hits node centers and stays
 * aligned at any width (PRD R7).
 */
export function SkillTree({ owned, lastPicked, available, burned }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const nodeRefs = useRef<Map<string, HTMLElement>>(new Map());
  const [lines, setLines] = useState<Line[]>([]);
  const [hovered, setHovered] = useState<string | null>(null);

  // Fall back to "every reachable neighbour" when the page doesn't pass an
  // explicit draftable set (e.g. an isolated storyboard).
  const availableSet = available ?? new Set(neighborsOf(owned));
  const burnedSet = burned ?? new Set<string>();

  const recompute = useCallback(() => {
    const c = containerRef.current;
    if (!c) return;
    const cr = c.getBoundingClientRect();
    const next: Line[] = [];
    Object.values(SKILLS).forEach((s) => {
      const fromEl = nodeRefs.current.get(s.id);
      if (!fromEl) return;
      const fr = fromEl.getBoundingClientRect();
      const x1 = fr.left - cr.left + fr.width / 2;
      const y1 = fr.top - cr.top + fr.height / 2;
      s.next.forEach((childId) => {
        const toEl = nodeRefs.current.get(childId);
        if (!toEl) return;
        const tr = toEl.getBoundingClientRect();
        const x2 = tr.left - cr.left + tr.width / 2;
        const y2 = tr.top - cr.top + tr.height / 2;
        next.push({
          id: `${s.id}-${childId}`,
          x1,
          y1,
          x2,
          y2,
          owned: owned.has(s.id) && owned.has(childId),
        });
      });
    });
    setLines(next);
  }, [owned]);

  useLayoutEffect(() => {
    recompute();
    const c = containerRef.current;
    if (!c) return;
    const ro = new ResizeObserver(recompute);
    ro.observe(c);
    window.addEventListener("resize", recompute);
    return () => {
      ro.disconnect();
      window.removeEventListener("resize", recompute);
    };
  }, [recompute]);

  const setRef = (id: string) => (el: HTMLDivElement | null) => {
    if (el) nodeRefs.current.set(id, el);
    else nodeRefs.current.delete(id);
  };

  return (
    <div ref={containerRef} className="relative">
      <svg className="absolute inset-0 w-full h-full pointer-events-none" style={{ overflow: "visible" }}>
        {lines.map((l) => (
          <line
            key={l.id}
            x1={l.x1}
            y1={l.y1}
            x2={l.x2}
            y2={l.y2}
            stroke={l.owned ? "#ffd98a" : "#3a4258"}
            strokeWidth={l.owned ? 2.5 : 2}
            strokeLinecap="round"
            opacity={l.owned ? 0.9 : 0.5}
          />
        ))}
      </svg>

      <div className="relative z-10 flex flex-col gap-12 py-4">
        {TIERS.map((row, ti) => (
          <div key={ti} className="flex flex-wrap justify-center gap-6 sm:gap-10">
            {row.map((id) => {
              const s = SKILLS[id];
              const isOwned = owned.has(id);
              const isAvail = !isOwned && availableSet.has(id);
              const isBurned = !isOwned && !isAvail && burnedSet.has(id);
              const isLast = lastPicked === id;
              const meta = BRANCH_META[s.branch];
              return (
                <div
                  key={id}
                  className="relative flex flex-col items-center gap-2"
                  onMouseEnter={() => setHovered(id)}
                  onMouseLeave={() => setHovered(null)}
                >
                  <div
                    ref={setRef(id)}
                    className={[
                      "grid place-items-center rounded-2xl border transition-all duration-200 text-[26px]",
                      s.isClass ? "px-4 h-14" : "w-14 h-14",
                      isOwned
                        ? "bg-[#1d2230] scale-105"
                        : isAvail
                          ? "bg-[#171b27]"
                          : isBurned
                            ? "bg-[#160f13] opacity-40 grayscale"
                            : "bg-[#12151c] opacity-45",
                      isLast ? "animate-pulse" : "",
                    ].join(" ")}
                    style={{
                      borderColor: isOwned
                        ? meta.color
                        : isAvail
                          ? "#3a4258"
                          : isBurned
                            ? "#5a2b39"
                            : "#222838",
                      boxShadow: isOwned
                        ? `0 0 22px ${meta.glow}`
                        : isAvail
                          ? "0 0 10px rgba(120,140,200,.15)"
                          : "none",
                    }}
                  >
                    {s.isClass ? (
                      <span
                        className="flex items-center gap-2 text-[15px] font-extrabold whitespace-nowrap"
                        style={{ color: isOwned ? meta.color : "#8a93a8" }}
                      >
                        {s.icon}
                        {s.name}
                      </span>
                    ) : (
                      <span>{s.icon}</span>
                    )}
                  </div>
                  {!s.isClass && (
                    <div
                      className={["text-[12px]", isBurned ? "line-through" : ""].join(" ")}
                      style={{ color: isOwned ? "#e7ebf3" : isBurned ? "#7d5560" : "#7f8aa3" }}
                    >
                      {s.name}
                    </div>
                  )}
                  {isBurned && !s.isClass && (
                    <div className="text-[9px] uppercase tracking-wider text-[#7d4a57] -mt-1.5">
                      missed
                    </div>
                  )}
                  {hovered === id && (
                    <div className="absolute z-30 top-full mt-2 left-1/2 -translate-x-1/2 pointer-events-none">
                      <SkillTooltip skill={s} />
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        ))}
      </div>
    </div>
  );
}
