import { config } from "./config.ts";

// The save document belongs to the GAME — its fields, its version, its meaning. This service stores it and
// hands it back; it does not interpret it, and it must not grow a second opinion about what a character's
// numbers are. What it DOES do is refuse something that cannot be a save at all: not an object, absurdly
// large, or carrying no version. That is the boundary between "storing what I was given" and "trusting it".
//
// When the game server takes authority over these numbers, the validation that belongs to it goes THERE.
// Widening this check into gameplay rules would put the rules in two places.

export interface ProgressCheck {
  ok: boolean;
  reason?: string;
}

/** A character that has never been played carries {} — the game then builds a fresh one exactly as it does
 *  today when no save file exists, which is what keeps starting values in the game. */
export function isUnstarted(progress: unknown): boolean {
  return typeof progress === "object" && progress !== null && Object.keys(progress).length === 0;
}

export function checkProgress(progress: unknown): ProgressCheck {
  if (typeof progress !== "object" || progress === null || Array.isArray(progress)) {
    return { ok: false, reason: "progress must be a JSON object" };
  }

  const size = Buffer.byteLength(JSON.stringify(progress), "utf8");
  if (size > config.maxProgressBytes) {
    return { ok: false, reason: `progress is ${size} bytes, over the ${config.maxProgressBytes} byte limit` };
  }

  if (isUnstarted(progress)) return { ok: true };

  const version = (progress as { version?: unknown }).version;
  if (!Number.isInteger(version) || (version as number) < 1) {
    return { ok: false, reason: "progress must carry a whole-number 'version'" };
  }

  return { ok: true };
}

/** The character's level as the save reports it, or null when it has never been played. Read out of the
 *  document rather than kept in a column beside it, so there is one owner of the number. */
export function levelOf(progress: unknown): number | null {
  if (typeof progress !== "object" || progress === null) return null;
  const level = (progress as { level?: unknown }).level;
  return Number.isInteger(level) ? (level as number) : null;
}
