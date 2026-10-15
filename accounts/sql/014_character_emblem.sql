-- A CHARACTER'S CAPE EMBLEM GETS ITS OWN ROW, the same shape as the hotbar's (008) and auto-combat's (013): written only
-- when the player uploads one (or a GM removes it), through its own server-to-server door, and read in the same query
-- that reads the character when it is handed to a map server.
--
-- `emblem` is one JSON object: { "v": 1, "png": "<base64>" } — a 24 x 24 PNG the map server has already checked and
-- re-encoded itself, so what is stored is exactly what is drawn. "png" is "" when the emblem was removed. A character
-- that never uploaded one has NO row, and the game reads that as "no emblem".
--
-- NOTHING IS LEFT BEHIND: the row is deleted with its character (`on delete cascade`), so it cannot outlive the
-- character it belongs to. A soft-deleted character keeps its row, exactly as it keeps its progress and its hotbar, until
-- the character row itself is purged.
create table if not exists character_emblem (
    character_id uuid        primary key references character (id) on delete cascade,
    emblem       jsonb       not null,
    updated_at   timestamptz not null default now()
);
