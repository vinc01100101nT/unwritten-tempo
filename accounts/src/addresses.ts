// WHERE THIS SERVICE CAN ACTUALLY BE REACHED — said only after trying it.
//
// The same rule as the other three servers (the character server, the map server, tools/serve-folder.py): no
// address is printed as reachable unless a connection to it was just made from this computer, the address
// players are handed is tried the same way and reported in red when it does not answer, and the whole report
// is made again when this computer's own addresses change — an adapter that comes up after the service started
// (Tailscale, a new Wi-Fi) is said when it happens, not left looking down for the rest of the run.

import { networkInterfaces } from "node:os";
import { connect } from "node:net";

const RECHECK_MS = 10_000;
const PROBE_MS = 1_000;

const red = (line: string) => `\u001b[31m${line}\u001b[0m`;

/// address -> adapter name, for every IPv4 address this computer holds right now. 169.254.x.x is left out: it
/// is the address an adapter gives ITSELF when it has no network to get one from, so nobody can reach it there.
function ownAddresses(): Map<string, string> {
  const found = new Map<string, string>();
  for (const [adapter, list] of Object.entries(networkInterfaces()))
    for (const one of list ?? [])
      if (one.family === "IPv4" && !one.address.startsWith("169.254.")) found.set(one.address, adapter);
  return found;
}

function answers(host: string, port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = connect({ host, port });
    const done = (ok: boolean) => { socket.destroy(); resolve(ok); };
    socket.setTimeout(PROBE_MS, () => done(false));
    socket.once("connect", () => done(true));
    socket.once("error", () => done(false));
  });
}

function dialledAt(url: string): { host: string; port: number } | null {
  try {
    const u = new URL(url.includes("://") ? url : `http://${url}`);
    return { host: u.hostname, port: u.port ? Number(u.port) : u.protocol === "https:" ? 443 : 80 };
  } catch {
    return null;
  }
}

async function report(label: string, port: number, given: { setting: string; url: string } | null) {
  const own = [...ownAddresses()].sort(([a], [b]) => a.localeCompare(b, undefined, { numeric: true }));
  const answering: [string, string][] = [];
  for (const [address, adapter] of own)
    if (await answers(address, port)) answering.push([`http://${address}:${port}`, adapter]);

  if (answering.length === 0) {
    console.error(red(`${label} does not answer at any address of this computer.`));
  } else {
    const width = Math.max(...answering.map(([url]) => url.length));
    console.log(`${label} answers at (tested from this computer):`);
    for (const [url, adapter] of answering) console.log(`  ${url.padEnd(width)}   ${adapter}`);
  }

  if (!given) return;
  const at = dialledAt(given.url);
  if (at && (await answers(at.host, at.port)))
    console.log(`${label} ${given.setting} ${given.url} answers — that is the address players are given.`);
  else
    console.error(red(`${label} ${given.setting} ${given.url} does not answer — anyone given that address `
                      + `cannot reach this service.`));
}

/// Say where this service answers, now and every time this computer's addresses change. `given` is the address
/// players' copies of the game are built to dial, when there is one.
export async function reportAddresses(label: string, port: number, given: { setting: string; url: string } | null) {
  await report(label, port, given);

  let seen = [...ownAddresses().keys()].sort().join(",");
  setInterval(async () => {
    const now = [...ownAddresses().keys()].sort().join(",");
    if (now === seen) return;
    seen = now;
    console.log(`${label} this computer's addresses changed:`);
    await report(label, port, given);
  }, RECHECK_MS).unref();
}
