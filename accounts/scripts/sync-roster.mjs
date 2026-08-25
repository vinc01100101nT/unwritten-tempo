// Re-exports the character-model roster from the Unity project into data/character-models.json.
// The project owns which models exist; this service only reads the list, so it can refuse an appearance the
// game could never draw. Run it after registering new models as Addressable.

import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const groupFile = fileURLToPath(
  new URL("../../unity/Assets/AddressableAssetsData/AssetGroups/Characters.asset", import.meta.url),
);
const outFile = fileURLToPath(new URL("../data/character-models.json", import.meta.url));

const source = readFileSync(groupFile, "utf8");
const models = [...source.matchAll(/m_Address: Characters\/(\S+)/g)].map((m) => m[1]).sort();

if (models.length === 0) {
  console.error("No character models found — has the Addressable group been created?");
  process.exit(1);
}

writeFileSync(
  outFile,
  JSON.stringify(
    {
      generatedFrom: "unity/Assets/AddressableAssetsData/AssetGroups/Characters.asset",
      note: "Generated. The Unity project owns this list; regenerate with accounts/scripts/sync-roster.mjs after adding models.",
      models,
    },
    null,
    2,
  ) + "\n",
);

console.log(`wrote ${models.length} model names to data/character-models.json`);
