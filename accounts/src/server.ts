import Fastify from "fastify";
import { pathToFileURL } from "node:url";
import { config } from "./config.ts";
import { accountRoutes } from "./routes/accounts.ts";
import { characterRoutes } from "./routes/characters.ts";
import { closeDb, pool } from "./db.ts";

export async function buildServer() {
  const app = Fastify({ logger: true });

  app.get("/health", async () => {
    await pool.query("select 1");
    return { ok: true };
  });

  await app.register(accountRoutes);
  await app.register(characterRoutes);

  return app;
}

// Only starts listening when this file is the program, so the tests can build a server without one.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const app = await buildServer();
  await app.listen({ port: config.port, host: "0.0.0.0" });

  for (const signal of ["SIGINT", "SIGTERM"] as const) {
    process.on(signal, async () => {
      await app.close();
      await closeDb();
      process.exit(0);
    });
  }
}
