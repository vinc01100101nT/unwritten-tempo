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
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        // ── Main lit pass ────────────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassMap);
            TEXTURE2D(_DirtMap);
            TEXTURE2D(_PathMap);
            TEXTURE2D(_StoneMap);
            TEXTURE2D(_RockMap);
            // Inline sampler: FORCE trilinear + repeat regardless of how the texture was imported. The Pxltgr
            // ground textures come in sprite-style (Clamp wrap), which with our continuous world-space UVs smears
            // the texture and leaves a bright "cross" at the UV origin (world 0,0) — this sidesteps that entirely.
            SAMPLER(sampler_trilinear_repeat);
            float _GrassTiling, _DirtTiling, _PathTiling, _StoneTiling, _RockTiling;
            float4 _Color;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;     // .a = dirt mask; .r = road; .g = stone; .b = rock (all 0..1 weights)
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  blend      : TEXCOORD2; // interpolated vertex-alpha → the soft grass↔dirt gradient
                float3 pathW      : TEXCOORD3; // interpolated vertex .rgb  → road / stone / rock weights
                float  fogCoord   : TEXCOORD4;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.blend      = IN.color.a;
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
                albedo      *= _Color.rgb;

                // Cheap Lambert + ambient SH + main-light shadow (ground is large; keep it light).
                float3 N = normalize(IN.normalWS);
                Light ml = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half  ndl = saturate(dot(N, ml.direction));
                half3 col = albedo * (ml.color * (ndl * ml.shadowAttenuation) + SampleSH(N));

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
