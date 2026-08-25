-- Accounts + characters. One migration for now; add 002_*.sql etc. rather than editing this file,
-- so a database that has already run it can catch up by running only what it hasn't seen.

create extension if not exists pgcrypto;   -- gen_random_uuid()

create table if not exists account (
    id            uuid primary key default gen_random_uuid(),
    email         text        not null,
    password_hash text        not null,
    created_at    timestamptz not null default now()
);

-- Email is matched case-insensitively, so uniqueness must be too. Storing the address as typed keeps
-- the display form; the index is what decides whether two rows collide.
create unique index if not exists account_email_unique on account (lower(email));

create table if not exists character (
    id         uuid primary key default gen_random_uuid(),
    account_id uuid        not null references account (id) on delete cascade,
    name       text        not null,

    -- The appearance chosen at creation. The model is stored here because nothing else owns it — the game's
    -- save has never carried which body a character wears. The two dye arrays are the same parallel pair the
    -- game uses: sources[i] is a colour in the model's own art, targets[i] is what it is recoloured to.
    model_name  text   not null,
    dye_sources text[] not null default '{}',
    dye_targets text[] not null default '{}',

    -- The character's whole save, exactly as the game writes it. It is stored as ONE document on purpose:
    -- the game owns that shape (it is already at version 7), and mirroring its fields into columns here
    -- would create a second owner for every value that then has to be kept in sync by hand. Anything this
    -- service needs to show — the level on a character-select screen — is READ out of this document, never
    -- copied beside it. An unstarted character has {}: the game builds a fresh one exactly as it does today
    -- when no save exists, so the starting values stay in the game where they belong.
    progress   jsonb       not null default '{}'::jsonb,

    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    deleted_at timestamptz
);

-- Character names are unique across the whole game, case-insensitively — a deleted character frees its name.
-- A recolour names one source colour and one replacement, so the two arrays move together or the pairing lies.
alter table character drop constraint if exists character_dye_pairs;
alter table character add constraint character_dye_pairs
    check (array_length(dye_sources, 1) is not distinct from array_length(dye_targets, 1));

create unique index if not exists character_name_unique on character (lower(name)) where deleted_at is null;
create index if not exists character_by_account on character (account_id) where deleted_at is null;

-- Refresh tokens live here rather than only in the client so a session can be revoked. Only the HASH is
-- stored: a stolen database then yields nothing that can be presented as a token.
create table if not exists refresh_token (
    token_hash text primary key,
    account_id uuid        not null references account (id) on delete cascade,
    expires_at timestamptz not null,
    revoked_at timestamptz
);

create index if not exists refresh_token_by_account on refresh_token (account_id);
