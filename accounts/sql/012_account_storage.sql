-- STORAGE: what an ACCOUNT keeps with the Keeper, shared by every character of that account. One row per stored thing:
-- a stack is one row per item (copy_id ''), a piece of gear is one row of its own, carrying the four values that make
-- that very piece itself (refine = its folds, cards, roll, copy_id — exactly what the bag saves for it).
--
-- EVERY MOVE IN OR OUT RIDES THE CHARACTER'S OWN SAVE (PUT …/progress carries a `storage` object beside the progress),
-- applied in the same transaction as the progress document — so an item is either in the bag that was written or in
-- this table, never in both and never in neither. `character.storage_move` is the id of the last move applied for that
-- character, so the same save sent twice applies its move once.
--
-- `account_storage_head` holds one row per account that has ever stored anything: its row is LOCKED by every move, which
-- puts two moves for one account in a queue rather than racing, and its revision rises with each.
--
-- NOTHING IS LEFT BEHIND: a row whose count reaches nothing is deleted by the move that emptied it, and every row goes
-- with its account (`on delete cascade`).
create table if not exists account_storage (
    seq        bigint      generated always as identity primary key,
    account_id uuid        not null references account (id) on delete cascade,
    item_id    text        not null,
    count      integer     not null check (count > 0),
    refine     integer     not null default 0,
    cards      text        not null default '',
    roll       text        not null default '',
    copy_id    text        not null default '',
    created_at timestamptz not null default now()
);

-- The storage list is read in the order things were put in.
create index if not exists account_storage_by_account on account_storage (account_id, seq);

-- One row per stacked item per account, so a second deposit of the same thing joins the first.
create unique index if not exists account_storage_one_stack on account_storage (account_id, item_id) where copy_id = '';

-- A piece of gear exists once in the whole game: it can never be stored twice, by anybody.
create unique index if not exists account_storage_one_copy on account_storage (copy_id) where copy_id <> '';

create table if not exists account_storage_head (
    account_id uuid   primary key references account (id) on delete cascade,
    revision   bigint not null default 0
);

-- Every character that exists today has applied no move yet, which is exactly what null says.
alter table character add column if not exists storage_move uuid;
