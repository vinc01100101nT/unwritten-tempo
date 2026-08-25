// Every environment-dependent value the service needs, read once and validated at startup. A missing
// secret is a startup failure on purpose: a server that boots with a default signing key is worse than
// one that refuses to boot, because nothing looks wrong until someone forges a token.

function required(name: string): string {
  const v = process.env[name];
  if (!v) throw new Error(`Missing required environment variable ${name}. Copy .env.example to .env.`);
  return v;
}

function optionalInt(name: string, fallback: number): number {
  const v = process.env[name];
  if (!v) return fallback;
  const n = Number(v);
  if (!Number.isInteger(n)) throw new Error(`${name} must be a whole number, got "${v}".`);
  return n;
}

export const config = {
  port: optionalInt("PORT", 8080),
  databaseUrl: required("DATABASE_URL"),
  jwtSecret: required("JWT_SECRET"),

  // Short enough that a leaked access token expires on its own; the refresh token is the revocable half.
  accessTokenSeconds: optionalInt("ACCESS_TOKEN_SECONDS", 15 * 60),
  refreshTokenDays: optionalInt("REFRESH_TOKEN_DAYS", 30),

  // A save document larger than this is a bug or an attack, not a character.
  maxProgressBytes: optionalInt("MAX_PROGRESS_BYTES", 256 * 1024),
};

export type Config = typeof config;
