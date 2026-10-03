-- THE HOTBAR GETS ITS OWN ROW. It used to live inside the progress document, and the map server writes that whole
-- document every fifteen seconds whether or not anything on the bar moved — so every one of those saves rewrote
-- the bar too. Here it is written only when a slot actually changes, through its own server-to-server door, and
-- the character server reads it in the same query that reads the character when it hands them to a map server.
--
-- `slots` is the bar exactly as the game sends it: a JSON array of strings, one per slot.
--
-- NOTHING IS LEFT BEHIND: the row is deleted with its character (`on delete cascade`), so it cannot outlive the
-- character it belongs to. A soft-deleted character keeps its row, exactly as it keeps its progress, until the
-- character row itself is purged.
create table if not exists character_hotbar (
    character_id uuid        primary key references character (id) on delete cascade,
    slots        jsonb       not null,
    updated_at   timestamptz not null default now()
);
