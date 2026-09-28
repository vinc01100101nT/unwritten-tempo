// HEAVY weather: screen-space WET GROUND. Like CloudShadow, a camera-locked full-screen quad reads the scene
// DEPTH texture, reconstructs each pixel's WORLD position, and re-shades wet surfaces — darkening them and
// adding a glossy fresnel shine with animated ripples. Standing water is a set of BODIES (WaterSurfaces), each marked
// on the squares of the map it covers and each with a flat surface: a thin SHEET over each level of flat ground, where
// a world-XZ noise mask picks the puddles (raised where rain runs off rising ground nearby), and a LAKE in each hollow,
// water all over, rising as the rain goes on. A pixel is water when its ground lies between its body's floor and
// surface, so every shoreline is where the ground itself rises out of the water.
// On High tier each water height on screen has its own planar mirror (PlanarReflection), and a puddle samples the
// mirror at ITS OWN height, which is how still water reflects. Only water near the middle of the view reflects the
// world; farther out it fades to reflecting the sky alone (a sky-only mirror, the same at every height), and so does a
// water height with no mirror left, so no water ever blinks out.
//
// Single pass with premultiplied blend (One OneMinusSrcAlpha): result = spec + dst*(1 - darken). That lets one
// pass BOTH darken the surface and add a highlight, with dry pixels (mask 0) left exactly as the camera drew
// them. Drawn before the billboards (Queue Transparent-100) so characters stay unshaded, matching CloudShadow.
Shader "Unwritten/WetGround"
{
    Properties
    {
        _Wetness      ("Wetness", Range(0,1)) = 1
        _RainNow      ("Rain Now (live rain, drives impact rings)", Range(0,1)) = 0
        _Darken       ("Darken", Range(0,1)) = 0.35
        _PuddleScale  ("Puddle Scale (1/size)", Float) = 0.12
        _PuddleAmount ("Puddle Amount", Range(0,1)) = 0.55
        _RippleSpeed  ("Ripple Speed", Float) = 1.0
        _RippleStrength ("Ripple Strength", Range(0,1)) = 0.35
        _ReflectColor ("Reflection Color", Color) = (0.60, 0.72, 0.95, 1)
        _SpecStrength ("Spec Strength", Range(0,4)) = 1.4
        _Flatness     ("Flatness Cutoff", Range(0,1)) = 0.6
        _RipScale     ("Ambient Ripple Scale", Float) = 0.5
        _RipSpeed     ("Ambient Ripple Speed", Float) = 1.0
        _WaterImpact  ("Water Impact (rings on puddles)", Range(0,4)) = 0.44
        _GroundImpact ("Ground Impact (same rings off puddles)", Range(0,4)) = 1.14
        _GroundImpactStart ("Ground Impact rain onset (rings start at this rain intensity)", Range(0,1)) = 0.5
        _StepRadius   ("Step Ripple Radius", Float) = 0.6
        _StepBright   ("Step Ripple Strength", Range(0,8)) = 2.5
        _ReflStrength ("Reflection Strength", Range(0,2)) = 0.0
        _ReflDistort  ("Reflection Distortion", Range(0,0.3)) = 0.04
        _ReflFlipY    ("Reflection Flip Y", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha   // premultiplied: dst*(1-a) + rgb  →  darken AND add spec in one pass
            ZWrite Off
            ZTest Always                 // full-screen; per-pixel occlusion comes from sampled depth
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Splash.hlsl"   // rain crown-splash SHAPE primitive (SplashShape)
            #include "WaterBodies.hlsl"   // the map's bodies of water, shared with the ink outlines

            float _Wetness, _RainNow, _Darken, _PuddleScale, _PuddleAmount;
            float _RippleSpeed, _RippleStrength, _SpecStrength, _Flatness;
            float _RipScale, _RipSpeed, _WaterImpact, _StepRadius, _StepBright, _GroundImpact, _GroundImpactStart;
            float _ReflStrength, _ReflDistort, _ReflFlipY;
            float4 _ReflectColor;
            float4 _Steps[24];   // footstep ripples: xy = world XZ, z = age 0..1 (>=1 = dead slot). [0,16) player, [16,24) NPCs

            TEXTURE2D(_WaterRunoffTex);
            SAMPLER(sampler_WaterRunoffTex);

            TEXTURE2D(_ReflectionTex0); TEXTURE2D(_ReflectionTex1); TEXTURE2D(_ReflectionTex2); TEXTURE2D(_ReflectionTex3);
            TEXTURE2D(_ReflectionTex4); TEXTURE2D(_ReflectionTex5); TEXTURE2D(_ReflectionTex6); TEXTURE2D(_ReflectionTex7);
            SAMPLER(sampler_ReflectionTex0);
            // The sky alone, reflected (PlanarReflection's sky mirror); xy = the middle of the view on the ground (world XZ),
            // z = how far from it the world is still fully reflected, w = how far until only the sky is.
            TEXTURE2D(_ReflectionSky);
            float4 _ReflectNear;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = float4(IN.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value noise in [0,1].
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float3 SampleMirror(int slot, float2 uv)
            {
                float3 c = float3(0, 0, 0);
                if      (slot == 0) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex0, sampler_ReflectionTex0, uv, 0).rgb;
                else if (slot == 1) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex1, sampler_ReflectionTex0, uv, 0).rgb;
                else if (slot == 2) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex2, sampler_ReflectionTex0, uv, 0).rgb;
                else if (slot == 3) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex3, sampler_ReflectionTex0, uv, 0).rgb;
                else if (slot == 4) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex4, sampler_ReflectionTex0, uv, 0).rgb;
                else if (slot == 5) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex5, sampler_ReflectionTex0, uv, 0).rgb;
                else if (slot == 6) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex6, sampler_ReflectionTex0, uv, 0).rgb;
                else if (slot == 7) c = SAMPLE_TEXTURE2D_LOD(_ReflectionTex7, sampler_ReflectionTex0, uv, 0).rgb;
                return c;
            }

            float fbm(float2 p)
            {
                float v = 0.0, amp = 0.5;
                for (int o = 0; o < 4; o++) { v += vnoise(p) * amp; p *= 2.0; amp *= 0.5; }
                return v;
            }

            // A field of scattered rain CROWN-SPLASHES with NO visible grid. Each cell hosts at most one live impact,
            // but the impact lands at a FULLY RANDOM spot anywhere in its cell — and we sum the 3×3 neighbourhood, so a
            // splash near a cell edge bleeds correctly into the next cell instead of being clipped at the border. That
            // (plus re-rolling the position every life-cycle) dissolves the lattice into random rainfall.
            float rainRipple(float2 p, float seed)
            {
                float2 ip = floor(p);
                float  sum = 0.0;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                  [unroll]
                  for (int x = -1; x <= 1; x++)
                  {
                    float2 cell  = ip + float2(x, y);
                    // Phase runs the cell's impact life-cycle; k = which impact (bumps each loop) seeds a NEW position.
                    float  phase = _Time.y * _RipSpeed * 0.30 + hash21(cell + float2(seed + 1.3, seed + 6.0));
                    float  age   = frac(phase);
                    float  k     = floor(phase);

                    // Occupancy depends on the cell AND the cycle index, so there are no PERMANENT empty cells (which
                    // would themselves read as a grid of holes) — which cells are raining shuffles every cycle.
                    float  ex    = step(0.42, hash21(cell + float2(k * 2.7 + seed + 9.1, k * 1.3 + seed)));
                    if (ex <= 0.0) continue;

                    float  maxR  = lerp(0.173, 0.367, hash21(cell + float2(seed + 5.5, seed + 3.0)));
                    // Impact centre: anywhere in [0,1)² of the cell, re-rolled each cycle (k). No clamp/wander limit.
                    float2 rc    = float2(hash21(cell + float2(k + 17.3, seed)),
                                          hash21(cell + float2(k + 41.7, seed + 4.0)));
                    float2 centre = cell + rc;
                    sum += SplashRing(p - centre, age, maxR, 0.13);
                  }
                }
                return sum;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);
                float depth = SampleSceneDepth(uv);
                if (depth == UNITY_RAW_FAR_CLIP_VALUE) return half4(0, 0, 0, 0);   // sky: leave untouched

                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                // Surface normal reconstructed from the depth-derived world position.
                float3 dx = ddx(wp), dz = ddy(wp);
                float3 N  = normalize(cross(dz, dx));
                if (N.y < 0.0) N = -N;                                   // face up
                float flatW = saturate((N.y - _Flatness) / max(0.001, 1.0 - _Flatness));

                // Wet-ground masks — TWO tiers on flat ground, driven by DIFFERENT sources so puddles outlive rain:
                //   • groundWet — ALL flat ground, follows the LIVE rain (_RainNow). Gets ONLY the faint rain-impact
                //                 rings, so rain visibly lands on the whole ground while it's falling — and STOPS the
                //                 moment the rain does (Option A, 2026-06-21).
                //   • wet (puddle) — the shiny standing-water mask, driven by _PuddleAmount which the C# spreads in /
                //                 recedes out on its OWN clock. Gets reflections, fresnel spec, footstep splashes and
                //                 darkening — the full puddle look, PERSISTING after the rain has stopped.
                float flat = saturate(flatW);

                // Standing water: in a body of water, and — while mirrors run — only where that surface has a mirror, so
                // no puddle ever shows another surface's reflection. A lake is water all over; a sheet holds the puddle
                // pattern below.
                int water = BodyIndex(wp);
                bool inLake = water >= 0 && IsLake(water);
                int mirror = water >= 0 ? MirrorSlot(_WaterLevels[water]) : -1;
                bool holdsWater = BodyShows(water);
                // How much of what lies under a lake still shows through it: less the deeper it is.
                float shows = holdsWater ? UnderWaterShows(wp, water) : 1.0;

                // Puddle mask: world-XZ noise, raised where rain runs off rising ground nearby (WaterPooling.Runoff).
                // _PuddleAmount IS the live coverage.
                float runoff = SAMPLE_TEXTURE2D_LOD(_WaterRunoffTex, sampler_WaterRunoffTex,
                                                    (wp.xz - _WaterRunoffRect.xy) * _WaterRunoffRect.zw, 0).r;
                float pn = fbm(wp.xz * _PuddleScale) + runoff * _WaterRunoffPull;
                float puddle = smoothstep(1.0 - _PuddleAmount, 1.0 - _PuddleAmount + 0.18, pn);
                float wet = holdsWater ? (inLake ? 1.0 : saturate(puddle)) * _Wetness : 0.0;

                // Open-ground rain impact has a rain-intensity ONSET: nothing in a drizzle, easing in above
                // _GroundImpactStart and reaching full at heavy rain. (Puddle rings below still use raw _RainNow, so
                // standing water dimples from any rain.) Below the onset on non-puddle ground this is 0 → early-out.
                float groundRain = smoothstep(_GroundImpactStart, 1.0, _RainNow);
                float groundWet  = flat * groundRain;

                // Nothing to draw: not flat ground, or it's neither raining nor any standing water left.
                if (groundWet <= 0.001 && wet <= 0.001) return half4(0, 0, 0, 0);

                // Rain-impact rings: a single scattered field of crown-splashes (random centre/size/phase, summed over
                // a 3×3 cell sweep so it never reads as a grid), drawn on EVERY flat wet pixel. Its brightness is the
                // bigger of the two independent per-surface dials — _WaterImpact where there's standing water (puddle),
                // _GroundImpact on the open wet ground — so "rings on water" and "rings on ground" tune separately.
                float ambient = rainRipple(wp.xz * _RipScale * 2.4, 0.0);
                // Both ring brightnesses scale by the LIVE rain (_RainNow): groundWet already carries it, and the
                // on-puddle rings get it explicitly — so raindrops stop dimpling the puddles when the rain stops,
                // even though the standing water (reflection/fresnel below) lingers.
                float3 spec = _ReflectColor.rgb * (ambient * max(wet * _WaterImpact * _RainNow, groundWet * _GroundImpact));

                // Everything below is PUDDLE-only (standing-water shine), branched out so the vast majority of
                // ground pixels (no puddle) skip the fresnel, footstep loop and reflection sample — that's what
                // keeps Option A ~free: off-puddle ground pays only for the cheap ambient rings above.
                if (wet > 0.001)
                {
                    // The water's surface is flat whatever stands in it: start from straight up, never from the surface
                    // under the water. A leg, a body or a creature's picture below the surface faces sideways, and its
                    // tilt threw the reflection sideways and lit a white fresnel band across it (2026-09-28).
                    // Animated ripples: perturb the normal by the gradient of scrolling noise → shimmering fresnel.
                    float t = _Time.y * _RippleSpeed;
                    float2 rc = wp.xz * 2.0 + t;
                    float e = 0.06;
                    float hx = vnoise(rc + float2(e, 0)) - vnoise(rc - float2(e, 0));
                    float hz = vnoise(rc + float2(0, e)) - vnoise(rc - float2(0, e));
                    N = normalize(float3(0.0, 1.0, 0.0) + float3(-hx, 0.0, -hz) * _RippleStrength);

                    // Faked reflection: a glancing-angle fresnel tinted by the sky/reflection colour.
                    float3 V = normalize(_WorldSpaceCameraPos - wp);
                    float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                    spec += _ReflectColor.rgb * (fres * _SpecStrength) * wet;

                    // Footsteps: an expanding ring at each recent step position (dead slots have age >= 1). Kept
                    // puddle-masked so a footstep ripple is invisible off a puddle — that's how IsPuddleAt answers.
                    float steps = 0.0;
                    for (int i = 0; i < 24; i++)
                    {
                        float4 s   = _Steps[i];
                        float  age = s.z;
                        float  scl = max(s.w, 0.0001);    // per-ripple scale: 1 = footstep, larger = landing splash
                        float  d   = distance(wp.xz, s.xy);
                        float  ring = smoothstep(0.03 * scl, 0.0, abs(d - age * _StepRadius * scl)) * saturate(1.0 - age);
                        steps += ring * _StepBright * scl * step(age, 1.0);
                    }
                    spec += _ReflectColor.rgb * steps * wet;

                    // Planar reflection: sample THIS water height's mirror at this screen pixel, ripple-distorted,
                    // blended by wetness — near the middle of the view; farther out it fades to the sky alone, which is
                    // also all a height with no mirror shows. Contributes nothing when no mirror runs. Water reflects
                    // more the lower the view skims it (Fresnel): at the game's usual steep view this is _ReflStrength,
                    // rising to a full mirror as the view flattens toward the surface.
                    float2 ruv = saturate(uv + N.xz * _ReflDistort);
                    ruv.y = lerp(ruv.y, 1.0 - ruv.y, _ReflFlipY);
                    float3 sky = SAMPLE_TEXTURE2D_LOD(_ReflectionSky, sampler_ReflectionTex0, ruv, 0).rgb;
                    float nearView = 1.0 - smoothstep(_ReflectNear.z, max(_ReflectNear.w, _ReflectNear.z + 0.001), distance(wp.xz, _ReflectNear.xy));
                    float3 refl = mirror >= 0 ? lerp(sky, SampleMirror(mirror, ruv), nearView) : sky;
                    float skim = pow(1.0 - saturate(V.y), 5.0);
                    spec += refl * (wet * lerp(_ReflStrength, 1.0, skim));
                }

                // Premultiplied: what was drawn is kept at (1 - alpha). A sheet darkens the wet ground under it by _Darken;
                // a lake also swallows what lies under it the deeper it is (shows → 0), leaving only its surface.
                float keep = (1.0 - _Darken) * shows;
                return half4(spec, wet * (1.0 - keep));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
