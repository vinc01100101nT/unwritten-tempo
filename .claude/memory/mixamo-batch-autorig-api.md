# Mixamo batch auto-rig via API (no manual UI)

**What:** Rig + download a whole folder of Quaternius meshes through Mixamo's Auto-Rigger
WITHOUT touching the marker-placement UI. `tools/mixamo_batch_rig.py` loops mesh-only FBX
(from `tools/export_mesh_for_rigging.py`) → uploads → rigs with a canned marker set →
downloads the T-Pose FBX to `unity/Assets/Art/Quat/Rigged`.

**Why it works:** every mesh is exported IDENTICALLY (transform applied, same scale/orient/pose,
materials stripped), so ONE captured `rigging_inputs` marker set lands correctly on all of them.
Verified: Unity Humanoid avatar mapped ALL-GREEN across different bodies. This is the
single-source-of-truth trick applied to marker coords — one set, all meshes read it.

## The 4-step HTTP pipeline (all under https://www.mixamo.com/api/v1)
Headers on every call: `authorization: Bearer <JWT>`, `x-api-key: mixamo2`,
`x-requested-with: XMLHttpRequest`. Token is a short-lived (~1h) Adobe IMS JWT — grab fresh
from any Mixamo request's Authorization header; pass via `--token` or `MIXAMO_TOKEN`.

1. **Upload** `POST /characters` (multipart `file=`) → async. Response is
   `{status:"processing", uuid:..., job_type:"character_mapper"}`. **Character id = `uuid`**
   (NOT `id`). Must then WAIT for the mapper job before rigging.
2. **Wait mapper** `GET /characters/{id}/monitor` → 202 working / 200 done.
3. **Rig** `PUT /characters/{id}/rig`, body `{"rigging_inputs":{...}}`. GOTCHA:
   `symmetric`, `for:"both"`, `skeleton-lod:"NoFingers"` live INSIDE `rigging_inputs`,
   not at top level. Returns 202.
4. **Wait rig** same monitor → 200.
5. **Export** `POST /animations/export` — even a bare rigged char is exported as a
   **`type:"Motion"`** carrying the standard **T-Pose** animation. REQUIRES `gms_hash`:
   `[{"model-id":115510901,"mirror":false,"trim":[0,100],"overdrive":0,"params":"1,0","arm-space":0,"inplace":false}]`
   (`model-id 115510901` = the T-Pose product, CONSTANT for all chars). Missing gms_hash →
   server error `no implicit conversion of nil into String`.
   `preferences:{format:"fbx7_2019",skin:"true",fps:"30",reducekf:"0"}`, `product_name:"T-Pose"`.
6. **Wait export** same monitor → 200 body carries **`job_result`** = presigned S3 URL.
   Guard a race: right after export POST the monitor can still return the PREVIOUS 200
   (rig-done, no url) — keep polling until the 200 body actually has an http url.
7. **Download** GET the presigned S3 URL with a **BARE request, NO auth headers**. Sending
   Mixamo's Authorization header → S3 rejects: *"Only one auth mechanism allowed"* (the
   presigned url already carries its signature in the query string).

## Marker set
Captured from a hand-tuned rig (`z≈524.68`, NoFingers), embedded as `EMBEDDED_MARKERS`,
override via `--markers file.json`. Coords are in the mesh's own space — only reusable
because all Quaternius exports share scale/orient/pose.

## Eyeball check (Unity)
FBX → Inspector ▸ Rig ▸ Animation Type=Humanoid, Avatar=Create From This Model, Apply →
Configure… → mapping diagram. ALL GREEN + clean bind pose = markers transferred fine.

## Op notes
- Script skips files already present in `--out` → token expiry (401/403) stops it cleanly,
  grab fresh JWT and re-run to resume.
- `REQUEST_GAP` between chars to be gentle on the API.
- cmd.exe not bash: `set VAR=val` (no quotes), not `export`.

Related: [[mixamo-on-quaternius-generic-1to1]], [[quaternius-humanoid-drops-clips]],
[[character-art-quaternius-charcoal]].
