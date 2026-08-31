
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

/** A character nobody has played yet. It is NOT "the document is empty" any more: appearance is chosen at
 *  creation and lives in this document, so a brand-new character already carries something. What marks a save
 *  as a save is the game's own version stamp, which only the game writes, and only once it has played. */
export function isUnstarted(progress: unknown): boolean {
  if (typeof progress !== "object" || progress === null) return false;
  const version = (progress as { version?: unknown }).version;
  return !Number.isInteger(version) || (version as number) < 1;
}

/** The look a character wears, read out of the save. Kept beside `levelOf` for the same reason: the character
 *  list needs it before the game has loaded anything, and reading it here means it is not stored twice. */
export interface Appearance {
  modelName: string;
  dyeSources: string[];
  dyeTargets: string[];
}

/** The only keys a never-played character is allowed to carry. */
const APPEARANCE_KEYS = new Set(["modelName", "dyeSources", "dyeTargets"]);

const stringArray = (v: unknown): string[] =>
  Array.isArray(v) ? v.filter((s): s is string => typeof s === "string") : [];

export function appearanceOf(progress: unknown): Appearance {
  const doc = (typeof progress === "object" && progress !== null ? progress : {}) as Record<string, unknown>;
  return {
    modelName: typeof doc.modelName === "string" ? doc.modelName : "",
    dyeSources: stringArray(doc.dyeSources),
    dyeTargets: stringArray(doc.dyeTargets),
  };
}

export function checkProgress(progress: unknown): ProgressCheck {
  if (typeof progress !== "object" || progress === null || Array.isArray(progress)) {
    return { ok: false, reason: "progress must be a JSON object" };
  }

  // The size ceiling is enforced on the REQUEST (see server.ts), before the body is ever parsed. Measuring
  // it again here meant turning every save back into text purely to count its bytes.

  const version = (progress as { version?: unknown }).version;
  if (Number.isInteger(version) && (version as number) >= 1) return { ok: true };

  // No version. That is legitimate for exactly one document: a character created but never played, which
  // carries only the look chosen at creation. Anything else claiming to be a save must stamp itself — without
  // this, any object at all would be accepted and stored as somebody's character.
  const keys = Object.keys(progress as Record<string, unknown>);
  const looksOnly = keys.every((k) => APPEARANCE_KEYS.has(k));
  if (looksOnly) return { ok: true };

  return { ok: false, reason: "progress must carry a whole-number 'version'" };
}

/** The character's level as the save reports it, or null when it has never been played. Read out of the
 *  document rather than kept in a column beside it, so there is one owner of the number. */
export function levelOf(progress: unknown): number | null {
  if (typeof progress !== "object" || progress === null) return null;
  const level = (progress as { level?: unknown }).level;
  return Number.isInteger(level) ? (level as number) : null;
}
