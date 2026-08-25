import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { pool, closeDb } from "./db.ts";

// Applies every .sql file in sql/ that this database has not seen yet, in filename order, each inside its
// own transaction. Re-running is a no-op — which is what makes it safe to run on every deploy rather than
// remembering whether it was already done.

const sqlDir = fileURLToPath(new URL("../sql/", import.meta.url));

export async function migrate(log: (line: string) => void = console.log): Promise<void> {
  await pool.query(`create table if not exists schema_migration (
      filename   text primary key,
      applied_at timestamptz not null default now())`);

  const applied = new Set(
    (await pool.query<{ filename: string }>("select filename from schema_migration")).rows.map((r) => r.filename),
  );

  const files = readdirSync(sqlDir).filter((f) => f.endsWith(".sql")).sort();
  let ran = 0;

  for (const file of files) {
    if (applied.has(file)) continue;

    const client = await pool.connect();
    try {
      await client.query("begin");
      await client.query(readFileSync(sqlDir + file, "utf8"));
      await client.query("insert into schema_migration (filename) values ($1)", [file]);
      await client.query("commit");
      log(`applied ${file}`);
      ran++;
    } catch (err) {
      await client.query("rollback");
      throw new Error(`migration ${file} failed: ${(err as Error).message}`);
    } finally {
      client.release();
    }
  }

  log(ran === 0 ? "database is already up to date" : `applied ${ran} migration(s)`);
}

if (process.argv[1]?.endsWith("migrate.ts")) {
  await migrate();
  await closeDb();
}
