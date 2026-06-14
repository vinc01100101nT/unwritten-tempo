// Shared skill-graph dataset. BOTH the level-up draft and the skill tree read
// from this single source, so the two views can never disagree (PRD R1).

export type Branch =
  | "novice"
  | "pyromancer"
  | "berserker"
  | "templar"
  | "assassin"
  | "ranger"
  | "cryomancer";

export interface Skill {
  id: string;
  name: string;
  icon: string;
  tier: 0 | 1 | 2 | 3 | 4 | 5 | 6;
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
    next: ["ember", "slash", "guard", "dagger", "quickshot", "frostbite"],
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

  // ── Tier 3 base job classes (now a milestone, not the end of the path) ──
  pyromancer: {
    id: "pyromancer", name: "Pyromancer", icon: "🧙", tier: 3, branch: "pyromancer",
    desc: "Master of devastating fire magic.", stat: "JOB CLASS",
    next: ["conflagration", "soulflame"], isClass: true,
  },
  berserker: {
    id: "berserker", name: "Berserker", icon: "⚔️", tier: 3, branch: "berserker",
    desc: "An unstoppable engine of fury.", stat: "JOB CLASS",
    next: ["bloodlust", "beastwithin"], isClass: true,
  },
  templar: {
    id: "templar", name: "Templar", icon: "🏛️", tier: 3, branch: "templar",
    desc: "A holy bulwark for the party.", stat: "JOB CLASS",
    next: ["consecrate", "inquisition"], isClass: true,
  },

  // ── Tier 4 advanced techniques (each begins a divergent prestige path) ──
  conflagration: {
    id: "conflagration", name: "Conflagration", icon: "💥", tier: 4, branch: "pyromancer",
    desc: "Fire spreads and feeds on itself.", stat: "chain ignite", next: ["meteor"],
  },
  soulflame: {
    id: "soulflame", name: "Soulflame", icon: "👻", tier: 4, branch: "pyromancer",
    desc: "Flame that burns the soul, not the body.", stat: "life-steal burn", next: ["phylactery"],
  },
  bloodlust: {
    id: "bloodlust", name: "Bloodlust", icon: "💢", tier: 4, branch: "berserker",
    desc: "The more you kill, the harder you hit.", stat: "stacking rage", next: ["warcry"],
  },
  beastwithin: {
    id: "beastwithin", name: "Beast Within", icon: "🐺", tier: 4, branch: "berserker",
    desc: "Let the animal take over.", stat: "shapeshift", next: ["feral"],
  },
  consecrate: {
    id: "consecrate", name: "Consecration", icon: "✨", tier: 4, branch: "templar",
    desc: "Sanctify the ground you stand on.", stat: "holy zone", next: ["divinelight"],
  },
  inquisition: {
    id: "inquisition", name: "Inquisition", icon: "🗝️", tier: 4, branch: "templar",
    desc: "Root out and punish the wicked.", stat: "anti-heal", next: ["judgment"],
  },

  // ── Tier 5 masteries ───────────────────────────────────────────────
  meteor: {
    id: "meteor", name: "Meteor Storm", icon: "🌠", tier: 5, branch: "pyromancer",
    desc: "Call the sky down in flames.", stat: "screen-wide fire", next: ["archmage"],
  },
  phylactery: {
    id: "phylactery", name: "Phylactery", icon: "⚱️", tier: 5, branch: "pyromancer",
    desc: "Bind your life to an object — cheat death.", stat: "auto-revive", next: ["lich"],
  },
  warcry: {
    id: "warcry", name: "War Cry", icon: "📣", tier: 5, branch: "berserker",
    desc: "Rally allies and rout the weak.", stat: "team haste", next: ["warlord"],
  },
  feral: {
    id: "feral", name: "Feral Rage", icon: "🐾", tier: 5, branch: "berserker",
    desc: "Pure, unthinking savagery.", stat: "frenzy crits", next: ["ravager"],
  },
  divinelight: {
    id: "divinelight", name: "Divine Light", icon: "🌟", tier: 5, branch: "templar",
    desc: "Channel the heavens to mend and smite.", stat: "mass heal", next: ["paladin"],
  },
  judgment: {
    id: "judgment", name: "Judgment", icon: "🔨", tier: 5, branch: "templar",
    desc: "Weigh every soul and find it wanting.", stat: "execute low HP", next: ["justicar"],
  },

  // ── Tier 6 prestige job classes (terminal) ─────────────────────────
  archmage: {
    id: "archmage", name: "Archmage", icon: "🪄", tier: 6, branch: "pyromancer",
    desc: "Supreme master of the arcane.", stat: "PRESTIGE CLASS", next: [], isClass: true,
  },
  lich: {
    id: "lich", name: "Lich", icon: "💀", tier: 6, branch: "pyromancer",
    desc: "Undying sorcerer-king.", stat: "PRESTIGE CLASS", next: [], isClass: true,
  },
  warlord: {
    id: "warlord", name: "Warlord", icon: "👑", tier: 6, branch: "berserker",
    desc: "Commander of the battlefield.", stat: "PRESTIGE CLASS", next: [], isClass: true,
  },
  ravager: {
    id: "ravager", name: "Ravager", icon: "🐗", tier: 6, branch: "berserker",
    desc: "A living avalanche of destruction.", stat: "PRESTIGE CLASS", next: [], isClass: true,
  },
  paladin: {
    id: "paladin", name: "Paladin", icon: "😇", tier: 6, branch: "templar",
    desc: "Holy champion and protector.", stat: "PRESTIGE CLASS", next: [], isClass: true,
  },
  justicar: {
    id: "justicar", name: "Justicar", icon: "⚖️", tier: 6, branch: "templar",
    desc: "The unyielding hand of judgment.", stat: "PRESTIGE CLASS", next: [], isClass: true,
  },

  // ── Wildcard archetypes ────────────────────────────────────────────
  // Extra starting paths the wildcard can fling the player onto, each leading to
  // its own job class. Kept compact (starter → 2 specs → class) so the tree stays
  // readable; they give the random wildcard somewhere genuinely different to go.

  // Assassin path (shadow)
  dagger: {
    id: "dagger", name: "Dagger", icon: "🔪", tier: 1, branch: "assassin",
    desc: "A quick, quiet blade.", stat: "+10% crit", next: ["backstab", "shadowstep"],
  },
  backstab: {
    id: "backstab", name: "Backstab", icon: "🗯️", tier: 2, branch: "assassin",
    desc: "Strike from the dark.", stat: "x3 from behind", next: ["assassin"],
  },
  shadowstep: {
    id: "shadowstep", name: "Shadowstep", icon: "🌫️", tier: 2, branch: "assassin",
    desc: "Blink through the shadows.", stat: "teleport strike", next: ["assassin"],
  },

  // Ranger path (nature / bow)
  quickshot: {
    id: "quickshot", name: "Quick Shot", icon: "🏹", tier: 1, branch: "ranger",
    desc: "Loose an arrow in a blink.", stat: "+15% fire rate", next: ["volley", "trap"],
  },
  volley: {
    id: "volley", name: "Volley", icon: "🎯", tier: 2, branch: "ranger",
    desc: "A rain of arrows.", stat: "AoE volley", next: ["ranger"],
  },
  trap: {
    id: "trap", name: "Trap", icon: "🪤", tier: 2, branch: "ranger",
    desc: "Snare your prey.", stat: "root + dmg", next: ["ranger"],
  },

  // Cryomancer path (ice)
  frostbite: {
    id: "frostbite", name: "Frostbite", icon: "❄️", tier: 1, branch: "cryomancer",
    desc: "A numbing, creeping cold.", stat: "+12 frost dmg", next: ["blizzard", "iceshard"],
  },
  blizzard: {
    id: "blizzard", name: "Blizzard", icon: "🌨️", tier: 2, branch: "cryomancer",
    desc: "Call down a storm of ice.", stat: "AoE slow", next: ["cryomancer"],
  },
  iceshard: {
    id: "iceshard", name: "Ice Shard", icon: "🧊", tier: 2, branch: "cryomancer",
    desc: "A piercing lance of ice.", stat: "armor pierce", next: ["cryomancer"],
  },

  // New base job classes (terminal for now)
  assassin: {
    id: "assassin", name: "Assassin", icon: "🥷", tier: 3, branch: "assassin",
    desc: "Death from the shadows.", stat: "JOB CLASS", next: [], isClass: true,
  },
  ranger: {
    id: "ranger", name: "Ranger", icon: "🦌", tier: 3, branch: "ranger",
    desc: "Master of bow and beast.", stat: "JOB CLASS", next: [], isClass: true,
  },
  cryomancer: {
    id: "cryomancer", name: "Cryomancer", icon: "🥶", tier: 3, branch: "cryomancer",
    desc: "Master of ice and absolute cold.", stat: "JOB CLASS", next: [], isClass: true,
  },
};

// Tier rows, left→right, used by the tree layout. Columns are grouped by
// archetype so each path reads top-to-bottom. The three deep paths (pyromancer ·
// berserker · templar) run all the way to tier 6; the three wildcard paths
// (assassin · ranger · cryomancer) end at their tier-3 base class.
export const TIERS: string[][] = [
  ["novice"],
  ["ember", "slash", "guard", "dagger", "quickshot", "frostbite"],
  ["magma", "comet", "flurry", "rend", "bulwark", "aegis", "backstab", "shadowstep", "volley", "trap", "blizzard", "iceshard"],
  ["pyromancer", "berserker", "templar", "assassin", "ranger", "cryomancer"],
  ["conflagration", "soulflame", "bloodlust", "beastwithin", "consecrate", "inquisition"],
  ["meteor", "phylactery", "warcry", "feral", "divinelight", "judgment"],
  ["archmage", "lich", "warlord", "ravager", "paladin", "justicar"],
];

export const ALL_IDS = Object.keys(SKILLS);

export const BRANCH_META: Record<Branch, { label: string; color: string; glow: string }> = {
  novice: { label: "Novice", color: "#cbd5e1", glow: "rgba(203,213,225,.4)" },
  pyromancer: { label: "Pyromancer", color: "#ff7a3c", glow: "rgba(255,122,60,.45)" },
  berserker: { label: "Berserker", color: "#ff5470", glow: "rgba(255,84,112,.45)" },
  templar: { label: "Templar", color: "#5fa8ff", glow: "rgba(95,168,255,.45)" },
  assassin: { label: "Assassin", color: "#a78bfa", glow: "rgba(167,139,250,.45)" },
  ranger: { label: "Ranger", color: "#4ade80", glow: "rgba(74,222,128,.45)" },
  cryomancer: { label: "Cryomancer", color: "#67e8f9", glow: "rgba(103,232,249,.45)" },
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
 * A skill is eligible to be drafted when it is reachable (a neighbour of an
 * owned skill), not already owned, NOT a job class (classes are auto-granted,
 * never drafted), and has not been offered before. The last clause is the
 * "no-repeat" rule: once a skill is shown and passed over it is burned for the
 * rest of the run and never re-appears in a future draw.
 */
const isDraftable = (s: Skill, owned: Set<string>, seen: Set<string>) =>
  !!s && !s.isClass && !owned.has(s.id) && !seen.has(s.id);

/** True if the player already owns at least one skill on `branch`. */
const ownsBranch = (owned: Set<string>, branch: Branch): boolean => {
  for (const id of owned) if (SKILLS[id]?.branch === branch) return true;
  return false;
};

/**
 * Build a level-up draw — ALWAYS 2 guaranteed + 1 wildcard (a full tray of 3),
 * never fewer. Every option is a real, reachable node (PRD R1) that hasn't been
 * offered before (`seen`, the no-repeat ledger) and is never a job class (those
 * are revealed automatically).
 *
 * - Guaranteed = the 2 strongest on-path continuations of the current build.
 * - Wildcard = a RANDOM option that leads to a different path. It prefers a
 *   branch the player isn't on, and within that a fresh archetype entry (a
 *   tier-1 starter of a path they haven't touched) so it can truly pivot the run.
 *
 * A level-up is 3 cards or nothing: when fewer than 3 draftable skills remain we
 * return null and the run is over (it never shows a partial tray of 1 or 2).
 */
export function getDraft(owned: Set<string>, seen: Set<string> = new Set()): Draft | null {
  const pool = neighborsOf(owned)
    .map((id) => SKILLS[id])
    .filter((s) => isDraftable(s, owned, seen));
  if (pool.length < 3) return null;

  const dom = dominantBranch(owned);
  const affinity = (s: Skill) => (dom && s.branch === dom ? 1 : 0);

  // Guaranteed: 2 strongest on-path picks (dominant branch first, then deeper tier).
  const guaranteed = [...pool]
    .sort((a, b) => (affinity(b) !== affinity(a) ? affinity(b) - affinity(a) : b.tier - a.tier))
    .slice(0, 2);

  // Wildcard: a random pivot. Prefer off-path branches, then fresh archetype entries.
  const taken = new Set(guaranteed.map((s) => s.id));
  const rest = pool.filter((s) => !taken.has(s.id));
  const offPath = rest.filter((s) => s.branch !== "novice" && s.branch !== dom);
  const freshEntries = offPath.filter((s) => s.tier === 1 && !ownsBranch(owned, s.branch));
  const wildPool = freshEntries.length ? freshEntries : offPath.length ? offPath : rest;
  const wildcard = pickRandom(wildPool);

  return { guaranteed, wildcard };
}

/** The job class auto-granted by owning `skillId`, if any (else null). */
export function classUnlockedBy(skillId: string): Skill | null {
  const s = SKILLS[skillId];
  if (!s) return null;
  const cls = s.next.map((id) => SKILLS[id]).find((n) => n?.isClass);
  return cls ?? null;
}
