// The check promised for this service, end to end against a real database: register, log in, refuse a wrong
// password, create two characters, save one character's progress, read it back byte-for-byte, and prove the
// other character was untouched. Requests go through the real server in-process (no port needed).
//
//   npm run migrate && npm test
//
// It creates its own throwaway account each run (a unique email) and deletes it at the end, so running it
// against a development database leaves nothing behind.

import assert from "node:assert/strict";
import { randomUUID } from "node:crypto";
import { buildServer } from "../src/server.ts";
import { query, closeDb } from "../src/db.ts";

const app = await buildServer();

const email = `smoke-${randomUUID()}@example.test`;
const password = "correct horse battery";
let accountId = "";

// A real save, in the shape the game writes today (version 7) — the point is that the document survives the
// round trip exactly as handed over, including the parallel arrays JsonUtility produces.
const save = {
  version: 7,
  level: 12,
  xp: 3400,
  currentHP: 210,
  currentMP: 88,
  primary: [14, 9, 12, 7, 11, 5],
  statPoints: 3,
  rankStats: [2, 5],
  rankValues: [3, 1],
  bankedPoints: 1,
  hasLeader: true,
  leader: 2,
  ringLevel: 4,
  ringXp: 120,
  gold: 15230,
  hotbarBindings: ["sword.lunge", "", "", "sword.parry", "", "", "", "", ""],
  activePassives: ["sword.momentum"],
  equipSlots: [0, 3],
  equipItemIds: ["sword.iron", "armor.leather"],
  equipRefines: [7, 0],
  equipCards: ["", ""],
  equipRarities: [3, 1],
  equipSkills: ["sword.lunge|sword.parry", ""],
  bagItemIds: ["potion.small"],
  bagCounts: [12],
  bagRefines: [0],
  bagCards: [""],
  bagRarities: [0],
  bagSkills: [""],
};
// Appearance is deliberately absent from the save: it lives on the character record, checked separately below.

async function step(name: string, run: () => Promise<void>) {
  await run();
  console.log(`  ok  ${name}`);
}

try {
  console.log(`\naccounts smoke test  (${email})\n`);

  await step("registers an account", async () => {
    const res = await app.inject({ method: "POST", url: "/accounts", payload: { email, password } });
    assert.equal(res.statusCode, 201, res.body);
    accountId = res.json().accountId;
    assert.ok(accountId);
  });

  await step("refuses to register the same email twice", async () => {
    const res = await app.inject({ method: "POST", url: "/accounts", payload: { email, password } });
    assert.equal(res.statusCode, 409, res.body);
  });

  await step("refuses a wrong password", async () => {
    const res = await app.inject({ method: "POST", url: "/sessions", payload: { email, password: "wrong wrong" } });
    assert.equal(res.statusCode, 401, res.body);
  });

  let accessToken = "";
  let refreshToken = "";
  await step("logs in", async () => {
    const res = await app.inject({ method: "POST", url: "/sessions", payload: { email, password } });
    assert.equal(res.statusCode, 200, res.body);
    ({ accessToken, refreshToken } = res.json());
    assert.ok(accessToken && refreshToken);
  });

  const auth = () => ({ authorization: `Bearer ${accessToken}` });

  await step("refuses to list characters without signing in", async () => {
    const res = await app.inject({ method: "GET", url: "/characters" });
    assert.equal(res.statusCode, 401, res.body);
  });

  const suffix = accountId.slice(0, 6).replace(/-/g, "");
  const heroName = `Hero_${suffix}`;
  const mageName = `Mage_${suffix}`;
  let heroId = "";
  let mageId = "";

  await step("creates two characters with different models and colours", async () => {
    const hero = await app.inject({
      method: "POST", url: "/characters", headers: auth(),
      payload: {
        name: heroName,
        modelName: "BlueSoldier_Male",
        dyeSources: ["#8A5A2B", "#3E4A6B"],
        dyeTargets: ["#B33A3A", "#1E2A4B"],
      },
    });
    assert.equal(hero.statusCode, 201, hero.body);
    heroId = hero.json().id;
    assert.equal(hero.json().modelName, "BlueSoldier_Male");
    assert.deepEqual(hero.json().dyeTargets, ["#B33A3A", "#1E2A4B"]);
    assert.equal(hero.json().level, null, "a character nobody has played yet has no level");

    const mage = await app.inject({
      method: "POST", url: "/characters", headers: auth(),
      payload: { name: mageName, modelName: "wizard" },   // lower case on purpose: the roster resolves casing
    });
    assert.equal(mage.statusCode, 201, mage.body);
    mageId = mage.json().id;
    assert.equal(mage.json().modelName, "Wizard");
  });

  await step("refuses a name that is already taken", async () => {
    const res = await app.inject({
      method: "POST", url: "/characters", headers: auth(),
      payload: { name: heroName.toLowerCase(), modelName: "Elf" },
    });
    assert.equal(res.statusCode, 409, res.body);
  });

  await step("refuses an unpaired recolour", async () => {
    const res = await app.inject({
      method: "POST", url: "/characters", headers: auth(),
      payload: { name: `Odd_${suffix}`, modelName: "Elf", dyeSources: ["#111111"], dyeTargets: [] },
    });
    assert.equal(res.statusCode, 400, res.body);
  });

  await step("refuses a colour that is not a colour", async () => {
    const res = await app.inject({
      method: "POST", url: "/characters", headers: auth(),
      payload: { name: `Bad_${suffix}`, modelName: "Elf", dyeSources: ["red"], dyeTargets: ["#111111"] },
    });
    assert.equal(res.statusCode, 400, res.body);
  });

  await step("refuses a model that is not in the roster", async () => {
    const res = await app.inject({
      method: "POST", url: "/characters", headers: auth(),
      payload: { name: `Ghost_${suffix}`, modelName: "NotARealModel" },
    });
    assert.equal(res.statusCode, 400, res.body);
  });

  await step("saves progress to one character", async () => {
    const res = await app.inject({
      method: "PUT", url: `/characters/${heroId}/progress`, headers: auth(), payload: { progress: save },
    });
    assert.equal(res.statusCode, 200, res.body);
    assert.equal(res.json().level, 12, "the list reads the level out of the save it stored");
  });

  await step("reads that progress back exactly as it was sent", async () => {
    const res = await app.inject({ method: "GET", url: `/characters/${heroId}/progress`, headers: auth() });
    assert.equal(res.statusCode, 200, res.body);
    assert.deepEqual(res.json().progress, save);
  });

  await step("left the other character untouched", async () => {
    const res = await app.inject({ method: "GET", url: `/characters/${mageId}/progress`, headers: auth() });
    assert.equal(res.statusCode, 200, res.body);
    assert.deepEqual(res.json().progress, {}, "never played, so still unstarted");
    assert.equal(res.json().level, null);
  });

  await step("refuses a save that is not a save", async () => {
    const res = await app.inject({
      method: "PUT", url: `/characters/${heroId}/progress`, headers: auth(), payload: { progress: { nope: true } },
    });
    assert.equal(res.statusCode, 400, res.body);
  });

  await step("hides another account's characters", async () => {
    const other = `smoke-${randomUUID()}@example.test`;
    await app.inject({ method: "POST", url: "/accounts", payload: { email: other, password } });
    const login = await app.inject({ method: "POST", url: "/sessions", payload: { email: other, password } });
    const otherAuth = { authorization: `Bearer ${login.json().accessToken}` };

    const list = await app.inject({ method: "GET", url: "/characters", headers: otherAuth });
    assert.deepEqual(list.json().characters, [], "a fresh account owns nobody");

    const peek = await app.inject({ method: "GET", url: `/characters/${heroId}/progress`, headers: otherAuth });
    assert.equal(peek.statusCode, 404, "someone else's character is not found, not forbidden");

    await query("delete from account where lower(email) = lower($1)", [other]);
  });

  await step("lists both characters for the owner", async () => {
    const res = await app.inject({ method: "GET", url: "/characters", headers: auth() });
    const names = res.json().characters.map((c: { name: string }) => c.name);
    assert.deepEqual(names, [heroName, mageName]);
  });

  await step("deletes a character and frees its name", async () => {
    const del = await app.inject({ method: "DELETE", url: `/characters/${mageId}`, headers: auth() });
    assert.equal(del.statusCode, 204, del.body);

    const list = await app.inject({ method: "GET", url: "/characters", headers: auth() });
    assert.deepEqual(list.json().characters.map((c: { name: string }) => c.name), [heroName]);

    const reuse = await app.inject({
      method: "POST", url: "/characters", headers: auth(), payload: { name: mageName, modelName: "Witch" },
    });
    assert.equal(reuse.statusCode, 201, reuse.body);
  });

  await step("changes a character's appearance without touching its progress", async () => {
    const recolour = await app.inject({
      method: "PATCH", url: `/characters/${heroId}/appearance`, headers: auth(),
      payload: { dyeSources: ["#8A5A2B"], dyeTargets: ["#2E7D32"] },
    });
    assert.equal(recolour.statusCode, 200, recolour.body);
    assert.deepEqual(recolour.json().dyeTargets, ["#2E7D32"]);
    assert.equal(recolour.json().modelName, "BlueSoldier_Male", "a recolour must not blank the body");

    const after = await app.inject({ method: "GET", url: `/characters/${heroId}/progress`, headers: auth() });
    assert.deepEqual(after.json().progress, save, "appearance and progress do not touch each other");
  });

  await step("refuses an appearance with a body that does not exist", async () => {
    const res = await app.inject({
      method: "PATCH", url: `/characters/${heroId}/appearance`, headers: auth(),
      payload: { modelName: "NotARealModel" },
    });
    assert.equal(res.statusCode, 400, res.body);
  });

  await step("trades a refresh token for a new session, once", async () => {
    const first = await app.inject({ method: "POST", url: "/sessions/refresh", payload: { refreshToken } });
    assert.equal(first.statusCode, 200, first.body);

    const replay = await app.inject({ method: "POST", url: "/sessions/refresh", payload: { refreshToken } });
    assert.equal(replay.statusCode, 401, "a spent refresh token cannot be used again");
  });

  console.log("\nall checks passed\n");
} finally {
  if (accountId) await query("delete from account where id = $1", [accountId]);
  await app.close();
  await closeDb();
}
