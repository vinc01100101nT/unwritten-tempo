-- WHICH HAND-OVER of a character is the live one. The character server adds one to this every time it sends the
-- character to a map server — entering the world, or walking through a door into a zone another map server holds —
-- and the map server sends the number back with every save. A save carrying an older number came from a map server
-- the character has since left, which lost touch with the character server and reconnected late; it is refused, so
-- it can never put that player back to an older state.
--
-- Starts at 0 for every existing character, which is safe: the first hand-over after this lands makes it 1, and a
-- save carrying 0 (a map server built before this) is not checked at all.
alter table character add column if not exists handover integer not null default 0;
