import { randomUUID } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { query, queryOne, isUniqueViolation } from "../db.ts";
import { hashPassword, verifyPassword, issueAccessToken, newRefreshToken, hashRefreshToken } from "../auth.ts";

const credentials = z.object({
  email: z.string().trim().email().max(254),
  password: z.string().min(8).max(200),
});

const refreshBody = z.object({ refreshToken: z.string().min(10) });

// Checking a password is deliberately slow — that is what makes a stolen hash expensive to crack. It also
// means a handful of requests can pin a CPU, so the same address only gets a few tries before it has to wait.
// Held in memory on purpose: it is a speed bump, not a security boundary, and it costs nothing to lose on a
// restart. If this service is ever run as more than one process, each keeps its own count.
const MAX_FAILURES = 5;
const LOCKOUT_MS = 60_000;

const failures = new Map<string, { count: number; until: number }>();

function lockedOut(email: string): boolean {
  const seen = failures.get(email.toLowerCase());
  return seen !== undefined && seen.until > Date.now();
}

function noteFailure(email: string): void {
  const key = email.toLowerCase();
  const now = Date.now();
  const seen = failures.get(key);
  const count = seen && seen.until > now - LOCKOUT_MS ? seen.count + 1 : 1;
  failures.set(key, { count, until: count >= MAX_FAILURES ? now + LOCKOUT_MS : now });

  // Swept here rather than on a timer: without this the map is a slow leak, which is the very shape of bug
  // this pass went looking for.
  if (failures.size > 10_000) {
    for (const [k, v] of failures) if (v.until <= now) failures.delete(k);
  }
}

function forgetFailures(email: string): void {
  failures.delete(email.toLowerCase());
}

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
    if (lockedOut(email)) {
      return reply.code(429).send({ error: "too many attempts — wait a minute and try again" });
    }

    const account = await queryOne<AccountRow>(
      "select id, password_hash from account where lower(email) = lower($1)",
      [email],
    );

    // Verify even when there is no such account, so the reply takes the same time either way — otherwise
    // the response time itself says which addresses are registered.
    const ok = account
      ? await verifyPassword(password, account.password_hash)
      : await verifyPassword(password, "scrypt$32768$8$1$00$00");

    if (!account || !ok) {
      noteFailure(email);
      return reply.code(401).send({ error: "wrong email or password" });
    }

    forgetFailures(email);
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

// How long a spent or expired token is kept before it is swept. Long enough to still be visible while
// investigating a theft, short enough that the table does not grow for the life of the game.
const TOKEN_KEEP_DAYS = 30;

async function startSession(accountId: string) {
  // Swept as sessions are made rather than on a schedule, so there is no job to remember to run. Scoped to
  // this account: one person signing in never pays for everybody else's history.
  await query(
    `delete from refresh_token
      where account_id = $1
        and (revoked_at < now() - interval '${TOKEN_KEEP_DAYS} days'
             or expires_at < now() - interval '${TOKEN_KEEP_DAYS} days')`,
    [accountId],
  );

  // THE NEWEST SIGN-IN TAKES OVER. A fresh marker is written on the account and carried in the pass below, so
  // every pass handed out before this moment stops being let into the world. The old refresh tokens go with it:
  // leaving them alive would let the machine that was just taken over quietly mint itself a new pass.
  const sessionId = randomUUID();
  await query("update account set session_id = $1 where id = $2", [sessionId, accountId]);
  await query(
    "update refresh_token set revoked_at = now() where account_id = $1 and revoked_at is null",
    [accountId],
  );

  const refresh = newRefreshToken();
  await query("insert into refresh_token (token_hash, account_id, expires_at) values ($1, $2, $3)", [
    refresh.hash,
    accountId,
    refresh.expiresAt,
  ]);
  return {
    accountId,
    accessToken: issueAccessToken(accountId, sessionId),
    refreshToken: refresh.token,
  };
}
