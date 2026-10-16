-- A CHARACTER'S AUTO-COMBAT SETTINGS GET THEIR OWN ROW, the same shape as the hotbar's (008): written only when the
-- player changes a setting, through its own server-to-server door, and read in the same query that reads the
-- character when it is handed to a map server.
--
-- `settings` is the settings exactly as the game sends them, one JSON object:
--   { "v": 1, "slots": [4 skill ids or ""], "lootRateAtMostPercent": 0-100, "lootGears": bool, "lootConsumables": bool,
--     "lootMaterials": bool, "lootAll": bool, "hpPotionBelowPercent": 0-100, "mpPotionBelowPercent": 0-100,
--     "huntMode": 0 Standing / 1 Radius / 2 Free Roam }
-- A character that never changed a setting has NO row, and the game reads that as "never set": empty slots,
-- auto-loot everything at 100%, a healing item below 70% health, no mana draught, Free Roam.
--
-- NOTHING IS LEFT BEHIND: the row is deleted with its character (`on delete cascade`), so it cannot outlive the
-- character it belongs to. A soft-deleted character keeps its row, exactly as it keeps its progress and its hotbar, until
-- the character row itself is purged.
create table if not exists character_auto_combat (
    character_id uuid        primary key references character (id) on delete cascade,
    settings     jsonb       not null,
    updated_at   timestamptz not null default now()
);
