import Fastify from "fastify";
import { createServer } from "node:net";
import { pathToFileURL } from "node:url";
import { config } from "./config.ts";
import { accountRoutes } from "./routes/accounts.ts";
import { characterRoutes } from "./routes/characters.ts";
import { closeDb, pool } from "./db.ts";

export async function buildServer() {
  // Refused before it is read, rather than measured again after parsing. The allowance above the save's own
  // ceiling covers the few characters of wrapper the save travels in.
  const app = Fastify({ logger: true, bodyLimit: config.maxProgressBytes + 4096 });

  app.get("/health", async () => {
    await pool.query("select 1");
    return { ok: true };
  });

  // THE world clock. The game used to read the day/night cycle off the player's own machine, which meant
  // moving your computer's clock moved your world while everybody else stayed where they were. The game now
  // asks this once when it signs in and counts forward from the answer with its own frame timer, so one clock
  // — this one — decides what time it is for everyone.
  app.get("/time", async () => ({ epochSeconds: Date.now() / 1000 }));

  await app.register(accountRoutes);
  await app.register(characterRoutes);

  return app;
}

/// Paint a line red so it can be FOUND. This service writes its log straight to a terminal, where the only
/// thing that stands out is colour — an error sits in the stream looking exactly like the hundred ordinary
/// lines around it. Same escape codes the project's own shell scripts already use, so there is one idea of
/// what "red" means here. Reserved for a line the reader MUST not miss.
function shout(line: string): string {
  return `\u001b[31m${line}\u001b[0m`;
}

/// Can this service actually take the port? Answered by BINDING it exactly the way it is about to — same
/// host, same protocol — and letting it go again. A cheaper test that merely looked for a listener would
/// answer a different question and could still be wrong.
function portIsFree(port: number, host: string): Promise<boolean> {
  return new Promise((resolve) => {
    const probe = createServer();
    probe.once("error", () => resolve(false));   // taken — the one answer this is here to give
    probe.listen({ port, host }, () => probe.close(() => resolve(true)));
  });
}

// Only starts listening when this file is the program, so the tests can build a server without one.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const host = config.bindHost;

  // IS ANYBODY ALREADY THERE. Asked BEFORE anything else starts, because the alternative is an unreadable
  // stack trace after the database is already connected — and because a copy that never took the port looks
  // alive in the process list while every caller keeps talking to the older copy that really holds it.
  if (!(await portIsFree(config.port, host))) {
    console.error(shout(`[accounts] port ${config.port} is already taken — another account service is `
                        + `running. This one is quitting rather than sitting here listening to nothing. Stop `
                        + `the other one first, or give this one a different PORT.`));
    await closeDb();
    process.exit(1);
  }

  const app = await buildServer();
  await app.listen({ port: config.port, host });

  for (const signal of ["SIGINT", "SIGTERM"] as const) {
    process.on(signal, async () => {
      await app.close();
      await closeDb();
      process.exit(0);
    });
  }
}
