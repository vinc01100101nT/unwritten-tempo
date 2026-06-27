// Screen-space SNOW ACCUMULATION. Twin of WetGround: a camera-locked full-screen quad reads the scene DEPTH
// texture, reconstructs each pixel's WORLD position + surface NORMAL (via ddx/ddy), and whitens UP-FACING
// surfaces — so roofs, the ground, fences and tree-tops gather snow while walls/trunks stay bare, all from the
// one reconstructed normal (no per-material snow shader needed). _UpCutoff is gentler than WetGround's puddle
// flatness so SLOPED roofs still catch snow. Coverage is broken up by a world-XZ noise patch so edges read
// organic.
//
// FOOTPRINTS: the same _Steps[24] ring buffer WetGround uses for ripples, but here each step is a PERSISTENT
// filled depression that slowly heals (age 0->1 over many seconds in C#). A print CARVES the snow (lowers
// coverage to reveal the trodden ground) and drops a cool shadow in the dent — that's the trail in the snow.
//
// Standard alpha blend (SrcAlpha OneMinusSrcAlpha) returning half4(snow.rgb, coverage) = lerp(scene, snow,
// coverage). Drawn before the billboards (Queue Transparent-100) so characters stay unshaded, like WetGround.
Shader "Unwritten/SnowGround"
{
    Properties
    {
        _Accum      ("Accumulation", Range(0,1)) = 0
        _SnowColor  ("Snow Color", Color) = (0.95, 0.97, 1.0, 1)
        _LightTint  ("Day/Night Light Tint", Color) = (1, 1, 1, 1)
        _ShadowTint ("Crevice Shadow Tint", Color) = (0.62, 0.70, 0.85, 1)
        _UpCutoff   ("Up-Facing Cutoff", Range(0,1)) = 0.35
        _Coverage   ("Coverage", Range(0,1)) = 0.7
        _PatchScale ("Patch Scale (1/size)", Float) = 0.25
        _Thickness  ("Fake Volume", Range(0,1)) = 0.6
        _Lumpiness  ("Clumpiness", Range(0,1)) = 0.4
        _FootRadius ("Footprint Radius", Float) = 0.28
        _FootCarve  ("Footprint Carve", Range(0,1)) = 0.85
        _FootDark   ("Footprint Shadow", Range(0,1)) = 0.4
        _ShadowStrength ("Cast Shadow Strength", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha   // lerp(scene, snow, coverage)
            ZWrite Off
            ZTest Always                       // full-screen; per-pixel occlusion comes from sampled depth
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Cast-shadow keywords: lets MainLightShadow sample the sun's shadow map so unit (BillboardShadow)
            // shadows show on the snow. Stripped to a no-op variant when shadows are off (and _ShadowStrength gates
            // it to 0 on low tiers in C#, so min-spec pays nothing).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _Accum, _UpCutoff, _Coverage, _PatchScale;
            float _Thickness, _Lumpiness;
            float _FootRadius, _FootCarve, _FootDark;
            float _ShadowStrength;
            float4 _SnowColor, _ShadowTint, _LightTint;
            float4 _Steps[24];   // footprints: xy = world XZ, z = age 0..1 (>=1 = dead slot), w = size scale. [0,16) player, [16,24) NPCs

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

                // Up-facing weight: 1 on flat tops, fading to 0 on walls. Gentler cutoff than puddles so sloped
                // roofs still gather snow. Squared so the rolloff onto vertical faces is soft, not a hard line.
                float up = saturate((N.y - _UpCutoff) / max(0.001, 1.0 - _UpCutoff));
                up *= up;

                // Organic coverage that FILLS as snow accumulates: world-XZ noise patches whose THRESHOLD drops as
                // the master ramp (_Accum) rises, so snow appears in spots and SPREADS to cover the surface — the
                // village fills in — instead of the whole blanket fading up in opacity. Covered area stays fully
                // opaque (alpha = cov, not scaled by _Accum). target = fraction of up-facing area covered at full.
                float pn = fbm(wp.xz * _PatchScale);                         // ~[0, 0.94]
                float target = saturate(_Coverage * _Accum);                 // 0 (snow starting) → _Coverage (landed)
                float thr = lerp(1.15, -0.05, target);                       // above-all noise (none) → below-all (full)
                float patch = smoothstep(thr - 0.12, thr + 0.12, pn);        // pn OVER threshold → snow here; as target
                                                                             // rises thr drops so more area passes (fills)
                float cov = saturate(up * patch);
                if (cov <= 0.001) return half4(0, 0, 0, 0);
                cov = smoothstep(0.0, 0.4, cov);   // make covered snow OPAQUE — roofs read pure white, not see-through

                // Footprints: persistent filled depressions that heal with age. Each carves coverage (revealing
                // the trodden ground) and drops a cool shadow in the dent. Only the freshest steps exist.
                float carve = 0.0, dent = 0.0;
                for (int i = 0; i < 24; i++)
                {
                    float4 s = _Steps[i];
                    if (s.z >= 1.0) continue;                            // dead slot
                    float scl = max(s.w, 0.0001);
                    float d   = distance(wp.xz, s.xy);
                    float disc = smoothstep(_FootRadius * scl, _FootRadius * scl * 0.4, d);
                    float heal = 1.0 - s.z;                              // fades as the print fills back in
                    carve = max(carve, disc * heal);
                    dent  = max(dent,  smoothstep(_FootRadius * scl * 0.85, 0.0, d) * heal);
                }
                cov = saturate(cov - carve * _FootCarve * cov);

                // Snow colour: bright top, cooled in crevices (low fbm) and in footprint dents.
                float shade = saturate(fbm(wp.xz * _PatchScale * 3.0) * 0.4 + 0.78);   // brighter floor = whiter snow
                float3 snow = lerp(_ShadowTint.rgb, _SnowColor.rgb, shade);
                snow = lerp(snow, _ShadowTint.rgb, dent * _FootDark);

                // ---- Fake VOLUME (cotton thickness without geometry) -----------------------------------------
                // This pass can only re-shade existing surface pixels, so it can't bulge past a silhouette — but
                // it can read as a deep, rounded blanket: (1) the thick interior domes toward bright white, (2) a
                // soft cool shadow "lip" sits where the snow thins out to bare ground (fake AO of a raised edge),
                // and (3) mid-frequency lumps break the surface into clumps so it looks piled, not painted.
                float thick = saturate(up * cov);                                  // how deep the snow reads here
                float lump  = fbm(wp.xz * _PatchScale * 6.0 + 17.0);               // cotton clumps
                snow  = lerp(snow, _SnowColor.rgb + 0.06, thick * _Thickness);     // dome: lift the deep interior
                snow *= lerp(1.0, 0.80 + lump * 0.45, _Lumpiness * (1.0 - carve)); // clumpy shading
                float rim = smoothstep(0.02, 0.4, cov) * (1.0 - smoothstep(0.4, 0.85, cov)); // edge band
                snow  = lerp(snow, _ShadowTint.rgb, rim * _Thickness * 0.45);      // cool shadow lip at the rim
                cov   = saturate(cov + thick * _Thickness * 0.2);                  // thick areas read more solid

                // Day/night: this depth pass overwrites the (already-lit, dimmed-at-night) scene with snow, so it
                // would glow white in the dark unless we dim it ourselves. Multiply by the time-of-day tint
                // (white at noon = no change, dim cool blue at midnight) so the blanket respects the night.
                // Cast shadows from world units: BillboardShadow proxies feed the sun's shadow map, so sample it at
                // this pixel's reconstructed world pos and cool/dim the snow where the sun is occluded — the player
                // and mobs get a real directional contact shadow ON the blanket. _ShadowStrength gates it (set to 0
                // on low tiers in C# → free); the keyword variant also compiles out entirely when shadows are off.
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                if (_ShadowStrength > 0.001)
                {
                    float atten = MainLightRealtimeShadow(TransformWorldToShadowCoord(wp));   // 1 = lit, 0 = shadowed
                    snow = lerp(snow, _ShadowTint.rgb * 0.7, (1.0 - atten) * _ShadowStrength);
                }
                #endif

                snow *= _LightTint.rgb;

                return half4(snow, cov);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
