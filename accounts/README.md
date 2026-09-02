# accounts

**Who you are.** A small Node + Postgres service that registers people, signs them in, and issues the token
every other server verifies. That is now its whole job.

**Characters moved out on 2026-09-02.** The list, creating and deleting one, the appearance and every save
belong to `charserver/` — a C# server that owns that half and is the only thing that reads or writes those
rows. This service still serves those routes for now so an older build keeps working, but nothing should be
pointed at them: set `CHAR_SERVER_URL` and they go where they belong.

The three servers share **one** database, with ownership split by table — this one owns accounts and refresh
tokens — and **one** signing secret. That is the whole arrangement: this issues a token, the others verify it,
and no two of them can write the same row.

## What it holds

- **account** — an email, a scrambled password, nothing else.
- **character** — a name (unique across the game), the appearance chosen at creation (model + recolour), and
  the character's whole save as one JSON document. Anything a character-select screen shows, like the level,
  is read out of that document rather than copied beside it, so the list can never disagree with the
  character it describes.
- **refresh_token** — hashes of live sessions, so one can be revoked.

## Running it locally

You need Postgres running and a database to point at.

```bash
createdb unwritten
```

```bash
cd accounts && npm install
```

Copy `.env.example` to `.env` and fill in `DATABASE_URL` and `JWT_SECRET`, then:

```bash
cd accounts && npm run migrate
```

```bash
cd accounts && npm start
```

## Proving it works

```bash
cd accounts && npm test
```

Registers a throwaway account, logs in, refuses a wrong password, creates two characters, saves a real
version-7 save to one, reads it back byte-for-byte, proves the other is untouched and that another account
cannot see either, then cleans up after itself.

## The endpoints

| | |
| --- | --- |
| `POST /accounts` | register — `{ email, password }` |
| `POST /sessions` | log in — returns an access token and a refresh token |
| `POST /sessions/refresh` | trade a refresh token for a fresh pair (the old one is spent) |
| `DELETE /sessions` | log out |
| `GET /character-models` | the models a character may be created with |
| `GET /characters` | this account's characters |
| `POST /characters` | create — `{ name, modelName, dyeSources?, dyeTargets? }` |
| `PATCH /characters/:id/appearance` | change the body and/or the recolour |
| `DELETE /characters/:id` | delete (recoverable; frees the name) |
| `GET /characters/:id/progress` | the character's save |
| `PUT /characters/:id/progress` | write the character's save |

Everything under `/characters` needs `Authorization: Bearer <accessToken>`.

## The model roster

`data/character-models.json` is generated from the Unity project's Addressable group — the project owns which
models exist, this service only reads the list so it can refuse an appearance the game could never draw.
After registering new models:

```bash
cd accounts && npm run sync-roster
```

## Who owns what

One rule, and it decides every question below: **appearance is identity, progress is progress.**

- The **character record** owns the name, the body, and the recolour. They are chosen at creation and are
  true before the character has ever been played. Changed through `PATCH /characters/:id/appearance`.
- The **progress document** owns everything that only exists after playing: level, XP, stats, gear, bag,
  gold, hotbar. Written whole through `PUT /characters/:id/progress`.

Neither repeats the other. The game no longer writes appearance into its save, and nothing here reads
appearance out of one.

## Not done yet

- **Nothing signs the game in.** The game's storage layer now talks to these endpoints, but it has no
  sign-in or character-select screen, so it never has a token or a chosen character — and therefore never
  loads or saves. That screen is the remaining piece.
- No limit on how many characters an account may own.
- Nothing signs the game in yet — see below.
