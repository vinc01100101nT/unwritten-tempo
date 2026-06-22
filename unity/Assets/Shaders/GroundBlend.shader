// Opaque, lit GROUND shader that layers THREE ground textures by VERTEX COLOR — the exact trick the Pxltgr
// "Fantasy Landscape" FBX uses to make grass melt into soil (its ground meshes carry white RGB + a 0..1 alpha
// ring; the GPU interpolates that across each triangle into a soft gradient). Two channels drive it, both baked
// per-vertex by PxltgrGroundBlend:
//   • vertex .a  → grass↔dirt blend (organic noise blobs)      base   = lerp(grass, dirt, a)
//   • vertex .r  → PATH weight (feathered routed polylines)     albedo = lerp(base, path, r)
// So the path is its OWN texture (road/soil/stones) feathered on top of the grass+dirt base — distinct yet
// seamless, no hard tile seams and no transparency overdraw (it stays Opaque — cheap, see PERFORMANCE.md).
// Textures are sampled by WORLD-XZ so tiling is seamless regardless of mesh size.
//
// Writes depth + casts/receives shadows like URP/Lit, so the screen-space weather passes (CloudShadow,
// WetGround, SnowGround — all read the depth texture) layer on top of it exactly as they do the tiled ground.
Shader "Unwritten/GroundBlend"
{
    Properties
    {
        _GrassMap    ("Grass Map", 2D) = "white" {}
        _DirtMap     ("Dirt Map",  2D) = "white" {}
        _PathMap     ("Path Map",  2D) = "white" {}
        _GrassTiling ("Grass Tiling (tiles/world-unit)", Float) = 0.4
        _DirtTiling  ("Dirt Tiling (tiles/world-unit)",  Float) = 0.4
        _PathTiling  ("Path Tiling (tiles/world-unit)",  Float) = 0.4
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
            // Inline sampler: FORCE trilinear + repeat regardless of how the texture was imported. The Pxltgr
            // ground textures come in sprite-style (Clamp wrap), which with our continuous world-space UVs smears
            // the texture and leaves a bright "cross" at the UV origin (world 0,0) — this sidesteps that entirely.
            SAMPLER(sampler_trilinear_repeat);
            float _GrassTiling, _DirtTiling, _PathTiling;
            float4 _Color;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;     // .a = dirt mask (0 grass→1 dirt); .r = path weight (0 none→1 path)
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  blend      : TEXCOORD2; // interpolated vertex-alpha → the soft grass↔dirt gradient
                float  pathW      : TEXCOORD3; // interpolated vertex-red   → the feathered path weight
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
                OUT.pathW      = IN.color.r;
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // World-XZ tiling → seamless across the whole plot, independent of mesh UVs/size.
                half3 grass = SAMPLE_TEXTURE2D(_GrassMap, sampler_trilinear_repeat, IN.positionWS.xz * _GrassTiling).rgb;
                half3 dirt  = SAMPLE_TEXTURE2D(_DirtMap,  sampler_trilinear_repeat, IN.positionWS.xz * _DirtTiling).rgb;
                half3 path  = SAMPLE_TEXTURE2D(_PathMap,  sampler_trilinear_repeat, IN.positionWS.xz * _PathTiling).rgb;
                half3 gd     = lerp(grass, dirt, saturate(IN.blend));     // grass+dirt blended base
                half3 albedo = lerp(gd, path, saturate(IN.pathW)) * _Color.rgb;   // path feathered on top

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
