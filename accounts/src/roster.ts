import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

// Which character models exist is decided by the Unity project, not by this service — the models are what
// they are, and a second hand-typed list here would drift the first time one is added. The list is exported
// from the project's Addressable group into data/character-models.json (npm run sync-roster) and only READ
// here, so this service can refuse an appearance the game could never draw.

interface RosterFile {
  models: string[];
}

const rosterPath = fileURLToPath(new URL("../data/character-models.json", import.meta.url));

const roster: RosterFile = JSON.parse(readFileSync(rosterPath, "utf8")) as RosterFile;

const byLowerName = new Map(roster.models.map((m) => [m.toLowerCase(), m]));

/** Every model name a character may be created with, in project order. */
export const characterModels: readonly string[] = Object.freeze([...roster.models]);

/** The canonically-cased model name, or null when the roster has no such model. */
export function resolveModel(name: string): string | null {
  return byLowerName.get(name.trim().toLowerCase()) ?? null;
}
