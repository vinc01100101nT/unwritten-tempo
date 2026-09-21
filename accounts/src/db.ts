import { Pool } from "pg";
import type { QueryResultRow } from "pg";
import { config } from "./config.ts";

// One pool for the process. Every query in the service goes through the helpers here rather than reaching
// for the pool directly, so connection handling and the parameterised-query rule have a single owner.
//
// BOUNDED ON PURPOSE. Left at the driver's defaults there is no query timeout at all, so one query that hangs
// (a lock, a slow read on a grown table) holds its connection until the TCP session dies, and enough of those
// wedge the whole service. Every number here is a ceiling a sign-in never comes near; the checker
// tools/check-server-rules.py refuses a pool without them.
export const pool = new Pool({
  connectionString: config.databaseUrl,
  max: 20,
  statement_timeout: 5000,
  query_timeout: 5000,
  idleTimeoutMillis: 30000,
  connectionTimeoutMillis: 3000,
});

export async function query<T extends QueryResultRow>(text: string, params: unknown[] = []): Promise<T[]> {
  const result = await pool.query<T>(text, params);
  return result.rows;
}

/** The first row, or null. For lookups that are unique by construction. */
export async function queryOne<T extends QueryResultRow>(text: string, params: unknown[] = []): Promise<T | null> {
  const rows = await query<T>(text, params);
  return rows[0] ?? null;
}

/** Postgres's code for "a unique index rejected this row" — the only conflict this service can hit. */
export const UNIQUE_VIOLATION = "23505";

export function isUniqueViolation(err: unknown): boolean {
  return typeof err === "object" && err !== null && (err as { code?: string }).code === UNIQUE_VIOLATION;
}

export async function closeDb(): Promise<void> {
  await pool.end();
}
