-- Who is allowed to run the game's commands. Every "/…" command except /help changes something — a level, a
-- bag, the creatures in the world — so the mark that permits them lives on the ACCOUNT, not on a character:
-- being trusted with them is a fact about a person, and it should not have to be granted again for every
-- character they make.
--
-- Nobody has it until it is granted by hand:
--     update account set is_gm = true where lower(email) = lower('you@example.com');
alter table account add column if not exists is_gm boolean not null default false;
