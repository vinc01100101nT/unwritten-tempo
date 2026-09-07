-- Every character saved before the game started recording a lifetime experience figure carries none, and the
-- leaderboard cannot wait for each of them to be played before it can rank them. This writes, once, exactly
-- what the game itself works out on loading such a save: every rung of the level curve below their level, plus
-- the experience standing inside the level they are on.
--
-- The curve is `5 + 5L` to leave level L (UnitStatMath.XpToNextFrom), so the climb to level L costs
--   sum over k = 1..L-1 of (5 + 5k)  =  5(L-1) + 5·L·(L-1)/2
-- which is the closed form below. This is a SNAPSHOT of the curve as it stands today, which is correct for a
-- one-time back-fill: from here on the game records the real figure as it is earned, and nothing recomputes it.
--
-- `version` is deliberately left alone. It stamps the shape the GAME wrote, and the game did not write this.
-- A save still stamped 9 makes the game re-derive the same number on load, so the two never disagree.
--
-- Worked out in arbitrary precision and held at the largest whole number the game can read, so a junk level
-- left behind by an old test cannot wrap round into a negative figure. Only rows that have never been given a
-- figure are touched, so re-running changes nothing.
update character
   set progress = progress || jsonb_build_object(
           'lifetimeXp',
           least(
             9223372036854775807::numeric,
               5 * ((progress->>'level')::numeric - 1)
             + 5 * (progress->>'level')::numeric * ((progress->>'level')::numeric - 1) / 2
             + case when jsonb_typeof(progress->'xp') = 'number'
                    then (progress->>'xp')::numeric else 0 end)::bigint)
 where progress ? 'level'
   and jsonb_typeof(progress->'level') = 'number'
   and (progress->>'level')::numeric >= 1
   and not (progress ? 'lifetimeXp');
