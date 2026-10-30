"use strict";
// THE 3D VIEW: the town's ground laid in 3D by the town builder's own rules — its levels with a curb face along every edge
// between two, the hand-raised heights, the stream's bed and water, the bridges fitted to their crossings and the ramps and
// mounds round them, every point wearing its tile and two tiles fading into each other across a triangle as the game draws
// them — seen from the game camera's own tilt and field of view, with the pieces and plants standing on it as Unity
// photographed them from that tilt. It only shows the town: the editing is done in the top-down view beside it, and every
// change there is laid here at once. The levels, curbs and tiles come from the page's own functions (levelAt, levelsAt,
// tileAt, builtLook, liftAt); the numbers the builder lays by, the camera and the bridge's model come from the pictures'
// index, written by Unity from the project itself.
const view3d = (() => {
  const STEP = 0.25;   // the ground's points, a quarter unit apart (the town builder's GroundCell, the curb trace's step)
  let on = false, gl = null, cv3 = null, progs = null;
  let cam = null, frame = 0;
  // what the shown ground was made from, and what it needs redoing for
  let builtFrom = -1, liftedAt = -1, forPictures = null, forPlan = null;
  let job = null, jobFor = -1, samples = null, bridges = [], items = [], waterN = 0;
  const textures = new Map();
  let tileArray = null, tileKey = "";
  const stats = {build: 0, lift: 0, items: 0, render: 0, frames: 0, vertices: 0, billboards: 0};

  const numbers = () => pictures.ground, camera = () => pictures.camera;
  const rise = () => numbers().levelHeight;
  const smooth01 = k => { k = Math.max(0, Math.min(1, k)); return k * k * (3 - 2 * k); };
  const clamp01 = k => Math.max(0, Math.min(1, k));
  const hasWater = () => plan.canals.length > 0;

  // ---- the town's shape beyond its curbs: the town builder's Shape --------------------------------------------------
  // How far a point is inside the stream (above 0 in it); past the middle of each mound's bridge the stream is gone.
  function sinkAt(x, z, mounds) {
    let g = -1;
    for (const c of plan.canals) g = Math.max(g, c.width * 0.5 - lineDist(c.xz, x, z));
    if (g <= -1) return g;
    for (const m of mounds) g = Math.min(g, -((x - m.at[0]) * m.dir[0] + (z - m.at[1]) * m.dir[1]) * 8);
    return g;
  }
  function moundLift(mounds, x, z) {
    let h = 0;
    for (const m of mounds) {
      const d = m.dir, px = x - m.at[0], pz = z - m.at[1], v = px * d[0] + pz * d[1], u = Math.abs(px * d[1] - pz * d[0]), back = m.bridgeWidth * 0.5;
      if (v < 0 || v > back + m.length) continue;
      const along = v < back ? smooth01(v / back) : 1 - smooth01((v - back) / m.length);
      const across = 1 - smooth01((u - (m.bridgeLength * 0.5 - 0.3)) / 2.5);
      h = Math.max(h, m.height * along * clamp01(across));
    }
    return h;
  }
  const handLift = (x, z, sink) => { const h = liftAt(x, z); return hasWater() && h !== 0 ? h * clamp01(-sink) : h; };
  function liftOf(x, z, sink) {
    const n = numbers(), hand = handLift(x, z, sink);
    let h = hand + moundLift(shape.mounds, x, z);
    for (const f of shape.footprints) {
      const px = x - f.mid[0], pz = z - f.mid[1], s = px * f.dir[0] + pz * f.dir[1], u = Math.abs(px * f.dir[1] - pz * f.dir[0]);
      if (Math.abs(s) > f.halfLength || u > f.halfWidth + 0.25 || (hasWater() && sink > -0.3)) continue;
      let k = 0;
      while (k + 2 < f.along.length && f.along[k + 1] < s) k++;
      const t = f.along[k + 1] === f.along[k] ? 0 : clamp01((s - f.along[k]) / (f.along[k + 1] - f.along[k]));
      h = Math.max(h, f.height[k] + (f.height[k + 1] - f.height[k]) * t - n.levelHeight - 0.02);
    }
    for (const r of shape.ramps) {
      const px = x - r.at[0], pz = z - r.at[1], beyond = px * r.out[0] + pz * r.out[1], side = Math.abs(px * r.out[1] - pz * r.out[0]);
      if (beyond < -0.3 || beyond > n.rampLength || side > r.width * 0.5 + 0.4) continue;
      const k = 1 - smooth01(Math.max(0, beyond) / n.rampLength), s = 1 - smooth01((side - r.width * 0.5) / 0.4);
      h = Math.max(h, hand + r.above * k * clamp01(s));
    }
    return h;
  }
  let shape = {mounds: [], footprints: [], ramps: []};

  // THE BRIDGES, the town builder's Bridges and BridgeDecks.Fit: the model as long as its crossing, at its own proportions,
  // centred at the road's height, its walkway's middle sampled from above every deckAlong along it.
  let model = null;
  function bridgeModel() {
    const m = pictures.bridgeModel;
    if (!m) return null;
    if (model && model.src === m) return model;
    const tris = [];
    for (let k = 0; k + 2 < m.tris.length; k += 3) {
      const a = m.tris[k] * 3, b = m.tris[k + 1] * 3, c = m.tris[k + 2] * 3;
      tris.push([m.v[a], m.v[a + 1], m.v[a + 2], m.v[b], m.v[b + 1], m.v[b + 2], m.v[c], m.v[c + 1], m.v[c + 2]]);
    }
    return (model = {src: m, tris});
  }
  // The highest point of the model straight under (lx, lz), in its own units; NaN where the line misses it.
  function topUnder(tris, lx, lz) {
    let best = NaN;
    for (const t of tris) {
      const d = (t[5] - t[8]) * (t[0] - t[6]) + (t[6] - t[3]) * (t[2] - t[8]);
      if (Math.abs(d) < 1e-12) continue;
      const a = ((t[5] - t[8]) * (lx - t[6]) + (t[6] - t[3]) * (lz - t[8])) / d, b = ((t[8] - t[2]) * (lx - t[6]) + (t[0] - t[6]) * (lz - t[8])) / d, c = 1 - a - b;
      if (a < 0 || b < 0 || c < 0) continue;
      const y = a * t[1] + b * t[4] + c * t[7];
      if (!(y <= best)) best = y;
    }
    return best;
  }
  function fitBridges() {
    const n = numbers(), m = bridgeModel(), R = n.levelHeight;
    shape = {mounds: [], footprints: [], ramps: []};
    bridges = [];
    if (!m) return;
    for (const b of plan.bridges) {
      const dir = [Math.sin(b.bearing * Math.PI / 180), Math.cos(b.bearing * Math.PI / 180)], mid = [b.x, b.z];
      const from = [mid[0] - dir[0] * b.length / 2, mid[1] - dir[1] * b.length / 2], to = [mid[0] + dir[0] * b.length / 2, mid[1] + dir[1] * b.length / 2];
      const at = p => moundLift(shape.mounds, p[0], p[1]) + handLift(p[0], p[1], sinkAt(p[0], p[1], shape.mounds));
      const height = R + Math.max(at(from), at(to));
      const size = m.src.size, centre = m.src.center, s = b.length / size[2];
      const u = [dir[0], dir[1]], right = [u[1], -u[0]];
      const pos = [mid[0] - (right[0] * centre[0] + u[0] * centre[2]) * s, height, mid[1] - (right[1] * centre[0] + u[1] * centre[2]) * s];
      const rows = Math.ceil(b.length / n.deckAlong) + 1, top = new Float32Array(rows);
      for (let a = 0; a < rows; a++) {
        const wx = from[0] + (to[0] - from[0]) * a / (rows - 1), wz = from[1] + (to[1] - from[1]) * a / (rows - 1);
        const lx = ((wx - pos[0]) * right[0] + (wz - pos[2]) * right[1]) / s, lz = ((wx - pos[0]) * u[0] + (wz - pos[2]) * u[1]) / s;
        const y = topUnder(m.tris, lx, lz);
        top[a] = isNaN(y) ? NaN : pos[1] + y * s - n.deckSink;
      }
      const has = a => !isNaN(top[a]);
      for (let a = 0; a < rows; a++) {
        if (has(a)) continue;
        let lo = a - 1, hi = a + 1;
        while (lo >= 0 && !has(lo)) lo--;
        while (hi < rows && !has(hi)) hi++;
        if (lo < 0 && hi >= rows) continue;
        const f0 = lo >= 0 ? lo : hi, t0 = hi < rows ? hi : lo, w = f0 === t0 ? 0 : (a - f0) / (t0 - f0);
        top[a] = top[f0] + (top[t0] - top[f0]) * w;
      }
      bridges.push({pos, right, u, s});
      const along = [], deckY = [];
      for (let a = 0; a < rows; a++) if (has(a)) { along.push((a / (rows - 1) - 0.5) * b.length); deckY.push(top[a]); }
      if (along.length >= 2) shape.footprints.push({mid, dir, halfLength: b.length / 2, halfWidth: b.width / 2, along, height: deckY});
      for (const [row, end, out] of [[0, from, [-dir[0], -dir[1]]], [rows - 1, to, dir]]) {
        if (!has(row)) continue;
        const above = top[row] - height;
        if (above > 0.01) shape.ramps.push({at: end, out, width: b.width, above});
      }
      for (const mo of plan.mounds) {
        if (Math.abs(mo.x - b.x) > 0.01 || Math.abs(mo.z - b.z) > 0.01) continue;
        const t = has(rows >> 1) ? top[rows >> 1] : height + 0.5, L = Math.hypot(mo.dir[0], mo.dir[1]) || 1;
        shape.mounds.push({at: mid, dir: [mo.dir[0] / L, mo.dir[1] / L], bridgeWidth: b.width, bridgeLength: b.length, length: mo.length, width: mo.width, height: Math.max(0.2, t - R)});
      }
    }
  }

  // ---- the ground: GroundBlendBuilder.LayPlotCurbed ------------------------------------------------------------------
  // Every point of the ground says where its height is read: a sample of the lift (the hand-raised ground, mounds, ramps),
  // shared by every point at the same spot. Painting heights redoes only the samples, never the ground's shape.
  function newSamples(n) {
    return {n: 1, x: new Float32Array(n), z: new Float32Array(n), g: new Float32Array(n), key: new Map()};
  }
  function sampleAt(S, x, z, g) {
    const key = Math.round(x * 1000) * 100003 + Math.round(z * 1000);
    const had = S.key.get(key); if (had !== undefined) return had;
    const at = addSample(S, x, z, g); S.key.set(key, at);
    return at;
  }
  function addSample(S, x, z, g) {
    if (S.n === S.x.length) for (const k of ["x", "z", "g"]) { const a = new Float32Array(S[k].length * 2); a.set(S[k]); S[k] = a; }
    S.x[S.n] = x; S.z[S.n] = z; S.g[S.n] = g;
    return S.n++;
  }
  // A growing list of ground points, PER floats each: x, z, base height, lift sample, its triangle's three tiles and
  // their tilings, which corner of it the point is, what it is (0 tiled ground, 1 a curb's face, 2 water, 3 the band of
  // curb stones on a raised top) and, on the curb's stones, where in the curb's picture it lies.
  const PER = 16;
  function points() {
    let a = new Float32Array(PER * 65536), n = 0;
    return {
      add(x, z, base, ref, t0, t1, t2, k0, k1, k2, corner, kind, u = 0, v = 0) {
        if (n + PER > a.length) { const b = new Float32Array(a.length * 2); b.set(a); a = b; }
        a[n] = x; a[n + 1] = z; a[n + 2] = base; a[n + 3] = ref; a[n + 4] = t0; a[n + 5] = t1; a[n + 6] = t2; a[n + 7] = k0; a[n + 8] = k1; a[n + 9] = k2;
        a[n + 10] = corner === 0 ? 1 : 0; a[n + 11] = corner === 1 ? 1 : 0; a[n + 12] = corner === 2 ? 1 : 0; a[n + 13] = kind;
        a[n + 14] = u; a[n + 15] = v;
        n += PER;
      },
      get data() { return a.subarray(0, n); }, get count() { return n / PER; },
    };
  }

  // Worked out a few rows of cells at a time (a generator), so the page never stalls while it is laid.
  function* buildGround(cc) {
    let busy = 0, from = performance.now();
    const areas = bedsAndAreas().areas, st = cc.st, nx = cc.nx, nz = cc.nz, w = nx + 1, R = rise();
    const n = numbers(), byName = new Map(plan.tiles.map((t, k) => [t.name, k])), soil = byName.get(plan.soilTile) || 0;
    fitBridges();
    const mounds = shape.mounds;
    const S = newSamples(w * (nz + 1) * 2);
    const G = new Float32Array(w * (nz + 1)), gridTile = new Float32Array(w * (nz + 1));
    for (let j = 0; j <= nz; j++) {
      for (let i = 0; i <= nx; i++) {
        const k = j * w + i, x = -HALF_X + i * STEP, z = -HALF_Z + j * STEP;
        G[k] = sinkAt(x, z, mounds); addSample(S, x, z, G[k]); gridTile[k] = byName.get(cc.tiles[k]) || 0;
      }
      if (j % 32 === 31) { busy += performance.now() - from; yield; from = performance.now(); }
    }
    const out = points(), water = points();
    const tilings = plan.tiles.map(t => t.tiling), tiling = t => tilings[t] || 1;
    const tri = (list, p, kind) => {
      const t0 = p[0].tile, t1 = p[1].tile, t2 = p[2].tile, k0 = tiling(t0), k1 = tiling(t1), k2 = tiling(t2);
      for (let q = 0; q < 3; q++) list.add(p[q].x, p[q].z, p[q].base, p[q].ref, t0, t1, t2, k0, k1, k2, q, kind, p[q].u, p[q].v);
    };
    // THE CURB'S STONES, the town builder's: its face and, on the raised top along its edge, a band of stones `band` wide,
    // both drawn with the curb's picture, one picture every `along` units along the curb. The way along is read off the
    // field's slope across the triangle being laid, so face and band line up.
    const band = n.curbBand || 0, along = n.curbAlong || 1;
    const alongOf = (t3, val) => {
      const [a, b, c] = t3, ux = b.x - a.x, uz = b.z - a.z, wx = c.x - a.x, wz = c.z - a.z, du = val(b) - val(a), dw = val(c) - val(a);
      const d = ux * wz - uz * wx; if (Math.abs(d) < 1e-12) return [1, 0];
      const gx = (du * wz - dw * uz) / d, gz = (dw * ux - du * wx) / d, L = Math.hypot(gx, gz) || 1;
      return [-gz / L, gx / L];
    };
    const stones = (piece, dir, val, base) => {
      const pts = piece.map(p => ({x: p.x, z: p.z, ref: p.ref, tile: 0, base, u: (p.x * dir[0] + p.z * dir[1]) / along, v: clamp01(val(p) / band)}));
      for (let k = 1; k + 1 < pts.length; k++) tri(out, [pts[0], pts[k], pts[k + 1]], 3);
    };
    const vert = (x, z, f, g, ref) => ({x, z, f, g, ref});
    const lerp = (a, b, t) => {
      const f = new Float32Array(a.f.length);
      for (let k = 0; k < f.length; k++) f[k] = a.f[k] + (b.f[k] - a.f[k]) * t;
      const x = a.x + (b.x - a.x) * t, z = a.z + (b.z - a.z) * t, g = a.g + (b.g - a.g) * t;
      return vert(x, z, f, g, sampleAt(S, x, z, g));
    };
    const split = (poly, val) => {
      const pos = [], neg = [];
      for (let k = 0; k < poly.length; k++) {
        const a = poly[k], b = poly[(k + 1) % poly.length], va = val(a), vb = val(b);
        (va > 0 ? pos : neg).push(a);
        if ((va > 0) !== (vb > 0)) { const c = lerp(a, b, va / (va - vb)); pos.push(c); neg.push(c); }
      }
      return [pos.length >= 3 ? pos : null, neg.length >= 3 ? neg : null];
    };
    const tileCache = new Map();
    const tileNear = (p, cx, cz) => {
      // A point on a region's edge takes its tile from just inside its own region.
      const dx = cx - p.x, dz = cz - p.z, d = Math.hypot(dx, dz), k = d > 1e-6 ? Math.min(0.02, d) / d : 0;
      const qx = p.x + dx * k, qz = p.z + dz * k, key = Math.round(qx * 1000) * 100003 + Math.round(qz * 1000);
      let t = tileCache.get(key); if (t === undefined) tileCache.set(key, t = byName.get(tileAt(areas, qx, qz)) || 0);
      return t;
    };
    const lay = (piece, t3) => {
      let cx = 0, cz = 0, g = 0; const f = new Float32Array(st.steps);
      for (const p of piece) { cx += p.x; cz += p.z; g += p.g; for (let k = 0; k < st.steps; k++) f[k] += p.f[k]; }
      cx /= piece.length; cz /= piece.length; g /= piece.length;
      let level = st.lowest; for (let k = 0; k < st.steps; k++) if (f[k] > 0) level++;
      const wet = g > 0;
      // a raised top: the band of stones along the curb that raised it, then the rest of it
      if (!wet && band > 0 && level > st.lowest && t3) {
        const t = level - st.lowest - 1, val = v => v.f[t], [inner, edge] = split(piece, v => v.f[t] - band);
        if (edge) stones(edge, alongOf(t3, val), val, level * R);
        if (!inner) return;
        if (edge) { piece = inner; cx = 0; cz = 0; for (const p of piece) { cx += p.x; cz += p.z; } cx /= piece.length; cz /= piece.length; }
      }
      const pts = piece.map(p => ({x: p.x, z: p.z, ref: p.ref, tile: wet ? soil : tileNear(p, cx, cz),
                                   base: wet ? R - Math.min(n.bedDepth + R, Math.max(0, p.g) * n.bankSlope) : level * R}));
      for (let k = 1; k + 1 < pts.length; k++) {
        tri(out, [pts[0], pts[k], pts[k + 1]], 0);
        if (wet) tri(water, [pts[0], pts[k], pts[k + 1]].map(p => ({x: p.x, z: p.z, ref: 0, tile: 0, base: R - n.waterBelow})), 2);
      }
    };
    const ks = [0, 0, 0, 0], X = [0, 0, 0, 0], Z = [0, 0, 0, 0];
    for (let j = 0; j < nz; j++) {
     if (j % 8 === 7) { busy += performance.now() - from; yield; from = performance.now(); }
     for (let i = 0; i < nx; i++) {
      ks[0] = j * w + i; ks[1] = ks[0] + 1; ks[2] = ks[0] + w + 1; ks[3] = ks[0] + w;
      // plain: no curb, no band of stones and no water's edge in it (the town builder's Plain)
      let plain = G[ks[0]] <= -band && G[ks[1]] <= -band && G[ks[2]] <= -band && G[ks[3]] <= -band;
      for (let t = 0; plain && t < st.steps; t++) {
        let lo = Infinity, hi = -Infinity; for (const k of ks) { lo = Math.min(lo, cc.fs[k][t]); hi = Math.max(hi, cc.fs[k][t]); }
        if (!(lo > band || hi <= 0)) plain = false;
      }
      if (plain) {
        // two triangles straight from the grid: the cell stands at one level, its corners' samples known
        let level = st.lowest; for (let t = 0; t < st.steps; t++) if (cc.fs[ks[0]][t] > 0) level++;
        const base = level * R, x0 = -HALF_X + i * STEP, z0 = -HALF_Z + j * STEP;
        X[0] = X[3] = x0; X[1] = X[2] = x0 + STEP; Z[0] = Z[1] = z0; Z[2] = Z[3] = z0 + STEP;
        // each corner wears the tile just inside this cell, as the town builder colours it: a corner lying on an area's
        // edge would otherwise wear the tile across the edge, and fade it into this cell
        const ct = ks.map(k => gridTile[k]);
        if (ct[0] !== ct[1] || ct[0] !== ct[2] || ct[0] !== ct[3])
          for (let q = 0; q < 4; q++) ct[q] = tileNear({x: X[q], z: Z[q]}, x0 + STEP / 2, z0 + STEP / 2);
        for (let h = 0; h < 2; h++) {
          const a = 0, b = h ? 2 : 1, c = h ? 3 : 2;
          const t0 = ct[a], t1 = ct[b], t2 = ct[c], k0 = tiling(t0), k1 = tiling(t1), k2 = tiling(t2);
          out.add(X[a], Z[a], base, 1 + ks[a], t0, t1, t2, k0, k1, k2, 0, 0);
          out.add(X[b], Z[b], base, 1 + ks[b], t0, t1, t2, k0, k1, k2, 1, 0);
          out.add(X[c], Z[c], base, 1 + ks[c], t0, t1, t2, k0, k1, k2, 2, 0);
        }
        continue;
      }
      const corners = ks.map(k => vert(-HALF_X + (k % w) * STEP, -HALF_Z + Math.floor(k / w) * STEP, cc.fs[k], G[k], 1 + k));
      // a cell a curb or the water runs through: four triangles round its middle, each cut along every curb and the
      // water's edge
      const mx = corners[0].x + STEP / 2, mz = corners[0].z + STEP / 2, mg = sinkAt(mx, mz, mounds);
      const m = vert(mx, mz, levelsAt(areas, st, mx, mz), mg, sampleAt(S, mx, mz, mg));
      for (let q = 0; q < 4; q++) {
        const t3 = [corners[q], corners[(q + 1) % 4], m];
        let pieces = [t3];
        for (let t = 0; t < st.steps; t++) {
          const next = [];
          for (const p of pieces) for (const half of split(p, v => v.f[t])) if (half) next.push(half);
          pieces = next;
        }
        for (const p of pieces) {
          const [wet, dry] = split(p, v => v.g);
          if (wet) lay(wet, t3);
          if (!dry) continue;
          // the band of stones along the water, one curb up (the town builder's), then the dry ground
          const [near, rest] = band > 0 ? split(dry, v => v.g + band) : [null, dry];
          if (near) stones(near, alongOf(t3, v => -v.g), v => band + v.g, R);
          if (rest) lay(rest, t3);
        }
        // a curb's face from `lo` up to `hi` along a→b, its picture running the way `dir` points
        const face = (a, b, lo, hi, dir) => {
          const at = (p, base, v) => ({x: p.x, z: p.z, ref: p.ref, tile: 0, base, u: (p.x * dir[0] + p.z * dir[1]) / along, v});
          tri(out, [at(a, lo, 0), at(b, lo, 0), at(b, hi, 1)], 1);
          tri(out, [at(a, lo, 0), at(b, hi, 1), at(a, hi, 1)], 1);
        };
        // the curb's face along each step's line across this triangle, on dry ground, from the step below to its top
        for (let t = 0; t < st.steps; t++) {
          const cut = [];
          for (let e = 0; e < 3; e++) {
            const a = t3[e], b = t3[(e + 1) % 3];
            if ((a.f[t] > 0) !== (b.f[t] > 0)) cut.push(lerp(a, b, a.f[t] / (a.f[t] - b.f[t])));
          }
          if (cut.length < 2 || (cut[0].g + cut[1].g) / 2 > -band) continue;
          const lo = (st.lowest + t) * R;
          face(cut[0], cut[1], lo, lo + R, alongOf(t3, v => v.f[t]));
        }
        // where low ground reaches the band of stones along the water, the band stands up out of it like a curb
        if (band > 0 && -st.lowest < st.steps) {
          const t1 = -st.lowest, cut = [];   // the step up to level 1, where the band stands
          for (let e = 0; e < 3; e++) {
            const a = t3[e], b = t3[(e + 1) % 3], va = a.g + band, vb = b.g + band;
            if ((va > 0) !== (vb > 0)) cut.push(lerp(a, b, va / (va - vb)));
          }
          if (cut.length === 2) {
            let [a, b] = cut; const fa = a.f[t1], fb = b.f[t1];
            if (!(fa >= 0 && fb >= 0)) {
              if (fa >= 0) a = lerp(a, b, fa / (fa - fb)); else if (fb >= 0) b = lerp(a, b, fa / (fa - fb));
              if (Math.hypot(b.x - a.x, b.z - a.z) > 1e-4) face(a, b, 0, R, alongOf(t3, v => v.g));
            }
          }
        }
      }
     }
    }
    stats.build = busy + performance.now() - from;
    return {data: out.data, count: out.count, water: water.data, waterCount: water.count, samples: S};
  }

  // ---- WebGL ---------------------------------------------------------------------------------------------------------
  const GROUND_VS = `#version 300 es
  in vec2 a_xz; in float a_base; in float a_ref; in vec3 a_tiles; in vec3 a_tilings; in vec3 a_bary; in float a_kind; in vec2 a_uv;
  uniform mat4 u_vp; uniform highp sampler2D u_lift; uniform int u_liftW;
  out vec3 v_pos; flat out vec3 v_tiles; flat out vec3 v_tilings; out vec3 v_bary; flat out float v_kind; out vec2 v_uv;
  void main() {
    int r = int(a_ref + 0.5);
    float y = a_base + texelFetch(u_lift, ivec2(r % u_liftW, r / u_liftW), 0).r;
    v_pos = vec3(a_xz.x, y, a_xz.y); v_tiles = a_tiles; v_tilings = a_tilings; v_bary = a_bary; v_kind = a_kind; v_uv = a_uv;
    gl_Position = u_vp * vec4(v_pos, 1.0);
  }`;
  const LIGHT_FS = `
  uniform vec3 u_eye; uniform vec3 u_light;
  float lit(vec3 p) {
    vec3 n = normalize(cross(dFdx(p), dFdy(p)));
    if (dot(n, u_eye - p) < 0.0) n = -n;
    return 0.55 + 0.6 * max(0.0, dot(n, u_light));
  }`;
  const GROUND_FS = `#version 300 es
  precision highp float; precision highp sampler2DArray;
  in vec3 v_pos; flat in vec3 v_tiles; flat in vec3 v_tilings; in vec3 v_bary; flat in float v_kind; in vec2 v_uv;
  uniform sampler2DArray u_tiles; uniform bool u_ready; uniform vec3 u_curb; uniform vec4 u_water;
  uniform sampler2D u_curbTex; uniform bool u_curbReady;
  out vec4 colour;
  ${LIGHT_FS}
  vec3 tile(float t, float k) { return u_ready ? texture(u_tiles, vec3(v_pos.xz * k, t)).rgb : vec3(0.45, 0.5, 0.4); }
  void main() {
    if (abs(v_kind - 2.0) < 0.5) { colour = u_water; return; }
    vec3 albedo;
    // the curb's face and its band of stones, in the curb's own picture
    if (v_kind > 0.5) albedo = u_curbReady ? texture(u_curbTex, v_uv).rgb : u_curb;
    else {
      albedo = tile(v_tiles.x, v_tilings.x);
      if (v_tiles.x != v_tiles.y || v_tiles.x != v_tiles.z)
        albedo = albedo * v_bary.x + tile(v_tiles.y, v_tilings.y) * v_bary.y + tile(v_tiles.z, v_tilings.z) * v_bary.z;
    }
    colour = vec4(albedo * lit(v_pos), 1.0);
  }`;
  const PICTURE_VS = `#version 300 es
  in vec3 a_pos; in vec2 a_uv; uniform mat4 u_vp; out vec3 v_pos; out vec2 v_uv;
  void main() { v_pos = a_pos; v_uv = a_uv; gl_Position = u_vp * vec4(a_pos, 1.0); }`;
  const PICTURE_FS = `#version 300 es
  precision highp float;
  in vec3 v_pos; in vec2 v_uv; uniform sampler2D u_pic; uniform bool u_lit; out vec4 colour;
  ${LIGHT_FS}
  void main() {
    vec4 c = texture(u_pic, v_uv);
    if (c.a < 0.5) discard;
    colour = vec4(c.rgb * (u_lit ? lit(v_pos) : 1.0), 1.0);
  }`;
  function program(vs, fs) {
    const make = (type, src) => { const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s);
      if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; };
    const p = gl.createProgram(); gl.attachShader(p, make(gl.VERTEX_SHADER, vs)); gl.attachShader(p, make(gl.FRAGMENT_SHADER, fs)); gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    const u = {}; for (let k = 0, n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS); k < n; k++) { const name = gl.getActiveUniform(p, k).name; u[name] = gl.getUniformLocation(p, name); }
    const a = {}; for (let k = 0, n = gl.getProgramParameter(p, gl.ACTIVE_ATTRIBUTES); k < n; k++) { const name = gl.getActiveAttrib(p, k).name; a[name] = gl.getAttribLocation(p, name); }
    return {p, u, a};
  }
  function setup() {
    cv3 = $("cv3");
    gl = cv3.getContext("webgl2", {antialias: true});
    if (!gl) throw new Error("this browser has no WebGL2");
    progs = {ground: program(GROUND_VS, GROUND_FS), picture: program(PICTURE_VS, PICTURE_FS)};
    progs.groundVao = groundVao(); progs.waterVao = groundVao();
    progs.pictureBuf = gl.createBuffer(); progs.pictureVao = gl.createVertexArray();
    gl.bindVertexArray(progs.pictureVao); gl.bindBuffer(gl.ARRAY_BUFFER, progs.pictureBuf);
    const pa = progs.picture.a;
    gl.enableVertexAttribArray(pa.a_pos); gl.vertexAttribPointer(pa.a_pos, 3, gl.FLOAT, false, 20, 0);
    gl.enableVertexAttribArray(pa.a_uv); gl.vertexAttribPointer(pa.a_uv, 2, gl.FLOAT, false, 20, 12);
    gl.bindVertexArray(null);
    progs.lift = gl.createTexture();
    cv3.addEventListener("contextmenu", e => e.preventDefault());
    cv3.addEventListener("pointerdown", pointerDown);
    cv3.addEventListener("pointermove", pointerMove);
    cv3.addEventListener("pointerup", e => { drag = null; cv3.releasePointerCapture(e.pointerId); });
    cv3.addEventListener("wheel", e => { e.preventDefault(); cam.dist = Math.max(camera().minDistance, Math.min(400, cam.dist * Math.pow(1.0015, e.deltaY))); ask(); }, {passive: false});
  }
  function groundVao() {
    const vao = gl.createVertexArray(), buf = gl.createBuffer(), a = progs.ground.a;
    gl.bindVertexArray(vao); gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    const at = [["a_xz", 2, 0], ["a_base", 1, 2], ["a_ref", 1, 3], ["a_tiles", 3, 4], ["a_tilings", 3, 7], ["a_bary", 3, 10], ["a_kind", 1, 13], ["a_uv", 2, 14]];
    for (const [name, size, off] of at) { gl.enableVertexAttribArray(a[name]); gl.vertexAttribPointer(a[name], size, gl.FLOAT, false, PER * 4, off * 4); }
    gl.bindVertexArray(null);
    return {vao, buf, count: 0};
  }
  function upload(target, data, count) {
    gl.bindBuffer(gl.ARRAY_BUFFER, target.buf); gl.bufferData(gl.ARRAY_BUFFER, data, gl.STATIC_DRAW); target.count = count;
  }
  // New pictures are sent to the graphics card a few milliseconds' worth a frame, so opening the view does not stall
  // the page; one not sent yet is drawn on a later frame.
  let sendUntil = 0, unsent = false;
  function texture(url) {
    let t = textures.get(url);
    if (t) return t;
    if (performance.now() > sendUntil) { unsent = true; return null; }
    // arriving, it asks only for this view to be drawn again: the top-down view's ground does not need redrawing for it
    const img = image(url, ask); if (!img) return null;
    t = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, t);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
    gl.generateMipmap(gl.TEXTURE_2D);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
    textures.set(url, t);
    return t;
  }
  // THE TILES as one stack, as the game's ground draws them: each tile's picture at 1024 pixels, one layer each.
  function tiles() {
    const key = plan.tiles.map(t => t.name).join("|");
    if (tileArray && tileKey === key) return tileArray;
    const imgs = plan.tiles.map(t => image("/tiles/" + encodeURIComponent(t.name) + ".png"));
    if (!imgs.length || imgs.some(i => !i)) return null;
    const size = 1024, c = Object.assign(document.createElement("canvas"), {width: size, height: size}), g = c.getContext("2d");
    if (tileArray) gl.deleteTexture(tileArray);
    tileArray = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D_ARRAY, tileArray);
    gl.texStorage3D(gl.TEXTURE_2D_ARRAY, Math.log2(size) + 1, gl.RGBA8, size, size, imgs.length);
    gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
    imgs.forEach((img, k) => { g.clearRect(0, 0, size, size); g.drawImage(img, 0, 0, size, size); gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, 0, 0, 0, k, size, size, 1, gl.RGBA, gl.UNSIGNED_BYTE, c); });
    gl.generateMipmap(gl.TEXTURE_2D_ARRAY);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_S, gl.REPEAT); gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_T, gl.REPEAT);
    const aniso = gl.getExtension("EXT_texture_filter_anisotropic");
    if (aniso) gl.texParameterf(gl.TEXTURE_2D_ARRAY, aniso.TEXTURE_MAX_ANISOTROPY_EXT, 4);
    tileKey = key;
    return tileArray;
  }

  // ---- what stands on the ground ------------------------------------------------------------------------------------
  // Every piece and painted plant at its point, at the ground's height there (the town builder's Height), with the eight
  // pictures Unity took of it from the game camera's tilt.
  function heightAt(areas, x, z) { return levelAt(areas, x, z) * rise() + liftOf(x, z, sinkAt(x, z, shape.mounds)); }
  function standing() {
    const started = performance.now(), areas = bedsAndAreas().areas, out = [];
    for (const p of plan.pieces) {
      if (kitOf(p).role === "effect") continue;
      const views = ((pictures.kitTurned || {})[p.kind] || [])[(p.variant || 1) - 1];
      if (views) out.push({x: p.x, y: heightAt(areas, p.x, p.z), z: p.z, rot: p.rot || 0, views});
    }
    const g = plantGrid();
    for (let j = 0; j < g.nz; j++) for (let i = 0; i < g.nx; i++) {
      const v = plantCells[j * g.nx + i]; if (!v) continue;
      const look = builtLook(areas, g.x0 + (i + 0.5) * g.step, g.z0 + (j + 0.5) * g.step);
      if (look !== "grass" && look !== "meadow") continue;
      const p = plantSpot(i, j), views = (pictures.plantsTurned || {})[(plan.plants[v - 1] || {}).kind];
      if (views) out.push({x: p.x, y: heightAt(areas, p.x, p.z), z: p.z, rot: p.rot, views});
    }
    stats.items = performance.now() - started;
    return out;
  }

  // ---- bringing it up to date ---------------------------------------------------------------------------------------
  function refresh() {
    if (!pictures.ground || !pictures.camera) { hint("The 3D view needs this town's pictures: press Take pictures."); return false; }
    if (forPictures !== pictures || forPlan !== plan) {
      forPictures = pictures; forPlan = plan; builtFrom = -1; liftedAt = -1; model = null; job = null; samples = null;
      for (const t of textures.values()) gl.deleteTexture(t);
      textures.clear();
      if (!cam) cam = {x: 0, y: 0, z: 0, yaw: camera().yaw, pitch: camera().pitch, dist: 80};
    }
    curbs();
    const cc = curbCache;
    if (cc.fs && cc.v !== builtFrom) {
      // laid over several frames, about 12 ms of each; the ground laid before stays on show until the new one is done
      if (!job || jobFor !== cc.v) { job = buildGround(cc); jobFor = cc.v; }
      const until = performance.now() + 12;
      let step;
      do step = job.next(); while (!step.done && performance.now() < until);
      if (step.done) {
        const g = step.value;
        upload(progs.groundVao, g.data, g.count); upload(progs.waterVao, g.water, g.waterCount);
        stats.vertices = g.count + g.waterCount;
        samples = g.samples; builtFrom = cc.v; liftedAt = -1; job = null;
      } else setTimeout(ask, 0);
    }
    if (samples && liftedAt !== version) {
      const started = performance.now();
      fitBridges();
      // one list of heights per laid ground, refilled and sent again in place at every brush step
      const W = 1024, H = Math.ceil(samples.n / W);
      gl.bindTexture(gl.TEXTURE_2D, progs.lift);
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
      if (!samples.lift) {
        samples.lift = new Float32Array(W * H);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.R32F, W, H, 0, gl.RED, gl.FLOAT, null);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      }
      const lift = samples.lift;
      for (let r = 1; r < samples.n; r++) lift[r] = liftOf(samples.x[r], samples.z[r], samples.g[r]);
      gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, W, H, gl.RED, gl.FLOAT, lift);
      progs.liftW = W;
      items = standing();
      stats.lift = performance.now() - started;
      liftedAt = version;
    }
    return !!samples;
  }

  // ---- the camera: the game camera's tilt and field of view; drag to turn, right-drag to move, wheel to zoom ----------
  let drag = null;
  function pointerDown(e) { drag = {x: e.clientX, y: e.clientY, pan: e.button !== 0 || e.shiftKey}; cv3.setPointerCapture(e.pointerId); }
  function pointerMove(e) {
    if (!drag) return;
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y; drag.x = e.clientX; drag.y = e.clientY;
    if (drag.pan) {
      const k = cam.dist * 0.0015, s = Math.sin(cam.yaw * Math.PI / 180), c = Math.cos(cam.yaw * Math.PI / 180);
      cam.x -= (dx * c - dy * s) * k; cam.z -= (-dx * s - dy * c) * k;
    } else {
      cam.yaw += dx * 0.3;
      cam.pitch = Math.max(5, Math.min(camera().maxPitch, cam.pitch + dy * 0.2));
    }
    ask();
  }
  function basis() {
    const y = cam.yaw * Math.PI / 180, p = cam.pitch * Math.PI / 180;
    const f = [Math.cos(p) * Math.sin(y), -Math.sin(p), Math.cos(p) * Math.cos(y)], r = [Math.cos(y), 0, -Math.sin(y)];
    const u = [Math.sin(p) * Math.sin(y), Math.cos(p), Math.sin(p) * Math.cos(y)];
    const eye = [cam.x - f[0] * cam.dist, cam.y - f[1] * cam.dist, cam.z - f[2] * cam.dist];
    return {f, r, u, eye};
  }
  function viewProjection(b) {
    const fov = camera().fov * Math.PI / 180, aspect = cv3.width / cv3.height, near = Math.max(0.02, cam.dist * 0.01), far = cam.dist * 4 + 200;
    const t = 1 / Math.tan(fov / 2);
    const P = [t / aspect, 0, 0, 0, 0, t, 0, 0, 0, 0, (far + near) / (near - far), -1, 0, 0, 2 * far * near / (near - far), 0];
    const {r, u, f, eye} = b, d = v => -(v[0] * eye[0] + v[1] * eye[1] + v[2] * eye[2]);
    // camera space: x to the right, y up, looking down -z (the world's z is Unity's, so this is a mirror; nothing is culled)
    const V = [r[0], u[0], -f[0], 0, r[1], u[1], -f[1], 0, r[2], u[2], -f[2], 0, d(r), d(u), -d(f), 1];
    const M = new Float32Array(16);
    for (let c = 0; c < 4; c++) for (let row = 0; row < 4; row++) { let s = 0; for (let k = 0; k < 4; k++) s += P[k * 4 + row] * V[c * 4 + k]; M[c * 4 + row] = s; }
    return M;
  }

  // ---- drawing -------------------------------------------------------------------------------------------------------
  let asked = false;
  function ask() { if (on && !asked) { asked = true; requestAnimationFrame(render); } }
  let pictureData = new Float32Array(6 * 5 * 1024);
  function render() {
    asked = false;
    if (!on || !plan) return;
    const started = performance.now();
    sendUntil = started + 6; unsent = false;
    if (!refresh()) { gl.clearColor(0.08, 0.09, 0.1, 1); gl.clear(gl.COLOR_BUFFER_BIT); return; }
    const b = basis(), vp = viewProjection(b), light = (() => {
      // Unity's preview light (55° down, turned -30°), as the pieces were photographed
      const p = 55 * Math.PI / 180, y = -30 * Math.PI / 180; return [-Math.cos(p) * Math.sin(y), Math.sin(p), -Math.cos(p) * Math.cos(y)];
    })();
    gl.viewport(0, 0, cv3.width, cv3.height);
    gl.clearColor(0.08, 0.09, 0.1, 1); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.enable(gl.DEPTH_TEST); gl.disable(gl.CULL_FACE); gl.disable(gl.BLEND);
    const G = progs.ground, ta = tiles();
    gl.useProgram(G.p);
    gl.uniformMatrix4fv(G.u.u_vp, false, vp); gl.uniform3fv(G.u.u_eye, b.eye); gl.uniform3fv(G.u.u_light, light);
    gl.uniform1i(G.u.u_liftW, progs.liftW);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, progs.lift); gl.uniform1i(G.u.u_lift, 0);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D_ARRAY, ta); gl.uniform1i(G.u.u_tiles, 1); gl.uniform1i(G.u.u_ready, ta ? 1 : 0);
    // the curb's stones in the curb's picture (the editor's stone colour until it has arrived); the water near Unity's
    // (its render of the built town, sampled: 0.29, 0.59, 0.56 over the bed)
    const curbTex = numbers().curbTile ? texture("/tiles/" + encodeURIComponent(numbers().curbTile) + ".png") : null;
    gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, curbTex); gl.uniform1i(G.u.u_curbTex, 2); gl.uniform1i(G.u.u_curbReady, curbTex ? 1 : 0);
    gl.uniform3f(G.u.u_curb, 0.79, 0.76, 0.72); gl.uniform4f(G.u.u_water, 0.31, 0.70, 0.66, 0.85);
    gl.bindVertexArray(progs.groundVao.vao); gl.drawArrays(gl.TRIANGLES, 0, progs.groundVao.count);

    // the bridges, as their model, and everything standing, as the pictures Unity took from this tilt
    const P = progs.picture, groups = new Map();
    let n = 0;
    const need = items.length * 30 + bridges.length * (model ? model.src.tris.length * 5 : 0);
    if (pictureData.length < need) pictureData = new Float32Array(need * 1.5);
    const begin = () => n;
    if (model && pictures.bridgeModel.picture) {
      const m = model.src, from = n;
      for (const br of bridges) for (const k of m.tris) {
        const lx = m.v[k * 3] * br.s, ly = m.v[k * 3 + 1] * br.s, lz = m.v[k * 3 + 2] * br.s;
        pictureData[n++] = br.pos[0] + br.right[0] * lx + br.u[0] * lz; pictureData[n++] = br.pos[1] + ly; pictureData[n++] = br.pos[2] + br.right[1] * lx + br.u[1] * lz;
        pictureData[n++] = m.uv[k * 2]; pictureData[n++] = m.uv[k * 2 + 1];
      }
      groups.set(`/pictures/${town}/${m.picture}`, [[from, n, true]]);
    }
    for (const it of items) {
      const turn = ((Math.round((cam.yaw - it.rot) / 45) % 8) + 8) % 8, e = it.views[turn];
      if (!e) continue;
      const url = `/pictures/${town}/${e.file}`, from = begin();
      // standing up to face the camera, its own point on the ground; drawn nearer the camera by about its half-depth and
      // scaled to match, so it covers exactly what it would but the ground in front of its point does not cut it off
      const toEye = Math.hypot(b.eye[0] - it.x, b.eye[1] - it.y, b.eye[2] - it.z), k = Math.max(0.05, 1 - Math.max(e.w, e.h) * 0.5 / toEye);
      for (const [cx, cy] of [[0, 0], [1, 0], [1, 1], [0, 0], [1, 1], [0, 1]]) {
        const ox = cx * e.w - e.px, oy = cy * e.h - e.py;
        const wx = it.x + b.r[0] * ox + b.u[0] * oy, wy = it.y + b.r[1] * ox + b.u[1] * oy, wz = it.z + b.r[2] * ox + b.u[2] * oy;
        pictureData[n++] = b.eye[0] + (wx - b.eye[0]) * k; pictureData[n++] = b.eye[1] + (wy - b.eye[1]) * k; pictureData[n++] = b.eye[2] + (wz - b.eye[2]) * k;
        pictureData[n++] = cx; pictureData[n++] = cy;
      }
      (groups.get(url) || (groups.set(url, []), groups.get(url))).push([from, n, false]);
    }
    gl.useProgram(P.p);
    gl.uniformMatrix4fv(P.u.u_vp, false, vp); gl.uniform3fv(P.u.u_eye, b.eye); gl.uniform3fv(P.u.u_light, light);
    gl.bindVertexArray(progs.pictureVao); gl.bindBuffer(gl.ARRAY_BUFFER, progs.pictureBuf);
    gl.bufferData(gl.ARRAY_BUFFER, pictureData.subarray(0, n), gl.DYNAMIC_DRAW);
    gl.activeTexture(gl.TEXTURE0); gl.uniform1i(P.u.u_pic, 0);
    let shown = 0;
    for (const [url, runs] of groups) {
      const t = texture(url); if (!t) continue;
      gl.bindTexture(gl.TEXTURE_2D, t);
      for (const [from, to, lit] of runs) { gl.uniform1i(P.u.u_lit, lit ? 1 : 0); gl.drawArrays(gl.TRIANGLES, from / 5, (to - from) / 5); if (!lit) shown++; }
    }
    stats.billboards = shown;

    // the water over the stream's bed, see-through
    gl.useProgram(G.p); gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA); gl.depthMask(false);
    gl.bindVertexArray(progs.waterVao.vao); gl.drawArrays(gl.TRIANGLES, 0, progs.waterVao.count);
    gl.depthMask(true); gl.disable(gl.BLEND); gl.bindVertexArray(null);
    stats.render = performance.now() - started; stats.frames++;
    if (unsent) ask();
  }

  function size() {
    if (!on) return;
    const r = cv3.getBoundingClientRect(), d = window.devicePixelRatio || 1;
    cv3.width = Math.max(1, Math.round(r.width * d)); cv3.height = Math.max(1, Math.round(r.height * d));
    ask();
  }
  function toggle() {
    on = !on;
    $("stage").classList.toggle("split", on); $("cv3").hidden = !on; $("view3d").classList.toggle("on", on);
    if (on && !gl) {
      try { setup(); } catch (err) { on = false; $("stage").classList.remove("split"); $("cv3").hidden = true; alert("The 3D view cannot start: " + err.message); }
    }
    resize(); size();
  }
  addEventListener("resize", size);
  return {toggle, changed: ask, stats, get camera() { return cam; }};
})();
