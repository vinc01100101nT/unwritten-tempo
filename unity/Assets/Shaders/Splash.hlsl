#ifndef UNWRITTEN_SPLASH_INCLUDED
#define UNWRITTEN_SPLASH_INCLUDED

// The rain "crown-splash" SHAPE primitive — an expanding crown ring + central pop + droplets, as a function of
// life. Kept in its own include so the shape is decoupled from where it's drawn; currently used by
// Unwritten/WetGround for both the water-impact (puddle) and ground-impact rain rings (rainRipple).

// Small hash → [0,1], used only to scatter the droplet directions/lengths per splash.
float SplashHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// The procedural splash as a function of LIFE: an expanding + thinning crown ring, a brief central pop, and a
// few droplets flicking outward — all fading out over the splash's life. Everything is in "splash space": `local`
// is the sample point with the splash's NOMINAL centre at the origin, and maxR is the outer radius in that same
// space (pass maxR = 1 for a unit-quad billboard splash, or a per-cell radius for the scattered ground field).
//
// EACH IMPACT lands at a different random spot: the actual centre wanders up to ±`wander` (in `local` units) from
// the origin, seeded by `iseed`. Feed a per-impact seed (e.g. cell + life-cycle index) and every new impact hits a
// fresh position; pass wander = 0 to pin it to the origin (e.g. a billboard whose position is already randomized
// on the CPU). `iseed` also seeds the droplet directions, so the whole splash is unique per impact.
//   local  : sample position with the splash's nominal centre at the origin
//   age    : 0..1 life (0 = the instant of impact, 1 = fully faded)
//   maxR   : outer radius the crown ring reaches at age 1
//   thick  : crown-ring thickness at impact (scaled by maxR + thinned over life internally)
//   iseed  : per-impact seed → randomizes BOTH the impact centre and the droplets
//   wander : how far (in `local` units) the impact centre may stray from the origin (0 = pinned)
// Returns an unweighted intensity (ring + core + droplets), already faded by (1-age). The caller multiplies in
// colour / alpha / per-surface strength.
// Cheap variant: just the expanding crown ring + central pop, NO droplet loop and NO internal wander (the caller
// passes `local` already relative to the impact's chosen centre). Built for the scattered ground field, where many
// neighbouring impacts are summed per pixel (a 3×3 cell sweep) and the 5-droplet loop ×N would be too costly — and
// the tiny droplets aren't readable at ground-impact scale anyway. Same ring/core maths as SplashShape.
float SplashRing(float2 local, float age, float maxR, float thick)
{
    float r  = length(local);
    float rr = lerp(0.08, 1.0, age) * maxR;
    float th = thick * (1.0 - 0.55 * age) * maxR;
    float ring = smoothstep(th, 0.0, abs(r - rr));
    float core = smoothstep(0.22 * maxR, 0.0, r) * saturate(1.0 - age * 3.0);
    return (ring + core) * (1.0 - age);
}

float SplashShape(float2 local, float age, float maxR, float thick, float2 iseed, float wander)
{
    // Pick THIS impact's centre — a random offset from the origin, fresh whenever iseed changes.
    float2 ctr = (float2(SplashHash(iseed + 17.3), SplashHash(iseed + 41.7)) - 0.5) * (2.0 * wander);
    float2 q   = local - ctr;
    float  r   = length(q);

    // Expanding crown ring: grows out from the impact point, thinning + fading as it spreads.
    float rr    = lerp(0.08, 1.0, age) * maxR;
    float th    = thick * (1.0 - 0.55 * age) * maxR;
    float ring  = smoothstep(th, 0.0, abs(r - rr));

    // Bright central pop at the instant of impact, gone almost immediately.
    float core  = smoothstep(0.22 * maxR, 0.0, r) * saturate(1.0 - age * 3.0);

    // A handful of crown droplets flicking outward (seeded per splash so each looks different).
    float drops = 0.0;
    [unroll]
    for (int i = 0; i < 5; i++)
    {
        float  ang = (float(i) + SplashHash(iseed + float2(float(i) * 1.7, 0.0))) * 1.2566370614; // ~2pi/5 + jitter
        float2 dir = float2(cos(ang), sin(ang));
        float  dr  = lerp(0.12, 0.92, age) * maxR * (0.6 + 0.4 * SplashHash(iseed + float2(float(i) * 3.1, 1.0)));
        drops += smoothstep(0.13 * maxR, 0.0, distance(q, dir * dr));
    }
    drops *= saturate(1.0 - age) * 0.5;

    return (ring + core + drops) * (1.0 - age);
}

#endif // UNWRITTEN_SPLASH_INCLUDED
