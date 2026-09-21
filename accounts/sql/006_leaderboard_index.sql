-- The leaderboard (charserver WorldStats) reads the top twenty characters every five seconds, ordered by
-- lifetime experience, then level, then name — three values pulled out of the progress document. With no index
-- on that ordering the database had to read EVERY character row, sort them all, and keep twenty: a cost that
-- grows with the character count, paid twelve times a minute whether or not anybody is looking.
--
-- This index holds the rows in exactly that order, so the read becomes "take the first twenty entries". The
-- expressions below are copied VERBATIM from the query, down to `desc nulls last`: Postgres only uses an
-- expression index when the ORDER BY matches it character for character, and the query's `where` must be
-- covered by the `where` here. Change one, change both.
create index if not exists character_leaderboard on character (
  (case when jsonb_typeof(progress->'lifetimeXp') = 'number'
        then (progress->>'lifetimeXp')::bigint else null end) desc nulls last,
  (case when jsonb_typeof(progress->'level') = 'number'
        then (progress->>'level')::bigint else 0 end) desc,
  name asc
) where deleted_at is null and progress ? 'level';
