import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { query, queryOne, isUniqueViolation } from "../db.ts";
import { hashPassword, verifyPassword, issueAccessToken, newRefreshToken, hashRefreshToken } from "../auth.ts";

const credentials = z.object({
  email: z.string().trim().email().max(254),
  password: z.string().min(8).max(200),
});

const refreshBody = z.object({ refreshToken: z.string().min(10) });

interface AccountRow {
  id: string;
  password_hash: string;
}

export async function accountRoutes(app: FastifyInstance): Promise<void> {
  // Register.
  app.post("/accounts", async (request, reply) => {
    const parsed = credentials.safeParse(request.body);
    if (!parsed.success) return reply.code(400).send({ error: "invalid email or password" });

    const { email, password } = parsed.data;
    try {
      const row = await queryOne<{ id: string }>(
        "insert into account (email, password_hash) values ($1, $2) returning id",
        [email, await hashPassword(password)],
      );
      return reply.code(201).send({ accountId: row!.id });
    } catch (err) {
      // Deliberately the same wording as a successful-looking failure would be: whether an address is
      // already registered is not something a stranger gets to ask this endpoint.
      if (isUniqueViolation(err)) return reply.code(409).send({ error: "could not create that account" });
      throw err;
    }
  });

  // Log in.
  app.post("/sessions", async (request, reply) => {
    const parsed = credentials.safeParse(request.body);
    if (!parsed.success) return reply.code(400).send({ error: "invalid email or password" });

    const { email, password } = parsed.data;
    const account = await queryOne<AccountRow>(
      "select id, password_hash from account where lower(email) = lower($1)",
      [email],
    );

    // Verify even when there is no such account, so the reply takes the same time either way — otherwise
    // the response time itself says which addresses are registered.
    const ok = account
      ? await verifyPassword(password, account.password_hash)
      : await verifyPassword(password, "scrypt$32768$8$1$00$00");

    if (!account || !ok) return reply.code(401).send({ error: "wrong email or password" });

    return reply.send(await startSession(account.id));
  });

  // Trade a refresh token for a fresh pair. The old one is spent in the same breath, so a stolen token is
  // usable at most once and the theft shows up as the real player being logged out.
  app.post("/sessions/refresh", async (request, reply) => {
    const parsed = refreshBody.safeParse(request.body);
    if (!parsed.success) return reply.code(400).send({ error: "invalid refresh token" });

    const rows = await query<{ account_id: string }>(
      `update refresh_token set revoked_at = now()
        where token_hash = $1 and revoked_at is null and expires_at > now()
        returning account_id`,
      [hashRefreshToken(parsed.data.refreshToken)],
    );
    const spent = rows[0];
    if (!spent) return reply.code(401).send({ error: "refresh token is expired or already used" });

    return reply.send(await startSession(spent.account_id));
  });

  // Log out.
  app.delete("/sessions", async (request, reply) => {
    const parsed = refreshBody.safeParse(request.body);
    if (!parsed.success) return reply.code(400).send({ error: "invalid refresh token" });

    await query("update refresh_token set revoked_at = now() where token_hash = $1 and revoked_at is null", [
      hashRefreshToken(parsed.data.refreshToken),
    ]);
    return reply.code(204).send();
  });
}

async function startSession(accountId: string) {
  const refresh = newRefreshToken();
  await query("insert into refresh_token (token_hash, account_id, expires_at) values ($1, $2, $3)", [
    refresh.hash,
    accountId,
    refresh.expiresAt,
  ]);
  return {
    accountId,
    accessToken: issueAccessToken(accountId),
    refreshToken: refresh.token,
  };
}
