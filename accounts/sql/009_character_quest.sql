-- ONE ROW PER QUEST A CHARACTER HAS EVER BEEN GIVEN — open or finished. Not inside the progress document: a finished
-- game holds hundreds of finished quests per character, and the map server rewrites that document every fifteen
-- seconds. Here a row is written only when it changes.
--
-- WRITTEN IN THE SAME TRANSACTION AS THE PROGRESS DOCUMENT, through the same door (PUT …/progress carries the changed
-- rows beside the progress), so the bag and the quest that judged it can never land apart: a quest that finished on
-- the bag it saw is written with that bag or not at all.
--
--   step_id      the step the character is on; stable ids, so a step inserted into a live quest moves nobody
--   tracked      the quest the player chose to follow (the ground arrows lead to its step); at most one per character
--   progress     how far along the current step is, for a step that counts (creatures defeated); 0 otherwise
--   finished_at  null while open; set once and never cleared — a finished quest is never given again
--
-- NOTHING IS LEFT BEHIND: deleted with its character (`on delete cascade`). A soft-deleted character keeps its rows,
-- exactly as it keeps its progress and its hotbar row, until the character row itself is purged.
create table if not exists character_quest (
    character_id uuid        not null references character (id) on delete cascade,
    quest_id     text        not null,
    step_id      text        not null default '',
    tracked      boolean     not null default false,
    progress     integer     not null default 0,
    finished_at  timestamptz,
    updated_at   timestamptz not null default now(),
    primary key (character_id, quest_id)
);
