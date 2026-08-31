-- Appearance moves INTO the save document.
--
-- It used to sit in its own columns beside the save, because it is chosen at creation and was thought of as
-- identity rather than progress. It is not: a player changes their body and their colours in-game, exactly as
-- they change everything else, so it belongs where the rest of what they change lives. Two homes for one fact
-- is what this move removes — the columns and the document could each be written alone, and then disagree.
--
-- Carried over first, then dropped, so no character loses the look it already has.

update character
   set progress = progress || jsonb_build_object(
         'modelName',  model_name,
         'dyeSources', to_jsonb(dye_sources),
         'dyeTargets', to_jsonb(dye_targets));

alter table character drop constraint if exists character_dye_pairs;

alter table character drop column if exists model_name;
alter table character drop column if exists dye_sources;
alter table character drop column if exists dye_targets;
