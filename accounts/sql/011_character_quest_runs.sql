-- A TUTORIAL RUN, saved on the quest row it belongs to (decided 2026-10-07). Written in the same transaction as the
-- progress document, through the same door, exactly as 009_character_quest.sql's columns are.
--
--   replaying     a finished tutorial being played again; its reward is never paid again, and finished_at is never
--                 cleared, so it stays finished throughout
--   resume_quest  the quest that was followed when this one took over (a tutorial plays the moment it unlocks), followed
--                 again when this one is done; '' when none
--   pieces        the run's pieces: [{ i: item id, c: the copy this run made or folds, g: free forges left,
--                 n: free folds left, b: free purchases left }] — a character's FIRST run of a tutorial takes nothing its
--                 steps use up; a replay frees nothing; empty once the quest is done
--
-- EVERY ROW THAT ALREADY EXISTS gets an empty piece list — nothing free. Every character with a quest row today was
-- handed the old starting kit (Copper, gold, potions) that paid for these tutorials, so freeing them as well would pay
-- them twice.
alter table character_quest
    add column if not exists replaying    boolean not null default false,
    add column if not exists resume_quest text    not null default '',
    add column if not exists pieces       jsonb   not null default '[]'::jsonb;
