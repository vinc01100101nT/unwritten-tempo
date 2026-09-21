import Fastify from "fastify";
import rateLimit from "@fastify/rate-limit";
import { createServer } from "node:net";
import { pathToFileURL } from "node:url";
import { config } from "./config.ts";
import { accountRoutes } from "./routes/accounts.ts";
import { closeDb, pool } from "./db.ts";

export async function buildServer() {
  // bodyLimit: nothing this service takes is bigger than a sign-in body. Characters — the list, a save, the
  // models one may be created with — live on the character server and nowhere else; the routes that once
  // answered for them here were the one door through which a player could write their own save, and are gone.
  // trustProxy: the limits below are counted per client address. Bound to 127.0.0.1 this service sits behind
  // the encrypting proxy (deploy/Caddyfile), where every request ARRIVES from the proxy and the real address
  // is in the forwarded header — without trusting it, all players would share one bucket. Bound anywhere else
  // there is no proxy, and trusting that header would let a caller name any address they liked.
  const app = Fastify({
    logger: true,
    bodyLimit: 16 * 1024,
    trustProxy: config.bindHost === "127.0.0.1",
  });

  // Checking or setting a password costs 32 MB of memory and one of Node's four worker threads for as long as
  // the hash takes, so a burst of sign-ups from one address can pin the whole process. Counted per address;
  // the sign-in and sign-up routes tighten this further on the route itself.
  await app.register(rateLimit, { global: true, max: 120, timeWindow: "1 minute" });

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
