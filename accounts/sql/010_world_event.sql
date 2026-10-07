-- WORLD EVENTS — a timed change a GM makes to the WHOLE world at once (every map server), such as an experience
-- rate. One row per KIND: starting one of a kind that is already running replaces it, so the table never holds
-- more rows than there are kinds.
--
--   kind        what the event changes — 'experience_rate'
--   multiplier  what that thing is multiplied by while it runs
--   started_at  when it was started; with the kind, the event's identity — a map server tells a new event from one it
--               already holds by this, so a replacement always sets it afresh
--   ends_at     when it stops
--   started_by  the GM character that started it, for the log
--
-- NOTHING IS LEFT BEHIND: every read of the list deletes the rows whose time is up first (charserver WorldEvents).
create table if not exists world_event (
    kind        text             primary key,
    multiplier  double precision not null check (multiplier > 0 and multiplier < 'infinity'),
    started_at  timestamptz      not null default now(),
    ends_at     timestamptz      not null,
    started_by  text             not null
);

-- WHICH VERSION OF THE LIST IS NEWEST. Raised by one in the same transaction as every start or stop, and sent with
-- every copy of the list, so a map server that receives two copies in the wrong order — a push and a poll answer
-- that crossed — keeps the newer one and ignores the other. Exactly one row.
create table if not exists world_event_revision (
    only_row boolean primary key default true check (only_row),
    revision bigint  not null
);
insert into world_event_revision (only_row, revision) values (true, 1) on conflict do nothing;
