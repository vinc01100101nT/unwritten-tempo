// Solid stylized lighting for the 3D props that are NOT foliage — houses, walls, rocks, crates, furniture.
// The SOLID sibling of Unwritten/FoliageWind: it shares that shader's clean storybook lighting (flat Lambert
// main light + soft shadow + SH ambient + lamp/torch lights, and crucially NO specular/smoothness sheen) so a
// Trellis/GPT auto-mesh reads as a clean painted object instead of a muddy glossy PBR blob. What it drops:
//   • no wind bend  — buildings are static; the vertex stays where it is.
//   • single-sided  — a house is a solid closed mesh, so Cull Back and light only the front face (the double-
//                     sided leaf trick would light interior back-faces and wash the walls out).
//   • opaque        — no alpha clip; the whole surface draws (foliage needs the cutout, solid props don't).
//
// Why it makes raw Trellis houses look better: same reason it helped the trees. The auto-mesh's painted texture
// already has the detail; URP/Lit buries it under specular highlights + normal-mapped facet noise on a low-poly
// auto-mesh. This flat wash lets the painted detail read and kills the plastic sheen.
//
// Applied at author time by cloning a prop's URP/Lit material onto this shader (Base Map + tint copied across),
// cached per source material so instances stay batched. Add to Project Settings ▸ Graphics ▸ Always Included
// Shaders for builds (the editor tool that applies it does this automatically).
Shader "Unwritten/StylizedLit"
{
    Properties
    {
        [MainTexture] _BaseMap   ("Base Map", 2D)      = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "Queue"           = "Geometry"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
        CBUFFER_END
        ENDHLSL

        // ---- forward: lambert main light + soft shadow + SH ambient + lamps, single-sided, opaque -----------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back           // solid closed mesh — only the outward faces
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Lib/AdditionalLights.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 posWS   = TransformObjectToWorld(IN.positionOS);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);

                half3 n   = normalize(IN.normalWS);
                half  ndl = saturate(dot(n, mainLight.direction));   // single-sided: front face only

                half3 diffuse   = mainLight.color * (ndl * mainLight.shadowAttenuation);
                half3 ambient   = SampleSH(n);
                half3 addLights = ApplyAdditionalLightsDiffuse(IN.positionWS, n, false);   // solid: outward face only
                return half4(tex.rgb * (ambient + diffuse + addLights), tex.a);
            }
            ENDHLSL
        }

        // ---- shadow caster: plain static shadow (no wind bend) ---------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings shadowVert (Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS);
                float3 nWS   = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 shadowFrag (Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
