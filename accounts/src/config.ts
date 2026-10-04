// Every environment-dependent value the service needs, read once and validated at startup. A missing
// secret is a startup failure on purpose: a server that boots with a default signing key is worse than
// one that refuses to boot, because nothing looks wrong until someone forges a token.

function required(name: string): string {
  const v = process.env[name];
  if (!v) throw new Error(`Missing required setting ${name}. Copy settings.env.example to settings.env at the top of the project and fill it in.`);
  return v;
}

function optionalInt(name: string, fallback: number): number {
  const v = process.env[name];
  if (!v) return fallback;
  const n = Number(v);
  if (!Number.isInteger(n)) throw new Error(`${name} must be a whole number, got "${v}".`);
  return n;
}

function requiredInt(name: string): number {
  const v = required(name);
  const n = Number(v);
  if (!Number.isInteger(n)) throw new Error(`${name} must be a whole number, got "${v}".`);
  return n;
}

// Addresses and ports have no default here: settings.env is the one place they are written, so a value typed in
// this file could only ever disagree with it.
export const config = {
  port: requiredInt("PORT"),

  // WHICH ADDRESSES THIS ANSWERS ON. 0.0.0.0 answers on all of them, which is what a machine on your own
  // desk needs. On a hosted machine set this to 127.0.0.1: the encrypting proxy sits on the same machine and
  // reaches it there, and nothing on the internet can then reach this service unencrypted — a password sent
  // to the open port would travel readable, and no amount of certificate on the proxy prevents that while a
  // second, plain door stands open beside it.
  bindHost: required("BIND_HOST"),

  databaseUrl: required("DATABASE_URL"),
  jwtSecret: required("JWT_SECRET"),

  // Short enough that a leaked access token expires on its own; the refresh token is the revocable half.
  accessTokenSeconds: optionalInt("ACCESS_TOKEN_SECONDS", 15 * 60),
  refreshTokenDays: optionalInt("REFRESH_TOKEN_DAYS", 30),

  // A save document larger than this is a bug or an attack, not a character.
};

export type Config = typeof config;
