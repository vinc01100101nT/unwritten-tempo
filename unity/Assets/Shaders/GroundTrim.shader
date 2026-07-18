// Opaque, lit shader for GROUND TRIM — the decorative curb strips on 1-notch drops (raised-sidewalk lip / gutter
// side) and the single square corner stones, both baked by GroundBlendBuilder into their own submeshes. It is the
// ink-immune sibling of Unwritten/GroundBlend: same stencil-bit-32 stamp and same end-of-opaque queue, so the
// hand-drawn INK overlay (Unwritten/InkScreen) skips these surfaces exactly as it skips the main ground — without
// that, a curb wall viewed edge-on smears into the same solid black ink band the far ground used to (the ink's
// crease term saturates on steep surfaces).
//
// Unlike GroundBlend (which samples by WORLD-XZ and would streak on a vertical wall), this samples the mesh's own
// UV0 — the curb UVs GroundBlendBuilder bakes (U along the edge, V = height in notches) and the corner stone's
// [0,1]² mapping — so each stone reads correctly. Single _BaseMap; property names match URP/Lit (_BaseMap,
// _BaseColor) so a material can be re-pointed at this shader with its texture assignment intact.
Shader "Unwritten/GroundTrim"
{
    Properties
    {
        _BaseMap   ("Base Map", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        // Same end-of-opaque queue as GroundBlend (Geometry+500 = 2500) so the trim draws AFTER all other solid
        // objects — that's what makes the bit-32 ink mask order-independent: where a nearer object (player/mob/prop)
        // covers the trim, the trim fails the depth test and never stamps it, so the stamp lands only on genuinely
        // visible trim, never on the character in front of a curb.
        Tags { "RenderType"="Opaque" "Queue"="Geometry+500" "RenderPipeline"="UniversalPipeline" }

        // ── Main lit pass ────────────────────────────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            // Stamp stencil bit 32 so Unwritten/InkScreen skips these pixels (matches GroundBlend). Bit 64 is
            // owned by Unwritten/OccludedSilhouette; 32 is the shared ground/trim ink-exclude bit.
            Stencil { Ref 32 WriteMask 32 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Lib/AdditionalLights.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            float4 _BaseColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;

                // Cheap Lambert + ambient SH + main-light shadow, matching the adjacent GroundBlend surface.
                float3 N = normalize(IN.normalWS);
                Light ml = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half  ndl = saturate(dot(N, ml.direction));
                half3 lighting = ml.color * (ndl * ml.shadowAttenuation) + SampleSH(N);
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

        // ── Shadow caster ─────────────────────────────────────────────────────────────────────────────────
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
