import { randomBytes, scrypt, timingSafeEqual, createHash } from "node:crypto";
import { promisify } from "node:util";
import jwt from "jsonwebtoken";
import { config } from "./config.ts";

const scryptAsync = promisify(scrypt) as (
  password: string,
  salt: Buffer,
  keylen: number,
  options: { N: number; r: number; p: number; maxmem: number },
) => Promise<Buffer>;

// Deliberately slow, so guessing a stolen hash costs real time. Node ships scrypt, so there is no native
// dependency to build — one less thing to break on a fresh machine.
const SCRYPT = { N: 2 ** 15, r: 8, p: 1 } as const;
const KEY_BYTES = 32;

// scrypt needs roughly 128 x N x r bytes, which at these settings is exactly Node's default 32 MB ceiling —
// so it refuses to run unless the ceiling is raised. Doubling it leaves room to raise the cost later without
// tripping over the limit again. MEASURED: without this, every hash throws "memory limit exceeded".
const maxmemFor = (N: number, r: number) => 128 * N * r * 2;

/** "scrypt$N$r$p$saltHex$keyHex" — the parameters travel WITH the hash, so raising the cost later still
 *  leaves every existing password verifiable against the cost it was created at. */
export async function hashPassword(password: string): Promise<string> {
  const salt = randomBytes(16);
  const key = await scryptAsync(password, salt, KEY_BYTES, { ...SCRYPT, maxmem: maxmemFor(SCRYPT.N, SCRYPT.r) });
  return `scrypt$${SCRYPT.N}$${SCRYPT.r}$${SCRYPT.p}$${salt.toString("hex")}$${key.toString("hex")}`;
}

export async function verifyPassword(password: string, stored: string): Promise<boolean> {
  const parts = stored.split("$");
  if (parts.length !== 6 || parts[0] !== "scrypt") return false;

  const [, nRaw, rRaw, pRaw, saltHex, keyHex] = parts as [string, string, string, string, string, string];
  const N = Number(nRaw), r = Number(rRaw), p = Number(pRaw);
  if (!Number.isInteger(N) || !Number.isInteger(r) || !Number.isInteger(p)) return false;

  const salt = Buffer.from(saltHex, "hex");
  const expected = Buffer.from(keyHex, "hex");
  const actual = await scryptAsync(password, salt, expected.length, { N, r, p, maxmem: maxmemFor(N, r) });
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

export interface AccessTokenClaims {
  accountId: string;
  /** WHICH SIGN-IN THIS PASS BELONGS TO. A new sign-in takes over: it writes a new one of these on the account,
   *  and every pass carrying an older one stops being let into the world. Without it a pass proves who you are
   *  and nothing about being the only one, so the same account could play in two places at once. */
  sessionId?: string;
}

/** The proof of identity the game carries. Short-lived and stateless — the game server will verify it with
 *  the same secret, which is the whole point of issuing it here rather than handing out a session cookie. */
export function issueAccessToken(accountId: string, sessionId?: string): string {
  return jwt.sign({ accountId, sessionId } satisfies AccessTokenClaims, config.jwtSecret, {
    expiresIn: config.accessTokenSeconds,
    subject: accountId,
  });
}

export function readAccessToken(token: string): AccessTokenClaims | null {
  try {
    const claims = jwt.verify(token, config.jwtSecret);
    if (typeof claims === "object" && claims !== null && typeof (claims as AccessTokenClaims).accountId === "string") {
      const c = claims as AccessTokenClaims;
      return { accountId: c.accountId, sessionId: typeof c.sessionId === "string" ? c.sessionId : undefined };
    }
    return null;
  } catch {
    return null;
  }
}

/** A refresh token is a plain random secret — it means nothing on its own, which is what lets the database
 *  store only its hash and still recognise it. */
export function newRefreshToken(): { token: string; hash: string; expiresAt: Date } {
  const token = randomBytes(32).toString("base64url");
  const expiresAt = new Date(Date.now() + config.refreshTokenDays * 24 * 60 * 60 * 1000);
  return { token, hash: hashRefreshToken(token), expiresAt };
}

export function hashRefreshToken(token: string): string {
  return createHash("sha256").update(token).digest("hex");
}
