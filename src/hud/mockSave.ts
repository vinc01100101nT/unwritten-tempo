import type { BaseStats, EquipmentInstance, EquipSlot, InvStack } from "./items";

/** One consistent mock character shared by all three windows + the canvas. */

export const MOCK_BASE: BaseStats = { STR: 40, AGI: 30, VIT: 20, INT: 10, DEX: 25, LUK: 5 };

export const MOCK_IDENTITY = {
  name: "Aldric",
  className: "Swordsman",
  level: 15,
  xpPct: 62,
  statPoints: 8,
};

export const MOCK_EQUIPPED: Partial<Record<EquipSlot, EquipmentInstance>> = {
  weapon: { defId: 1001, upgrade: 7, cards: [101, 102] }, // +7 Iron Sword, 2/3 cards
  head: { defId: 2002, upgrade: 4, cards: [] }, // +4 Feather Beret, 0/1
  armor: { defId: 3001, upgrade: 5, cards: [] }, // +5 Chain Mail
  garment: { defId: 4001, upgrade: 0, cards: [] },
  shield: { defId: 5001, upgrade: 0, cards: [] },
  acc1: { defId: 6001, upgrade: 0, cards: [] },
  acc2: { defId: 6002, upgrade: 0, cards: [] },
};

/** Flatten the equipped map to the list the pure engine consumes. */
export function equippedList(
  map: Partial<Record<EquipSlot, EquipmentInstance>> = MOCK_EQUIPPED,
): EquipmentInstance[] {
  return Object.values(map).filter(Boolean) as EquipmentInstance[];
}

export const MOCK_GOLD = 128_530;
export const MOCK_WEIGHT_CAP = 1000;

export const MOCK_INVENTORY: InvStack[] = [
  // Two upgrade candidates so the hover delta-preview has something to show:
  { defId: 1003, amount: 1, instance: { defId: 1003, upgrade: 0, cards: [] }, isNew: true }, // Dawnbreaker (legendary)
  { defId: 1002, amount: 1, instance: { defId: 1002, upgrade: 9, cards: [103] } }, // +9 Cursed Falchion (epic)
  { defId: 2001, amount: 1, instance: { defId: 2001, upgrade: 0, cards: [] } }, // Leather Cap
  { defId: 7001, amount: 30 }, // Red Potion
  { defId: 7002, amount: 12 }, // Blue Potion
  { defId: 8001, amount: 47 }, // Iron Ore
  { defId: 8002, amount: 2 }, // Emperium Shard
  { defId: 9001, amount: 1, isNew: true }, // Old Blue Box
];
