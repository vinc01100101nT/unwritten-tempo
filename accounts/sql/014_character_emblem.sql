-- A CHARACTER'S CAPE EMBLEM GETS ITS OWN ROW, the same shape as the hotbar's (008) and auto-combat's (013): written only
-- when the player uploads one (or a GM removes it), through its own server-to-server door, and read in the same query
-- that reads the character when it is handed to a map server.
--
-- `emblem` is one JSON object: { "v": 1, "png": "<base64>", "colours": "RRGGBB,RRGGBB,RRGGBB,0|1" } — a 24 x 24 PNG the
-- map server has already checked and re-encoded itself, so what is stored is exactly what is drawn, and the cape's
-- cloth, trim and hem colours with the gradient on or off. "png" is "" when there is no emblem and "colours" is "" (or
-- absent) for the cape's own colours. A character that never set either has NO row, and the game reads that as neither.
--
-- NOTHING IS LEFT BEHIND: the row is deleted with its character (`on delete cascade`), so it cannot outlive the
-- character it belongs to. A soft-deleted character keeps its row, exactly as it keeps its progress and its hotbar, until
-- the character row itself is purged.
create table if not exists character_emblem (
    character_id uuid        primary key references character (id) on delete cascade,
    emblem       jsonb       not null,
    updated_at   timestamptz not null default now()
);
