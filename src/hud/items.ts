// ─────────────────────────────────────────────────────────────────────────────
// HUD / progression data model — the locked spec from the GPT⇄Claude relay.
//
//   Visual channels stay INDEPENDENT (never blended):
//     • border color = Rarity        (what it is)
//     • +N badge     = Enhancement   (what you did to it)
//     • ◇ pips       = Card slots    (always neutral, never rarity-tinted)
//     • icon         = Item identity (untouched — no glow spam)
//
// This is a UI-only prototype: definitions + instances + a PURE derived-stat
// function so the windows can preview "equip this → stat delta" without ever
// mutating the live character. In Unity these map to ScriptableObjects +
// EquipmentInstance saves + a pure ComputeDerived().
// ─────────────────────────────────────────────────────────────────────────────

export type Rarity = "common" | "uncommon" | "rare" | "epic" | "legendary";

export interface RarityStyle {
  label: string;
  /** Border + tooltip header color = the single rarity signal. */
  border: string;
  /** Name text color in tooltips. */
  text: string;
  /** Soft outer glow (Legendary screenshot-worthy; Common = none). */
  glow: string;
}

// Gold (NOT Diablo orange) for Legendary — fits the RO fantasy + the +N
// enhancement language better.
export const RARITY: Record<Rarity, RarityStyle> = {
  common: { label: "Common", border: "#4b5563", text: "#cbd2e0", glow: "transparent" },
  uncommon: { label: "Uncommon", border: "#46d17f", text: "#a8f0c5", glow: "rgba(70,209,127,.28)" },
  rare: { label: "Rare", border: "#4f8cff", text: "#bcd4ff", glow: "rgba(79,140,255,.30)" },
  epic: { label: "Epic", border: "#a855f7", text: "#e6c6ff", glow: "rgba(168,85,247,.34)" },
  legendary: { label: "Legendary", border: "#f5b942", text: "#ffe2a6", glow: "rgba(245,185,66,.40)" },
};

// ── Stats ────────────────────────────────────────────────────────────────────

export type BaseStatKey = "STR" | "AGI" | "VIT" | "INT" | "DEX" | "LUK";
export const BASE_STATS: BaseStatKey[] = ["STR", "AGI", "VIT", "INT", "DEX", "LUK"];
export const STAT_ICON: Record<BaseStatKey, string> = {
  STR: "⚔",
  AGI: "🏹",
  VIT: "🛡",
  INT: "✨",
  DEX: "👁",
  LUK: "🍀",
};

export type BaseStats = Record<BaseStatKey, number>;

export interface Derived {
  ATK: number;
  MATK: number;
  DEF: number;
  MDEF: number;
  HIT: number;
  FLEE: number;
  CRIT: number; // %
  ASPD: number;
  MoveSpeed: number;
  CastSpeed: number; // % reduction
}

export const DERIVED_ORDER: { key: keyof Derived; label: string; suffix?: string }[] = [
  { key: "ATK", label: "ATK" },
  { key: "MATK", label: "MATK" },
  { key: "DEF", label: "DEF" },
  { key: "MDEF", label: "MDEF" },
  { key: "HIT", label: "HIT" },
  { key: "FLEE", label: "FLEE" },
  { key: "CRIT", label: "CRIT", suffix: "%" },
  { key: "ASPD", label: "ASPD" },
  { key: "MoveSpeed", label: "Move Speed" },
  { key: "CastSpeed", label: "Cast Speed", suffix: "%" },
];

// ── Equipment slots (7 visible + hidden costume room) ─────────────────────────

export type EquipSlot =
  | "head"
  | "armor"
  | "garment"
  | "weapon"
  | "shield"
  | "acc1"
  | "acc2"
  // Reserved in schema, NOT shown in v1 UI — a future cosmetic layer slots in
  // here without a save redesign.
  | "costumeHead"
  | "costumeBody"
  | "costumeBack";

export const VISIBLE_SLOTS: { slot: EquipSlot; label: string; empty: string }[] = [
  { slot: "head", label: "Head", empty: "🪖" },
  { slot: "armor", label: "Armor", empty: "🥋" },
  { slot: "garment", label: "Garment", empty: "🧣" },
  { slot: "weapon", label: "Weapon", empty: "🗡" },
  { slot: "shield", label: "Shield", empty: "🛡" },
  { slot: "acc1", label: "Accessory 1", empty: "💍" },
  { slot: "acc2", label: "Accessory 2", empty: "📿" },
];

// ── Definitions (≈ ScriptableObjects) ─────────────────────────────────────────

export type ItemType = "equip" | "use" | "material" | "etc";

export interface ItemDef {
  id: number;
  name: string;
  icon: string;
  type: ItemType;
  rarity: Rarity;
  slot?: EquipSlot;
  cardSlots?: number;
  weight: number;
  value: number; // zeny
  atk?: number;
  matk?: number;
  def?: number;
  mdef?: number;
  bonuses?: Partial<BaseStats> & { aspd?: number; crit?: number };
  desc?: string;
}

export interface CardDef {
  id: number;
  name: string;
  icon: string;
  bonuses?: Partial<BaseStats> & { atk?: number; aspd?: number; crit?: number };
  blurb: string;
}

/** A unique, upgradeable instance of an equip — what actually lives in a save. */
export interface EquipmentInstance {
  defId: number;
  upgrade: number; // +N enhancement
  cards: number[]; // CardDef ids, length ≤ def.cardSlots
}

/** A stack in the bag — equips carry their instance data, others just count. */
export interface InvStack {
  defId: number;
  amount: number;
  instance?: EquipmentInstance;
  isNew?: boolean;
}

// ── Mock content ──────────────────────────────────────────────────────────────

export const ITEMS: Record<number, ItemDef> = {
  1001: { id: 1001, name: "Iron Sword", icon: "🗡", type: "equip", rarity: "rare", slot: "weapon", cardSlots: 3, weight: 90, value: 5000, atk: 145, bonuses: { STR: 3 }, desc: "A dependable blade. The leveling sweet spot." },
  1002: { id: 1002, name: "Cursed Falchion", icon: "⚔️", type: "equip", rarity: "epic", slot: "weapon", cardSlots: 2, weight: 110, value: 22000, atk: 168, bonuses: { STR: 5, aspd: 4 }, desc: "Hums faintly. Build-defining for crit swordsmen." },
  1003: { id: 1003, name: "Dawnbreaker", icon: "🌟", type: "equip", rarity: "legendary", slot: "weapon", cardSlots: 3, weight: 130, value: 0, atk: 205, bonuses: { STR: 8, crit: 5 }, desc: "Named drop from the Sunken Throne. Screenshot-worthy." },
  2001: { id: 2001, name: "Leather Cap", icon: "🪖", type: "equip", rarity: "common", slot: "head", weight: 40, value: 600, def: 8, desc: "Basic head protection." },
  2002: { id: 2002, name: "Feather Beret", icon: "🎩", type: "equip", rarity: "uncommon", slot: "head", cardSlots: 1, weight: 35, value: 2400, def: 10, bonuses: { AGI: 2 }, desc: "Light and breezy." },
  3001: { id: 3001, name: "Chain Mail", icon: "🥋", type: "equip", rarity: "rare", slot: "armor", cardSlots: 1, weight: 260, value: 6800, def: 42, bonuses: { VIT: 2 }, desc: "Solid mid-game armor." },
  4001: { id: 4001, name: "Wool Manteau", icon: "🧣", type: "equip", rarity: "uncommon", slot: "garment", weight: 60, value: 1800, def: 12, bonuses: { VIT: 1 }, desc: "Keeps the chill off." },
  5001: { id: 5001, name: "Round Shield", icon: "🛡", type: "equip", rarity: "common", slot: "shield", weight: 130, value: 1100, def: 24, desc: "Standard issue." },
  6001: { id: 6001, name: "Clip of Vigor", icon: "💍", type: "equip", rarity: "rare", slot: "acc1", cardSlots: 1, weight: 10, value: 4200, bonuses: { STR: 2, VIT: 1 }, desc: "A warm, steady ring." },
  6002: { id: 6002, name: "Gleam Earring", icon: "📿", type: "equip", rarity: "epic", slot: "acc2", weight: 8, value: 9000, bonuses: { DEX: 3, crit: 2 }, desc: "Catches the light just so." },
  7001: { id: 7001, name: "Red Potion", icon: "🧪", type: "use", rarity: "common", weight: 7, value: 50, desc: "Restores 45 HP." },
  7002: { id: 7002, name: "Blue Potion", icon: "🔵", type: "use", rarity: "uncommon", weight: 9, value: 120, desc: "Restores 40 SP." },
  8001: { id: 8001, name: "Iron Ore", icon: "🪨", type: "material", rarity: "common", weight: 15, value: 80, desc: "Refining material." },
  8002: { id: 8002, name: "Emperium Shard", icon: "💎", type: "material", rarity: "epic", weight: 5, value: 0, desc: "Glows with contained power." },
  9001: { id: 9001, name: "Old Blue Box", icon: "📦", type: "etc", rarity: "uncommon", weight: 25, value: 0, desc: "Who knows what's inside?" },
};

export const CARDS: Record<number, CardDef> = {
  101: { id: 101, name: "Andre Card", icon: "🐜", bonuses: { atk: 20 }, blurb: "+20 ATK" },
  102: { id: 102, name: "Poring Card", icon: "🍡", bonuses: { LUK: 2 }, blurb: "+2 LUK" },
  103: { id: 103, name: "Zipper Bear Card", icon: "🐻", bonuses: { atk: 30, aspd: -3 }, blurb: "+30 ATK, −3 ASPD" },
  104: { id: 104, name: "Whisper Card", icon: "👻", bonuses: { aspd: 6 }, blurb: "+6 ASPD" },
};

// ── The PURE derived-stat engine (the keystone) ───────────────────────────────
//
// computeDerived(base, equipped) → Derived, no side effects. Powers:
//   • the live Character panel        = computeDerived(base, currentSet)
//   • the hover preview               = computeDerived(base, set w/ item swapped)
//   • the equip-compare tooltip       = diff of two such calls
// ──────────────────────────────────────────────────────────────────────────────

function add(a: Partial<BaseStats> | undefined, into: BaseStats) {
  if (!a) return;
  for (const k of BASE_STATS) if (a[k]) into[k] += a[k]!;
}

export function computeDerived(base: BaseStats, equipped: EquipmentInstance[]): Derived {
  // 1) effective stats = base + all gear/card stat bonuses
  const s: BaseStats = { ...base };
  let flatAtk = 0;
  let flatAspd = 0;
  let flatCrit = 0;
  let weaponAtk = 0;
  let defSum = 0;
  let mdefSum = 0;
  let matkSum = 0;

  for (const inst of equipped) {
    const def = ITEMS[inst.defId];
    if (!def) continue;
    add(def.bonuses, s);
    flatAspd += def.bonuses?.aspd ?? 0;
    flatCrit += def.bonuses?.crit ?? 0;
    defSum += def.def ?? 0;
    mdefSum += def.mdef ?? 0;
    matkSum += def.matk ?? 0;
    // weapon atk scales with +N enhancement (~+3 per level)
    if (def.atk) weaponAtk += def.atk + inst.upgrade * 3;
    defSum += inst.upgrade; // refine adds a touch of def too
    for (const cid of inst.cards) {
      const c = CARDS[cid];
      if (!c) continue;
      add(c.bonuses, s);
      flatAtk += c.bonuses?.atk ?? 0;
      flatAspd += c.bonuses?.aspd ?? 0;
      flatCrit += c.bonuses?.crit ?? 0;
    }
  }

  const r = Math.round;
  return {
    ATK: r(s.STR * 2 + weaponAtk + flatAtk),
    MATK: r(s.INT * 2 + matkSum),
    DEF: r(defSum + s.VIT * 0.5),
    MDEF: r(mdefSum + s.INT * 0.3),
    HIT: r(80 + s.DEX),
    FLEE: r(80 + s.AGI),
    CRIT: Math.round((s.LUK * 0.3 + flatCrit) * 10) / 10,
    ASPD: r(150 + s.AGI * 0.2 + flatAspd),
    MoveSpeed: 100,
    CastSpeed: Math.round(s.DEX * 0.4 * 10) / 10,
  };
}

export function totalWeight(stacks: InvStack[]): number {
  return stacks.reduce((w, st) => w + (ITEMS[st.defId]?.weight ?? 0) * st.amount, 0);
}
