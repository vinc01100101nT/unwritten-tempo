import type { FastifyInstance, FastifyRequest, FastifyReply } from "fastify";
import { z } from "zod";
import { query, queryOne, isUniqueViolation } from "../db.ts";
import { readAccessToken } from "../auth.ts";
import { characterModels, resolveModel } from "../roster.ts";
import { checkProgress, levelOf } from "../progress.ts";

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
  model_name: string;
  dye_sources: string[];
  dye_targets: string[];
  progress: unknown;
  created_at: Date;
  updated_at: Date;
}

const CHARACTER_COLUMNS = "id, name, model_name, dye_sources, dye_targets, progress, created_at, updated_at";

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
      const row = await queryOne<CharacterRow>(
        `insert into character (account_id, name, model_name, dye_sources, dye_targets)
              values ($1, $2, $3, $4, $5)
           returning ${CHARACTER_COLUMNS}`,
        [request.accountId, parsed.data.name, model, parsed.data.dyeSources, parsed.data.dyeTargets],
      );
      return reply.code(201).send(summarise(row!));
    } catch (err) {
      if (isUniqueViolation(err)) return reply.code(409).send({ error: "that name is taken" });
      throw err;
    }
  });

  // Appearance is its own endpoint because it is stored on the character record, not inside the save —
  // it is identity, chosen at creation, and it changes at completely different moments than progress does.
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

    // coalesce leaves any field the caller omitted exactly as it was, so a recolour cannot blank the body.
    const rows = await query<CharacterRow>(
      `update character
          set model_name  = coalesce($3, model_name),
              dye_sources = coalesce($4, dye_sources),
              dye_targets = coalesce($5, dye_targets),
              updated_at  = now()
        where id = $1 and account_id = $2 and deleted_at is null
        returning ${CHARACTER_COLUMNS}`,
      [params.data.id, request.accountId, model, body.data.dyeSources ?? null, body.data.dyeTargets ?? null],
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

    const row = await queryOne<CharacterRow>(
      `select ${CHARACTER_COLUMNS}
         from character where id = $1 and account_id = $2 and deleted_at is null`,
      [parsed.data.id, request.accountId],
    );
    if (!row) return reply.code(404).send({ error: "no such character" });
    return reply.send({ ...summarise(row), progress: row.progress });
  });

  app.put("/characters/:id/progress", async (request, reply) => {
    const params = idParam.safeParse(request.params);
    if (!params.success) return reply.code(400).send({ error: "invalid character id" });

    const body = progressBody.safeParse(request.body);
    if (!body.success) return reply.code(400).send({ error: "send { progress: … }" });

    const verdict = checkProgress(body.data.progress);
    if (!verdict.ok) return reply.code(400).send({ error: verdict.reason });

    const rows = await query<CharacterRow>(
      `update character set progress = $3, updated_at = now()
        where id = $1 and account_id = $2 and deleted_at is null
        returning ${CHARACTER_COLUMNS}`,
      [params.data.id, request.accountId, body.data.progress],
    );
    if (!rows[0]) return reply.code(404).send({ error: "no such character" });
    return reply.send(summarise(rows[0]));
  });
}

// What a character-select screen needs. The level is READ from the save document rather than stored beside
// it — one owner for the number, so the list can never disagree with the character it describes.
function summarise(row: CharacterRow) {
  return {
    id: row.id,
    name: row.name,
    modelName: row.model_name,
    dyeSources: row.dye_sources,
    dyeTargets: row.dye_targets,
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
