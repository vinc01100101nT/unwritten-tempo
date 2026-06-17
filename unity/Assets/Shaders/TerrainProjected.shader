// Paints the merged 3D terrain with the 2D pixel-art ground tiles via WORLD-SPACE projection:
// grass on up-facing surfaces, dirt on cliffs, tiled by world position (frac() wraps the UVs so it
// repeats even though the pixel-art importer forces Clamp wrap). Lit by the main directional light
// with a gentle Lambert term, RECEIVES soft main-light shadows (player/props/cliffs darken the ground),
// and CASTS shadows (cliffs self-shadow). Built on URP's canonical boilerplate so it draws reliably.
// NOTE: renders in the GAME (3D renderer / Play), not the Scene view (2D renderer) — verify on Play.
Shader "Unwritten/TerrainProjected"
{
    Properties
    {
        _GrassTex ("Grass (top)", 2D) = "white" {}
        _DirtTex  ("Dirt (cliff)", 2D) = "white" {}
        _Tiles    ("Tiles per world unit", Float) = 4
        _GrassMin ("Grass slope min (normal.y)", Range(0,1)) = 0.5
        _GrassMax ("Grass slope max (normal.y)", Range(0,1)) = 0.8
        _ShadeFloor ("Shadowed brightness", Range(0,1)) = 0.5
        _Ambient  ("Ambient", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_DirtTex);  SAMPLER(sampler_DirtTex);

            CBUFFER_START(UnityPerMaterial)
                float _Tiles;
                float _GrassMin;
                float _GrassMax;
                float _ShadeFloor;
                float _Ambient;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 n = normalize(IN.normalWS);
                float3 wp = IN.positionWS * _Tiles;

                // frac() = tile repeats regardless of texture Clamp wrap; point-filtered seamless tile.
                float3 grass = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, frac(wp.xz)).rgb;
                float3 dirtX = SAMPLE_TEXTURE2D(_DirtTex,  sampler_DirtTex,  frac(wp.zy)).rgb;
                float3 dirtZ = SAMPLE_TEXTURE2D(_DirtTex,  sampler_DirtTex,  frac(wp.xy)).rgb;
                float wx = abs(n.x), wz = abs(n.z);
                float3 dirt = (dirtX * wx + dirtZ * wz) / max(wx + wz, 1e-3);

                float top = smoothstep(_GrassMin, _GrassMax, n.y);
                float3 albedo = lerp(dirt, grass, top);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = saturate(dot(n, mainLight.direction));
                float lit = lerp(_ShadeFloor, 1.0, mainLight.shadowAttenuation) * (0.55 + 0.45 * ndl);
                float3 col = albedo * (mainLight.color * lit + _Ambient);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // Cliffs/hills cast shadows (incl. onto lower terrain). Standard URP caster (as BillboardLit).
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings shadowVert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }

            half4 shadowFrag (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
