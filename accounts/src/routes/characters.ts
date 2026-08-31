import type { FastifyInstance, FastifyRequest, FastifyReply } from "fastify";
import { z } from "zod";
import { query, queryOne, isUniqueViolation } from "../db.ts";
import { readAccessToken } from "../auth.ts";
import { characterModels, resolveModel } from "../roster.ts";
import { checkProgress, levelOf, appearanceOf } from "../progress.ts";

// Names players will read out loud and type at each other: letters, digits and underscore only, so a name
// can't hide look-alike whitespace or wear punctuation that breaks a chat command.
const NAME_PATTERN = /^[A-Za-z0-9_]{3,16}$/;

const hexColour = z.string().trim().regex(/^#[0-9a-fA-F]{6}$/, "colours must look like #RRGGBB");

const createBody = z
  .object({
    name: z.string().trim().regex(NAME_PATTERN, "3-16 letters, digits or underscore"),
    modelName: z.string().trim().min(1),
    // The recolour the player picked, as the game expresses it: which colour in the model's own art, and
    // what to replace it with. Both arrays or neither — an unpaired recolour has no meaning.
    dyeSources: z.array(hexColour).max(32).default([]),
    dyeTargets: z.array(hexColour).max(32).default([]),
  })
  .refine((c) => c.dyeSources.length === c.dyeTargets.length, {
    message: "dyeSources and dyeTargets must have the same number of colours",
  });

// Every field optional — a recolour sends colours, a body change sends a model, and whatever is left out
// keeps the value it already had. Both colour arrays still travel together when either is sent.
const appearanceBody = z
  .object({
    modelName: z.string().trim().min(1).optional(),
    dyeSources: z.array(hexColour).max(32).optional(),
    dyeTargets: z.array(hexColour).max(32).optional(),
  })
  .refine((a) => (a.dyeSources === undefined) === (a.dyeTargets === undefined), {
    message: "send both dyeSources and dyeTargets, or neither",
  })
  .refine((a) => (a.dyeSources?.length ?? 0) === (a.dyeTargets?.length ?? 0), {
    message: "dyeSources and dyeTargets must have the same number of colours",
  });

const progressBody = z.object({ progress: z.unknown() });

const idParam = z.object({ id: z.string().uuid() });

interface CharacterRow {
  id: string;
  name: string;
  progress: unknown;
  created_at: Date;
  updated_at: Date;
}

const CHARACTER_COLUMNS = "id, name, progress, created_at, updated_at";

// The same columns, named through the character table — needed only where a query joins another table and a
// bare column name would be ambiguous. Derived from the list above rather than typed out again, so the two
// can never come to disagree about which columns a character has.
const CHARACTER_COLUMNS_QUALIFIED = CHARACTER_COLUMNS.split(", ").map((c) => `c.${c}`).join(", ");

export async function characterRoutes(app: FastifyInstance): Promise<void> {
  // Everything below belongs to whoever is holding the access token.
  app.addHook("preHandler", async (request: FastifyRequest, reply: FastifyReply) => {
    const header = request.headers.authorization ?? "";
    const token = header.startsWith("Bearer ") ? header.slice(7) : "";
    const claims = token ? readAccessToken(token) : null;
    if (!claims) return reply.code(401).send({ error: "sign in first" });
    request.accountId = claims.accountId;
  });

  /** The roster the game can draw — so a character-creation screen shows exactly what the server will accept. */
  app.get("/character-models", async () => ({ models: characterModels }));

  app.get("/characters", async (request) => {
    const rows = await query<CharacterRow>(
      `select ${CHARACTER_COLUMNS}
         from character
        where account_id = $1 and deleted_at is null
        order by created_at`,
      [request.accountId],
    );
    return { characters: rows.map(summarise) };
  });

  app.post("/characters", async (request, reply) => {
    const parsed = createBody.safeParse(request.body);
    if (!parsed.success) {
      return reply.code(400).send({ error: parsed.error.issues[0]?.message ?? "invalid character" });
    }

    const model = resolveModel(parsed.data.modelName);
    if (!model) return reply.code(400).send({ error: `no character model called '${parsed.data.modelName}'` });

    try {
      // The look goes straight into the save document — the character has no other home for it. It carries
      // no version yet, which is exactly what marks this character as never played.
      const born = {
        modelName: model,
        dyeSources: parsed.data.dyeSources,
        dyeTargets: parsed.data.dyeTargets,
      };
      const row = await queryOne<CharacterRow>(
        `insert into character (account_id, name, progress)
              values ($1, $2, $3)
           returning ${CHARACTER_COLUMNS}`,
        [request.accountId, parsed.data.name, JSON.stringify(born)],
      );
      return reply.code(201).send(summarise(row!));
    } catch (err) {
      if (isUniqueViolation(err)) return reply.code(409).send({ error: "that name is taken" });
      throw err;
    }
  });

  // Appearance has its own endpoint because it changes at a completely different moment than progress does —
  // the instant a player recolours, rather than on the next autosave. It writes into the same document.
  app.patch("/characters/:id/appearance", async (request, reply) => {
    const params = idParam.safeParse(request.params);
    if (!params.success) return reply.code(400).send({ error: "invalid character id" });

    const body = appearanceBody.safeParse(request.body);
    if (!body.success) {
      return reply.code(400).send({ error: body.error.issues[0]?.message ?? "invalid appearance" });
    }

    let model: string | null = null;
    if (body.data.modelName !== undefined) {
      model = resolveModel(body.data.modelName);
      if (!model) return reply.code(400).send({ error: `no character model called '${body.data.modelName}'` });
    }

    // Merged into the save document, so whatever the caller left out keeps the value it already had — the
    // same promise the old three columns made, now made by the merge itself.
    const patch: Record<string, unknown> = {};
    if (model !== null) patch.modelName = model;
    if (body.data.dyeSources !== undefined) {
      patch.dyeSources = body.data.dyeSources;
      patch.dyeTargets = body.data.dyeTargets;
    }

    const rows = await query<CharacterRow>(
      `update character
          set progress   = progress || $3::jsonb,
              updated_at = now()
        where id = $1 and account_id = $2 and deleted_at is null
        returning ${CHARACTER_COLUMNS}`,
      [params.data.id, request.accountId, JSON.stringify(patch)],
    );
    if (!rows[0]) return reply.code(404).send({ error: "no such character" });
    return reply.send(summarise(rows[0]));
  });

  app.delete("/characters/:id", async (request, reply) => {
    const parsed = idParam.safeParse(request.params);
    if (!parsed.success) return reply.code(400).send({ error: "invalid character id" });

    // Soft delete: the row stays, so a mistaken deletion is recoverable and anything that later references
    // this character (a trade, a kill log) still has something to point at. The partial unique index treats
    // a deleted row as absent, so the name comes free.
    const rows = await query<{ id: string }>(
      `update character set deleted_at = now(), updated_at = now()
        where id = $1 and account_id = $2 and deleted_at is null
        returning id`,
      [parsed.data.id, request.accountId],
    );
    if (!rows[0]) return reply.code(404).send({ error: "no such character" });
    return reply.code(204).send();
  });

  app.get("/characters/:id/progress", async (request, reply) => {
    const parsed = idParam.safeParse(request.params);
    if (!parsed.success) return reply.code(400).send({ error: "invalid character id" });

    // The GM mark rides along here, joined off the owning account. It is answered on THIS reply and nowhere
    // else because both readers already ask for exactly this — the player's own game, to decide which commands
    // it will even attempt, and the game server, to decide which it will actually carry out. Answering it in a
    // second place as well would be two owners for one fact, and the pair drifts the moment either is written
    // alone. The server's copy is the one that decides anything that touches the shared world; the game's copy
    // only spares a player from typing a command that was going to be refused.
    const row = await queryOne<CharacterRow & { is_gm: boolean }>(
      `select ${CHARACTER_COLUMNS_QUALIFIED}, a.is_gm
         from character c
         join account a on a.id = c.account_id
        where c.id = $1 and c.account_id = $2 and c.deleted_at is null`,
      [parsed.data.id, request.accountId],
    );
    if (!row) return reply.code(404).send({ error: "no such character" });
    return reply.send({ ...summarise(row), progress: row.progress, isGm: row.is_gm });
  });

  app.put("/characters/:id/progress", async (request, reply) => {
    const params = idParam.safeParse(request.params);
    if (!params.success) return reply.code(400).send({ error: "invalid character id" });

    const body = progressBody.safeParse(request.body);
    if (!body.success) return reply.code(400).send({ error: "send { progress: … }" });

    const verdict = checkProgress(body.data.progress);
    if (!verdict.ok) return reply.code(400).send({ error: verdict.reason });

    // A save REPLACES the document, because the game owns it whole. The one exception is the look: a client
    // that does not carry it must not be able to wipe it, so an incoming save that names no body keeps the
    // one already stored. (Without this, a single save from an older build erases a character's appearance.)
    const incoming = body.data.progress as Record<string, unknown>;
    const keepsLook = typeof incoming.modelName === "string" && incoming.modelName.length > 0;

    const rows = await query<CharacterRow>(
      keepsLook
        ? `update character set progress = $3, updated_at = now()
            where id = $1 and account_id = $2 and deleted_at is null
            returning ${CHARACTER_COLUMNS}`
        : `update character
              set progress = $3::jsonb
                           || jsonb_build_object('modelName',  progress -> 'modelName',
                                                 'dyeSources', progress -> 'dyeSources',
                                                 'dyeTargets', progress -> 'dyeTargets'),
                  updated_at = now()
            where id = $1 and account_id = $2 and deleted_at is null
            returning ${CHARACTER_COLUMNS}`,
      [params.data.id, request.accountId, JSON.stringify(body.data.progress)],
    );
    if (!rows[0]) return reply.code(404).send({ error: "no such character" });
    return reply.send(summarise(rows[0]));
  });
}

// What a character-select screen needs. The level is READ from the save document rather than stored beside
// it — one owner for the number, so the list can never disagree with the character it describes.
function summarise(row: CharacterRow) {
  const look = appearanceOf(row.progress);
  return {
    id: row.id,
    name: row.name,
    modelName: look.modelName,
    dyeSources: look.dyeSources,
    dyeTargets: look.dyeTargets,
    level: levelOf(row.progress),
    createdAt: row.created_at,
    updatedAt: row.updated_at,
  };
}

declare module "fastify" {
  interface FastifyRequest {
    accountId: string;
  }
}
