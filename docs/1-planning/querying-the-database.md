---
status:
owner: vince
last-updated: 2026-09-07
tags:
  - planning-production
---

# Querying the Database

How to ask the accounts database questions by hand — find a player, look up a character by name, see who is
wearing which body, read a level out of a save. Every query below is written against the tables the account
service actually has, so what you type here matches what the game stores. The shape is `001_init.sql` PLUS every
migration beside it — a later one can move a value or drop a column, which is exactly how the body and its colours
stopped being columns. **`\d character` is the authority**, not any list written down here; if this page and the
table disagree, the table is right and this page needs fixing.

This is a **read-and-poke** guide for a developer at a terminal. The service itself never queries this way:
it goes through its two helpers in `accounts/src/db.ts`, which always pass values as parameters. Anything you
learn here that ends up in code must follow that rule — see [Never paste a value into SQL](#never-paste-a-value-into-sql).

---

## Getting in

The service reads its connection string from `DATABASE_URL`. The same string works at the command line:

```bash
psql "$DATABASE_URL"
```

Locally the database is created with `createdb unwritten`, so this also works:

```bash
psql unwritten
```

Useful once you are in:

| | |
| --- | --- |
| `\dt` | list the tables |
| `\d character` | show one table's columns, indexes and constraints |
| `\x` | toggle expanded output — essential for rows carrying a save document |
| `\q` | quit |

To run one query without opening a session:

```bash
psql "$DATABASE_URL" -c "select count(*) from account"
```

---

## What is in there

The tables you will actually query, and one rule that explains most of the queries below: **appearance is
identity, progress is progress.** (One more, `schema_migration`, is bookkeeping the migrator owns — it just
records which `.sql` files this database has already run.)

- **`account`** — `id`, `email`, `password_hash`, `created_at`, `is_gm`, `session_id` (the one live sign-in). There is no display name here.
- **`character`** — `id`, `account_id`, `name`, `progress`, `created_at`, `updated_at`, `deleted_at`,
  `handover` (which hand-over to a map server the latest save belongs to) and `storage_move` (the id of the last
  storage move applied for this character; null when it has never made one). **Everything about a character except
  its name and its hotbar lives inside the single `progress` JSON document** — level, XP, gear, gold, AND the body
  and its recolour (`modelName`, `dyeSources`, `dyeTargets`). A player changes their look in-game exactly as they change everything else, so it belongs with
  the rest of what they change; keeping it in columns as well meant two homes for one fact that could disagree.
  Reach into the document with `->>` for a value and `->` for an array or object.
- **`character_hotbar`** — `character_id`, `slots`, `updated_at`. The hotbar, one row per character, written only
  when a slot changes. `slots` is a JSON array of 27 strings, three rows of nine, top row first: `""` for an empty
  slot, a skill's id, `item:<item id>` for a usable item, or `gear:<copy id>` for one exact piece of gear. A save
  from before version 18 still carries its old bar as `hotbarBindings` inside `progress`; the server reads that once
  to fill this row and never writes it again.
- **`account_storage`** — `seq`, `account_id`, `item_id`, `count`, `refine`, `cards`, `roll`, `copy_id`, `created_at`.
  What an **account** keeps with the Keeper, shared by all its characters, one row per stored thing. A stack is one row
  per item with `copy_id` `''`; a piece of gear is a row of its own carrying the same four values the bag saves for it
  (`refine` is its folds). A row never holds a count of 0: the move that empties it deletes it.
  **`account_storage_head`** holds one row per account that has ever stored anything — `revision`, raised by every
  move. Every move is written in the same transaction as a character's `progress`, so an item is in a bag or in
  storage, never both.
- **`world_event`** — `kind`, `multiplier`, `started_at`, `ends_at`, `started_by`. The world events running now
  (a GM's `/exprate`), one row per kind; a row whose time is up is deleted the next time the list is read.
  **`world_event_revision`** holds one number, raised with every start and stop, that every copy of the list carries.
- **`refresh_token`** — `token_hash`, `account_id`, `expires_at`, `revoked_at`. Live sessions, hashed.

Two things about `character` that will bite you if you forget them:

1. **Deleting is soft.** A deleted character keeps its row and gets a `deleted_at` timestamp. Almost every
   query you write wants `and deleted_at is null`, and the two indexes on the table only cover rows where that
   is true — so leaving it out is both wrong and slower.
2. **`character` is also a SQL type name.** Postgres still accepts it as a table name unquoted, exactly as the
   service uses it, so `from character` is fine. If a tool ever chokes on it, write `from "character"`.

---

## Accounts

Find one by email. The address is stored as the player typed it, and matched case-insensitively — there is a
unique index on `lower(email)`, so this is the form that uses it:

```sql
select id, email, created_at
  from account
 where lower(email) = lower('Player@Example.com');
```

Everyone who signed up today:

```sql
select id, email, created_at
  from account
 where created_at >= current_date
 order by created_at desc;
```

The newest twenty:

```sql
select id, email, created_at
  from account
 order by created_at desc
 limit 20;
```

---

## Characters

### One character by name

Names are unique across the whole game, case-insensitively, among characters that are not deleted. Match the
same way the index does:

```sql
select id, name, progress->>'modelName', created_at
  from character
 where lower(name) = lower('Ashwalker')
   and deleted_at is null;
```

### One character's hotbar

The bar is not in `progress`; join its own row. No row means none has been written for that character yet — the
character server then hands the map server no bar, and a save from before version 18 has its old bar read from
`progress` instead.

```sql
select h.slots, h.updated_at
  from character_hotbar h
  join character c on c.id = h.character_id
 where lower(c.name) = lower('Ashwalker')
   and c.deleted_at is null;
```

### Names that start with something

A prefix search — useful for "who is called Ash-something":

```sql
select name, progress->>'modelName'
  from character
 where name ilike 'ash%'
   and deleted_at is null
 order by name;
```

`ilike` is `like` that ignores case. `%` means "any run of characters", `_` means "exactly one". So
`'%wolf%'` finds the word anywhere in the name:

```sql
select name from character
 where name ilike '%wolf%' and deleted_at is null;
```

Be aware that a leading `%` cannot use an index — on a small database nobody will notice, on a large one it
reads every row.

### Everything one account owns

This is the query behind the character-select screen:

```sql
select id, name, progress->>'modelName', progress->'dyeSources', progress->'dyeTargets',
       created_at, updated_at
  from character
 where account_id = '00000000-0000-0000-0000-000000000000'
   and deleted_at is null
 order by created_at;
```

### Characters with the player they belong to

Joining reaches the email, which the character row does not carry:

```sql
select c.name, c.progress->>'modelName', a.email
  from character c
  join account a on a.id = c.account_id
 where c.deleted_at is null
 order by a.email, c.created_at;
```

### Filtering by body and recolour

The model is a value inside the document, so it is read with `->>` (which returns text) and compared as text:

```sql
select name, created_at
  from character
 where progress->>'modelName' = 'BlueSoldier_Female'
   and deleted_at is null;
```

How popular each body is:

```sql
select progress->>'modelName' as body, count(*) as characters
  from character
 where deleted_at is null
 group by body
 order by characters desc;
```

The two dye lists are JSON arrays that move in step — `dyeSources[i]` is a colour in the model's own art and
`dyeTargets[i]` is what it was recoloured to. `@>` asks whether a JSON array contains a value; the value is
written as JSON, so a string needs its own quotes inside the literal:

```sql
select name, progress->'dyeTargets'
  from character
 where progress->'dyeTargets' @> '"#FF0000"'::jsonb
   and deleted_at is null;
```

Characters that were never recoloured at all:

```sql
select name from character
 where coalesce(jsonb_array_length(progress->'dyeSources'), 0) = 0 and deleted_at is null;
```

### Deleted characters

They are still there, and their names are free for someone else to take:

```sql
select name, deleted_at
  from character
 where deleted_at is not null
 order by deleted_at desc;
```

---

## Reading inside a save

`progress` is one JSON document, written by the game, in the game's own shape. Nothing is mirrored into
columns beside it — so anything you want out of a save, you read out of the document.

A character that has never been played carries only its look — `modelName`, `dyeSources`, `dyeTargets` — and no
`version`.

`->` returns JSON, `->>` returns text. To compare a number you must say it is a number:

```sql
select name,
       progress ->> 'version'                as save_version,
       (progress ->> 'level')::int           as level
  from character
 where deleted_at is null
 order by level desc nulls last;
```

Everyone past level 20:

```sql
select name, (progress ->> 'level')::int as level
  from character
 where deleted_at is null
   and (progress ->> 'level')::int > 20;
```

That cast fails loudly if any row's `level` is not a number, so on mixed data filter to the rows that have one
first:

```sql
select name, (progress ->> 'level')::int as level
  from character
 where deleted_at is null
   and jsonb_typeof(progress -> 'level') = 'number'
   and (progress ->> 'level')::int > 20;
```

Characters nobody has started yet:

```sql
select name, created_at
  from character
 where progress = '{}'::jsonb
   and deleted_at is null;
```

Reach a nested value with a path — `#>>` takes the route as an array and returns text:

```sql
select name, progress #>> '{stats,strength}' as strength
  from character
 where deleted_at is null;
```

Ask whether a document contains something, at any depth, with `@>`:

```sql
select name
  from character
 where progress @> '{"version": 7}'
   and deleted_at is null;
```

See what keys a save actually has, which is the fastest way to learn its shape:

```sql
select jsonb_object_keys(progress)
  from character
 where lower(name) = lower('Ashwalker') and deleted_at is null;
```

Read a whole save without it wrapping into unreadable mush:

```sql
\x
select jsonb_pretty(progress) from character where lower(name) = lower('Ashwalker');
```

What a character sold and can still buy back — at most ten entries, oldest first, each with the gold the shop paid
and `endsAt`, the moment it stops being kept, in seconds since 1970. Missing or `[]` means nothing to buy back:

```sql
select name, jsonb_pretty(progress->'buyback')
  from character
 where lower(name) = lower('Ashwalker') and deleted_at is null;
```

---

## Storage

What one account keeps with the Keeper, in the order it was put in. Storage belongs to the account, so reach it
through any of its characters:

```sql
select s.item_id, s.count, s.refine, s.copy_id, s.created_at
  from account_storage s
  join character c on c.account_id = s.account_id
 where lower(c.name) = lower('Ashwalker')
   and c.deleted_at is null
 order by s.seq;
```

How many things each account has stored, fullest first — a stack counts as one thing, and storage holds 600:

```sql
select a.email, count(*) as things, h.revision
  from account_storage s
  join account a on a.id = s.account_id
  join account_storage_head h on h.account_id = s.account_id
 group by a.email, h.revision
 order by things desc
 limit 20;
```

Where one exact piece of gear is: in somebody's storage, or nowhere there. A piece's own id is unique across the
whole game, so this returns one row at most:

```sql
select a.email, s.item_id, s.refine, s.roll
  from account_storage s
  join account a on a.id = s.account_id
 where s.copy_id = 'the-piece-id';
```

---

## Sessions

Which sessions are live for one account:

```sql
select expires_at, revoked_at
  from refresh_token
 where account_id = '00000000-0000-0000-0000-000000000000'
   and revoked_at is null
   and expires_at > now();
```

Only the hash of a refresh token is stored, so you cannot look one up by the token a client is holding, and
nothing in this table can be presented as a token. That is deliberate.

Sign one account out everywhere:

```sql
update refresh_token set revoked_at = now()
 where account_id = '00000000-0000-0000-0000-000000000000'
   and revoked_at is null;
```

---

## Habits that keep you out of trouble

### Never paste a value into SQL

In code, a value is always a numbered placeholder and a separate argument — `$1`, `$2` — never glued into the
string. Both query helpers in the service take the text and the values apart for exactly this reason:

```ts
const rows = await query<CharacterRow>(
  "select id, name from character where lower(name) = lower($1) and deleted_at is null",
  [name],
);
```

Written the other way, a name containing a quote stops being a name and starts being SQL.

### Let the database enforce what must be true

A duplicate name is not caught by looking first and inserting second — two players can pass that check at the
same moment. The unique index refuses the second insert, and the service turns that one error code into "that
name is taken". Check for the error, don't pre-check the row.

### Always say `and deleted_at is null`

Both character indexes only cover live rows. Omitting it silently includes deleted characters and gives up the
index at the same time.

### Say what you want and how many

`select *` on `character` drags every save document across the wire. Name the columns you actually need, and
put a `limit` on anything exploratory.

### Look before you change

Run any `update` or `delete` as a `select` with the identical `where` clause first and read the rows it
returns. If you are still nervous, wrap it:

```sql
begin;
update character set name = 'Ashwalker' where id = '…';
-- read it back, then:
commit;   -- or: rollback;
```

### Find out why something is slow

`explain analyze` in front of a query shows what the database really did — whether it used an index or read
the whole table:

```sql
explain analyze
select name from character where lower(name) = lower('Ashwalker') and deleted_at is null;
```

---

## Changing the shape of a table

Never edit a migration that has already run. Add the next numbered `.sql` file beside the others and run
`npm run migrate` — every `.sql`
file the database has not seen yet is applied in filename order, each inside its own transaction, and the
filename is recorded in `schema_migration`. Re-running is therefore harmless. Editing the old file instead
leaves every database that already ran it behind, silently.

---

## Related

- [Starting the Network](starting-the-network.md) — how the database gets running in the first place.
- [Networking — Implementation Notes](../2-systems/networking/implementation-notes.md) — who writes what into it, and why progress is still written by the client.
- [Unified Unit Stats](../2-systems/active-skill-system-v3/unified-unit-stats.md) — how to read the stats inside a save.
- [Ring System — Current Facts (Trio-Ring)](../2-systems/ring-system-trio-ring.md) — the ring walk that a save serializes.
- [Roadmap to Launch](roadmap-to-launch.md) — the persistence line under Tier 2.
