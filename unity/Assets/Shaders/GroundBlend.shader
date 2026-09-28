// Opaque, lit GROUND shader that layers up to FIVE ground textures by VERTEX COLOR — the exact trick the Pxltgr
// "Fantasy Landscape" FBX uses to make grass melt into soil (its ground meshes carry white RGB + a 0..1 alpha
// ring; the GPU interpolates that across each triangle into a soft gradient). ALL FOUR vertex-colour channels
// drive it, baked per-vertex by PxltgrGroundBlend (the procedural recipe leaves g/b at 0):
//   • vertex .a  → grass↔dirt blend (organic noise blobs)      base   = lerp(grass, dirt, a)
//   • vertex .r  → PATH/road weight (feathered)                albedo = lerp(base,  path,  r)
//   • vertex .g  → STONE-path weight                           albedo = lerp(albedo, stone, g)
//   • vertex .b  → ROCK-path weight                            albedo = lerp(albedo, rock,  b)
// So each path variant is its OWN texture (road/stone/rock) feathered on top of the grass+dirt base — distinct yet
// seamless, no hard tile seams and no transparency overdraw (it stays Opaque — cheap, see PERFORMANCE.md). The Map
// Painter importer paints one channel per ground brush (path→r, stone→g, rock→b, dirt→a) so a single map can mix
// all of them. Textures are sampled by WORLD-XZ so tiling is seamless regardless of mesh size.
//
// Writes depth + casts/receives shadows like URP/Lit, so the screen-space weather passes (CloudShadow,
// WetGround, SnowGround — all read the depth texture) layer on top of it exactly as they do the tiled ground.
Shader "Unwritten/GroundBlend"
{
    Properties
    {
        _GrassMap    ("Grass Map", 2D) = "white" {}
        _DirtMap     ("Dirt Map",  2D) = "white" {}
        _PathMap     ("Path Map (road, .r)",  2D) = "white" {}
        _StoneMap    ("Stone Map (.g)", 2D) = "white" {}
        _RockMap     ("Rock Map (.b)",  2D) = "white" {}
        _GrassTiling ("Grass Tiling (tiles/world-unit)", Float) = 0.4
        _DirtTiling  ("Dirt Tiling (tiles/world-unit)",  Float) = 0.4
        _PathTiling  ("Path Tiling (tiles/world-unit)",  Float) = 0.4
        _StoneTiling ("Stone Tiling (tiles/world-unit)", Float) = 0.4
        _RockTiling  ("Rock Tiling (tiles/world-unit)",  Float) = 0.4
        _Color       ("Tint", Color) = (1,1,1,1)
        // Cliff faces only (keyword _CLIFF_ROCK, set on a map's cliff material): rock projected from all three axes,
        // faded in by uv1.x. Every other ground material leaves the keyword off and draws exactly as before.
        _CliffRockMap    ("Cliff Rock Map (uv1.x)", 2D) = "white" {}
        _CliffRockTiling ("Cliff Rock Tiling (tiles/world-unit)", Float) = 0.4
        _CliffFaceColor  ("Cliff Face Colour (x the rock map; white = the map as it is)", Color) = (1,1,1,1)
    }

    SubShader
    {
        // Queue = end of the opaque range (Geometry+500 = 2500, still opaque) so the ground draws AFTER all other
        // solid objects. That's what makes the ink's ground stencil mask (bit 32, stamped in ForwardLit below)
        // order-independent: at any pixel a nearer object covers, the ground now fails the depth test and never
        // stamps it — so the stamp lands ONLY on genuinely-visible ground, never on the character/props in front.
        Tags { "RenderType"="Opaque" "Queue"="Geometry+500" "RenderPipeline"="UniversalPipeline" }

        // ── Main lit pass ────────────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            // Stamp stencil bit 32 on every ground pixel so the hand-drawn INK overlay (Unwritten/InkScreen) can skip
            // the ground entirely — the far ground viewed edge-on otherwise smears into a solid black ink band, and
            // the ground carries no linework worth keeping. (Bit 64 is owned by Unwritten/OccludedSilhouette; 32 is free.)
            Stencil { Ref 32 WriteMask 32 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog
            #pragma shader_feature_local _CLIFF_ROCK
            #pragma shader_feature_local _CLIFF_FACE_COLOR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Lib/AdditionalLights.hlsl"

            TEXTURE2D(_GrassMap);
            TEXTURE2D(_DirtMap);
            TEXTURE2D(_PathMap);
            TEXTURE2D(_StoneMap);
            TEXTURE2D(_RockMap);
            TEXTURE2D(_CliffRockMap);
            // Inline sampler: FORCE trilinear + repeat regardless of how the texture was imported. The Pxltgr
            // ground textures come in sprite-style (Clamp wrap), which with our continuous world-space UVs smears
            // the texture and leaves a bright "cross" at the UV origin (world 0,0) — this sidesteps that entirely.
            SAMPLER(sampler_trilinear_repeat);
            float _GrassTiling, _DirtTiling, _PathTiling, _StoneTiling, _RockTiling, _CliffRockTiling;
            float4 _Color;
            float4 _CliffFaceColor;

        #if defined(_CLIFF_FACE_COLOR)
            // Smooth 3D value noise in [0,1] on world position, so it varies up a cliff face as well as along it.
            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }
            float vnoise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(hash31(i),                  hash31(i + float3(1, 0, 0)), u.x),
                               lerp(hash31(i + float3(0, 1, 0)), hash31(i + float3(1, 1, 0)), u.x), u.y);
                float b = lerp(lerp(hash31(i + float3(0, 0, 1)), hash31(i + float3(1, 0, 1)), u.x),
                               lerp(hash31(i + float3(0, 1, 1)), hash31(i + float3(1, 1, 1)), u.x), u.y);
                return lerp(a, b, u.z);
            }
            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }
            // How close a point is to the border between two rock cells: the gap between its nearest and second-nearest
            // cell centres. 0 on a border — the cracks.
            float cellBorder(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 o = float3(x, y, z);
                    float3 r = o + hash33(i + o) - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return sqrt(d2) - sqrt(d1);
            }
        #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;     // .a = dirt mask; .r = road; .g = stone; .b = rock (all 0..1 weights)
            #if defined(_CLIFF_ROCK)
                float2 uv1        : TEXCOORD1; // .x = cliff rock weight, .y = how far up the cliff (0 foot, 1 top)
            #endif
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  blend      : TEXCOORD2; // interpolated vertex-alpha → the soft grass↔dirt gradient
                float3 pathW      : TEXCOORD3; // interpolated vertex .rgb  → road / stone / rock weights
                float  fogCoord   : TEXCOORD4;
            #if defined(_CLIFF_ROCK)
                float2 cliffW     : TEXCOORD5; // .x rock weight, .y how far up the cliff
            #endif
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.blend      = IN.color.a;
            #if defined(_CLIFF_ROCK)
                OUT.cliffW     = IN.uv1;
            #endif
                OUT.pathW      = IN.color.rgb;   // .x road, .y stone, .z rock
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // World-XZ tiling → seamless across the whole plot, independent of mesh UVs/size.
                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_trilinear_repeat, IN.positionWS.xz * _GrassTiling).rgb;
                half3 dirt  = SAMPLE_TEXTURE2D(_DirtMap,  sampler_trilinear_repeat, IN.positionWS.xz * _DirtTiling).rgb;
                half3 path  = SAMPLE_TEXTURE2D(_PathMap,  sampler_trilinear_repeat, IN.positionWS.xz * _PathTiling).rgb;
                half3 stone = SAMPLE_TEXTURE2D(_StoneMap, sampler_trilinear_repeat, IN.positionWS.xz * _StoneTiling).rgb;
                half3 rock  = SAMPLE_TEXTURE2D(_RockMap,  sampler_trilinear_repeat, IN.positionWS.xz * _RockTiling).rgb;
                half3 gd     = lerp(grass, dirt, saturate(IN.blend));            // grass+dirt blended base
                half3 albedo = lerp(gd, path,  saturate(IN.pathW.x));           // road feathered on top
                albedo       = lerp(albedo, stone, saturate(IN.pathW.y));       // then stone
                albedo       = lerp(albedo, rock,  saturate(IN.pathW.z));       // then rock
            #if defined(_CLIFF_ROCK) && defined(_CLIFF_FACE_COLOR)
                // The biome's face colour, never flat: lighter up the cliff and darker, greyer at its foot, in patches
                // about 2.5 m and 0.8 m across, split by thin dark cracks — mostly running up the face, some slanting,
                // broken into lengths.
                float3 wp = IN.positionWS;
                float up = saturate(IN.cliffW.y);
                float patches = vnoise3(wp * 0.4) * 0.65 + vnoise3(wp * 1.3 + 17.0) * 0.35;
                // Cracks: the borders of rock cells about 0.9 m across and 2 m tall, so most run up the face and the
                // rest slant between them; only some lengths of border are cracked.
                float border = cellBorder(wp * float3(1.1, 0.5, 1.1) + 31.0);
                float crackLengths = smoothstep(0.42, 0.58, vnoise3(wp * 0.7 + 71.0));
                float crack = (1.0 - smoothstep(0.0, fwidth(border) * 1.5 + 0.02, border)) * crackLengths;
                half3 face = _CliffFaceColor.rgb * (lerp(0.6, 1.15, up) * lerp(0.78, 1.22, patches) * (1.0 - 0.6 * crack));
                half grey = dot(face, half3(0.299, 0.587, 0.114));
                face = lerp(grey.xxx, face, lerp(0.55, 1.0, up));
                albedo       = lerp(albedo, face, saturate(IN.cliffW.x));
            #elif defined(_CLIFF_ROCK)
                // A cliff face is steep, so world-XZ alone would smear the rock down it in streaks: project the rock
                // from all three axes and weight each by how squarely the surface faces it.
                float3 tw = pow(abs(normalize(IN.normalWS)), 4.0);
                tw /= (tw.x + tw.y + tw.z);
                half3 rockX = SAMPLE_TEXTURE2D(_CliffRockMap, sampler_trilinear_repeat, IN.positionWS.zy * _CliffRockTiling).rgb;
                half3 rockY = SAMPLE_TEXTURE2D(_CliffRockMap, sampler_trilinear_repeat, IN.positionWS.xz * _CliffRockTiling).rgb;
                half3 rockZ = SAMPLE_TEXTURE2D(_CliffRockMap, sampler_trilinear_repeat, IN.positionWS.xy * _CliffRockTiling).rgb;
                albedo       = lerp(albedo, (rockX * tw.x + rockY * tw.y + rockZ * tw.z) * _CliffFaceColor.rgb, saturate(IN.cliffW.x));
            #endif
                albedo      *= _Color.rgb;

                // Cheap Lambert + ambient SH + main-light shadow (ground is large; keep it light).
                float3 N = normalize(IN.normalWS);
                Light ml = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half  ndl = saturate(dot(N, ml.direction));
                half3 lighting = ml.color * (ndl * ml.shadowAttenuation) + SampleSH(N);
                // Additional lights (lamps/torches) incl. their cast shadows — shared loop, see Lib/AdditionalLights.hlsl.
                lighting += ApplyAdditionalLightsDiffuse(IN.positionWS, N, false);

                half3 col = albedo * lighting;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ── Depth prepass (feeds _CameraDepthTexture for the screen-space weather FX) ─────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V vert (A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag (V i) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Shadow caster (tree/cloud shadows fall onto the ground via the main-light shadow map) ─────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };

            V vert (A i)
            {
                V o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(i.normalOS);
                float4 pos = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    pos.z = min(pos.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    pos.z = max(pos.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = pos;
                return o;
            }
            half4 frag (V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
