// Shared skill-graph dataset. BOTH the level-up draft and the skill tree read
// from this single source, so the two views can never disagree (PRD R1).

export type Branch = "novice" | "pyromancer" | "berserker" | "templar";

export interface Skill {
  id: string;
  name: string;
  icon: string;
  tier: 0 | 1 | 2 | 3;
  branch: Branch;
  desc: string;
  stat: string;
  next: string[]; // child skill ids (directed edges)
  isClass?: boolean;
}

export const SKILLS: Record<string, Skill> = {
  novice: {
    id: "novice", name: "Novice", icon: "⭐", tier: 0, branch: "novice",
    desc: "A blank slate, full of potential.", stat: "Lv.1 start",
    next: ["ember", "slash", "guard"],
  },

  // ── Tier 1 archetypes ──────────────────────────────────────────────
  ember: {
    id: "ember", name: "Ember", icon: "🔥", tier: 1, branch: "pyromancer",
    desc: "Hurl a bolt of fire.", stat: "+18 fire dmg", next: ["magma", "comet"],
  },
  slash: {
    id: "slash", name: "Slash", icon: "🗡️", tier: 1, branch: "berserker",
    desc: "A swift opening strike.", stat: "+12% atk speed", next: ["flurry", "rend"],
  },
  guard: {
    id: "guard", name: "Guard", icon: "🛡️", tier: 1, branch: "templar",
    desc: "Raise your shield.", stat: "+20 block", next: ["bulwark", "aegis"],
  },

  // ── Tier 2 specializations ─────────────────────────────────────────
  magma: {
    id: "magma", name: "Magma Burst", icon: "🌋", tier: 2, branch: "pyromancer",
    desc: "Erupt a pool of molten fire.", stat: "AoE burn", next: ["pyromancer"],
  },
  comet: {
    id: "comet", name: "Comet Call", icon: "☄️", tier: 2, branch: "pyromancer",
    desc: "Summon a falling star.", stat: "huge single hit", next: ["pyromancer"],
  },
  flurry: {
    id: "flurry", name: "Flurry", icon: "⚡", tier: 2, branch: "berserker",
    desc: "A barrage of rapid blows.", stat: "5 rapid hits", next: ["berserker"],
  },
  rend: {
    id: "rend", name: "Rend", icon: "🩸", tier: 2, branch: "berserker",
    desc: "Tear a bleeding wound.", stat: "bleed DoT", next: ["berserker"],
  },
  bulwark: {
    id: "bulwark", name: "Bulwark", icon: "🧱", tier: 2, branch: "templar",
    desc: "Become an immovable wall.", stat: "+50% block", next: ["templar"],
  },
  aegis: {
    id: "aegis", name: "Aegis", icon: "🔰", tier: 2, branch: "templar",
    desc: "Project a holy barrier.", stat: "team shield", next: ["templar"],
  },

  // ── Tier 3 job classes (terminal) ──────────────────────────────────
  pyromancer: {
    id: "pyromancer", name: "Pyromancer", icon: "🧙", tier: 3, branch: "pyromancer",
    desc: "Master of devastating fire magic.", stat: "JOB CLASS", next: [], isClass: true,
  },
  berserker: {
    id: "berserker", name: "Berserker", icon: "⚔️", tier: 3, branch: "berserker",
    desc: "An unstoppable engine of fury.", stat: "JOB CLASS", next: [], isClass: true,
  },
  templar: {
    id: "templar", name: "Templar", icon: "🏛️", tier: 3, branch: "templar",
    desc: "A holy bulwark for the party.", stat: "JOB CLASS", next: [], isClass: true,
  },
};

// Tier rows, left→right, used by the tree layout.
export const TIERS: string[][] = [
  ["novice"],
  ["ember", "slash", "guard"],
  ["magma", "comet", "flurry", "rend", "bulwark", "aegis"],
  ["pyromancer", "berserker", "templar"],
];

export const ALL_IDS = Object.keys(SKILLS);

export const BRANCH_META: Record<Branch, { label: string; color: string; glow: string }> = {
  novice: { label: "Novice", color: "#cbd5e1", glow: "rgba(203,213,225,.4)" },
  pyromancer: { label: "Pyromancer", color: "#ff7a3c", glow: "rgba(255,122,60,.45)" },
  berserker: { label: "Berserker", color: "#ff5470", glow: "rgba(255,84,112,.45)" },
  templar: { label: "Templar", color: "#5fa8ff", glow: "rgba(95,168,255,.45)" },
};

export const getSkill = (id: string): Skill => SKILLS[id];

/** Convenience for building an owned-set (used by canvas storyboards/tests). */
export const makeOwned = (...ids: string[]): Set<string> => new Set(ids);

/** The job class a branch leads to (null for the novice / neutral start). */
export const classOf = (branch: Branch): Skill | null =>
  branch === "novice" ? null : SKILLS[branch];

/** Un-owned children of owned skills — the candidate pool for a draw. */
export function neighborsOf(owned: Set<string>): string[] {
  const out = new Set<string>();
  owned.forEach((id) => {
    SKILLS[id]?.next.forEach((n) => {
      if (!owned.has(n)) out.add(n);
    });
  });
  return [...out];
}

/** Branch the player has invested in most (null if none / tied). */
export function dominantBranch(owned: Set<string>): Branch | null {
  const count: Partial<Record<Branch, number>> = {};
  owned.forEach((id) => {
    const b = SKILLS[id]?.branch;
    if (b && b !== "novice") count[b] = (count[b] ?? 0) + 1;
  });
  let best: Branch | null = null;
  let max = 0;
  let tie = false;
  (Object.keys(count) as Branch[]).forEach((b) => {
    const v = count[b] ?? 0;
    if (v > max) {
      max = v;
      best = b;
      tie = false;
    } else if (v === max) {
      tie = true;
    }
  });
  return tie ? null : best;
}

export function headingToward(owned: Set<string>) {
  const b = dominantBranch(owned);
  return b ? BRANCH_META[b] : null;
}

export interface Draft {
  guaranteed: Skill[];
  wildcard: Skill | null;
}

const pickRandom = <T,>(arr: T[]): T => arr[Math.floor(Math.random() * arr.length)];

/**
 * Build a level-up draw: 2 guaranteed on-path options + 1 random wildcard.
 * Every option is a real node in the graph above (PRD R1). The wildcard
 * prefers a branch other than the player's current path, so it can pivot them.
 */
export function getDraft(owned: Set<string>): Draft | null {
  const allUnowned = ALL_IDS.filter((id) => !owned.has(id)).map((id) => SKILLS[id]);
  if (allUnowned.length === 0) return null;

  const pool = neighborsOf(owned).map((id) => SKILLS[id]);
  const dom = dominantBranch(owned);

  // Guaranteed: prefer neighbors on the dominant branch, then deeper tier.
  const affinity = (s: Skill) => (dom && s.branch === dom ? 1 : 0);
  const sorted = [...pool].sort((a, b) =>
    affinity(b) !== affinity(a) ? affinity(b) - affinity(a) : b.tier - a.tier,
  );

  const guaranteed: Skill[] = [];
  for (const s of sorted) {
    if (guaranteed.length < 2 && !guaranteed.includes(s)) guaranteed.push(s);
  }
  // Backfill from anything un-owned if the neighbour pool is too small.
  if (guaranteed.length < 2) {
    for (const s of allUnowned) {
      if (guaranteed.length < 2 && !guaranteed.includes(s)) guaranteed.push(s);
    }
  }

  // Wildcard: a real un-owned node not already offered, ideally off-path.
  const taken = new Set(guaranteed.map((s) => s.id));
  const wildPool = allUnowned.filter((s) => !taken.has(s.id));
  let wildcard: Skill | null = null;
  if (wildPool.length) {
    const offBranch = wildPool.filter((s) => s.branch !== "novice" && s.branch !== dom);
    wildcard = offBranch.length ? pickRandom(offBranch) : pickRandom(wildPool);
  }

  return { guaranteed, wildcard };
}
