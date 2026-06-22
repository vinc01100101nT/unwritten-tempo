// HEAVY weather: screen-space WET GROUND. Like CloudShadow, a camera-locked full-screen quad reads the scene
// DEPTH texture, reconstructs each pixel's WORLD position, and re-shades wet surfaces — darkening them and
// adding a glossy fresnel "reflection" with animated ripples. Puddles are a world-XZ noise mask confined to
// near-flat surfaces (normal derived from depth via ddx/ddy), so walls/roofs stay dry-ish and only the ground
// pools water. NOT true SSR (no ray-march): the reflection is a faked sky-tint fresnel + ripple shimmer, which
// reads as "wet" and suits the stylized art far better than ray-traced reflections.
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
        _GroundImpact ("Ground Impact (same rings off puddles)", Range(0,4)) = 0.15
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

            float _Wetness, _RainNow, _Darken, _PuddleScale, _PuddleAmount;
            float _RippleSpeed, _RippleStrength, _SpecStrength, _Flatness;
            float _RipScale, _RipSpeed, _WaterImpact, _StepRadius, _StepBright, _GroundImpact;
            float _ReflStrength, _ReflDistort, _ReflFlipY;
            float4 _ReflectColor;
            float4 _Steps[16];   // footstep ripples: xy = world XZ, z = age 0..1 (>=1 = dead slot)

            TEXTURE2D(_ReflectionTex);   // global planar-reflection texture (black when reflections are off)
            SAMPLER(sampler_ReflectionTex);

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

            float fbm(float2 p)
            {
                float v = 0.0, amp = 0.5;
                for (int o = 0; o < 4; o++) { v += vnoise(p) * amp; p *= 2.0; amp *= 0.5; }
                return v;
            }

            // One field of scattered rain CROWN-SPLASHES. This function owns only the SCATTER (which cells splash,
            // and each splash's random centre / phase / size); the splash SHAPE itself is SplashShape (Splash.hlsl).
            float rainRipple(float2 p, float seed)
            {
                float2 cell = floor(p);
                float2 f    = frac(p);
                float  ex   = step(0.45, hash21(cell + float2(seed + 9.1, seed)));          // ~55% of cells splash
                float  phase = _Time.y * _RipSpeed * 0.30 + hash21(cell + float2(seed + 1.3, seed + 6.0));
                float  age  = frac(phase);
                float  k    = floor(phase);   // life-cycle index: which impact this cell is on (bumps each loop)
                float  maxR = lerp(0.173, 0.367, hash21(cell + float2(seed + 5.5, seed + 3.0))); // splash size

                // Per-IMPACT seed = cell + cycle index, so every loop lands the splash at a NEW random spot inside
                // the cell (wander 0.2 keeps the old middle-of-cell placement range). SplashShape does the rest.
                float2 iseed = cell + seed + float2(31.7, 13.3) * k;
                return SplashShape(f - 0.5, age, maxR, 0.13, iseed, 0.20) * ex;
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

                // Puddle mask: low spots of world-XZ noise, only on flat ground. _PuddleAmount IS the live coverage.
                float pn = fbm(wp.xz * _PuddleScale);
                float puddle = smoothstep(1.0 - _PuddleAmount, 1.0 - _PuddleAmount + 0.18, pn);
                float wet = saturate(puddle * flat) * _Wetness;

                float groundWet = flat * _RainNow;

                // Nothing to draw: not flat ground, or it's neither raining nor any standing water left.
                if (groundWet <= 0.001 && wet <= 0.001) return half4(0, 0, 0, 0);

                // Rain-impact rings: ONE pattern (two overlapping fields with random centre/size/phase/gaps so it
                // doesn't read as a grid), drawn on EVERY flat wet pixel. Its brightness is the bigger of the two
                // independent per-surface dials — _WaterImpact where there's standing water (puddle), _GroundImpact
                // on the open wet ground — so "rings on water" and "rings on ground" tune separately.
                // Polish 2026-06-21: grid frequency ×2 per axis = ×4 as many ripples (denser stipple).
                float ambient = rainRipple(wp.xz * _RipScale * 2.0,        0.0)
                              + rainRipple(wp.xz * _RipScale * 3.4 + 11.3, 31.0);
                // Both ring brightnesses scale by the LIVE rain (_RainNow): groundWet already carries it, and the
                // on-puddle rings get it explicitly — so raindrops stop dimpling the puddles when the rain stops,
                // even though the standing water (reflection/fresnel below) lingers.
                float3 spec = _ReflectColor.rgb * (ambient * max(wet * _WaterImpact * _RainNow, groundWet * _GroundImpact));

                // Everything below is PUDDLE-only (standing-water shine), branched out so the vast majority of
                // ground pixels (no puddle) skip the fresnel, footstep loop and reflection sample — that's what
                // keeps Option A ~free: off-puddle ground pays only for the cheap ambient rings above.
                if (wet > 0.001)
                {
                    // Animated ripples: perturb the normal by the gradient of scrolling noise → shimmering fresnel.
                    float t = _Time.y * _RippleSpeed;
                    float2 rc = wp.xz * 2.0 + t;
                    float e = 0.06;
                    float hx = vnoise(rc + float2(e, 0)) - vnoise(rc - float2(e, 0));
                    float hz = vnoise(rc + float2(0, e)) - vnoise(rc - float2(0, e));
                    N = normalize(N + float3(-hx, 0.0, -hz) * _RippleStrength);

                    // Faked reflection: a glancing-angle fresnel tinted by the sky/reflection colour.
                    float3 V = normalize(_WorldSpaceCameraPos - wp);
                    float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                    spec += _ReflectColor.rgb * (fres * _SpecStrength) * wet;

                    // Footsteps: an expanding ring at each recent step position (dead slots have age >= 1). Kept
                    // puddle-masked so a footstep ripple is invisible off a puddle — that's how IsPuddleAt answers.
                    float steps = 0.0;
                    for (int i = 0; i < 16; i++)
                    {
                        float4 s   = _Steps[i];
                        float  age = s.z;
                        float  scl = max(s.w, 0.0001);    // per-ripple scale: 1 = footstep, larger = landing splash
                        float  d   = distance(wp.xz, s.xy);
                        float  ring = smoothstep(0.03 * scl, 0.0, abs(d - age * _StepRadius * scl)) * saturate(1.0 - age);
                        steps += ring * _StepBright * scl * step(age, 1.0);
                    }
                    spec += _ReflectColor.rgb * steps * wet;

                    // Planar reflection: sample the mirror RT at this screen pixel, ripple-distorted, blended by
                    // wetness. Contributes nothing when reflections are off (texture is bound to black).
                    float2 ruv = saturate(uv + N.xz * _ReflDistort);
                    ruv.y = lerp(ruv.y, 1.0 - ruv.y, _ReflFlipY);
                    float3 refl = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, ruv).rgb;
                    spec += refl * (wet * _ReflStrength);
                }

                return half4(spec, _Darken * wet);   // premultiplied: add spec, darken puddles by _Darken*wet
            }
            ENDHLSL
        }
    }
    Fallback Off
}
