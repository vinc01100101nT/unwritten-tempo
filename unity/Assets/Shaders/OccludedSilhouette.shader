// X-ray occlusion silhouette. Used as an OVERRIDE material by OccludedSilhouetteFeature: after transparents,
// the feature re-draws each marked body (OccludedSilhouetteTarget — the player's mesh, or a creature's 3D mesh or
// flat picture) with this material's THREE passes so that wherever the body is HIDDEN behind opaque geometry
// (house / tree / wall / grass) a flat coloured silhouette shows through — the classic "see the hero behind the
// wall" look. The comments below say "player" for whichever marked body is being drawn.
//
// WHY THREE PASSES (and not a per-fragment depth compare): a depth test paints a fragment when it sits behind the
// stored depth, but the stored depth already contains the PLAYER — so the player's own nearer parts (head-cap,
// weapon) count as occluders and the face/eyes behind them get painted ("eyes through the back of the head").
// That self-occlusion is unfixable by any per-fragment depth test. Instead we decide occlusion by SCREEN AREA,
// using a stencil bit as scratch:
//   Pass 0  ClearArea   — over the whole on-screen silhouette, zero our stencil bit (fresh scratch).
//   Pass 1  MarkVisible — where the player is actually the front-most surface (passes the depth test), SET the bit.
//                         Any visible part of the player at a pixel sets it, so a pixel hidden behind the player's
//                         OWN head-cap is still "visible" (the head-cap set it) and won't be painted.
//   Pass 2  PaintOccluded — paint the silhouette colour over the silhouette area only where the bit is NOT set,
//                         i.e. the player covers this pixel yet nothing of the player is visible here → it's hidden
//                         behind SCENE geometry. Area minus visible = occluded. No self-occlusion.
// The stencil bit is cleared then used within one render pass, and we only ever touch our own bit (write mask),
// so it can't disturb anything the pipeline does earlier or later.
Shader "Unwritten/OccludedSilhouette"
{
    Properties
    {
        _OutlineColor ("Silhouette Color", Color) = (0.55, 0.8, 1.0, 1.0)
        // A FLAT body's picture, handed over by its SpriteRenderer, so the shape is the painted picture and not the
        // sheet it is painted on. A 3D body hands over nothing and reads this white default: solid everywhere.
        [PerRendererData] _MainTex ("Shape Texture", 2D) = "white" {}
        // The cut-out line every flat body's own material is drawn with (Unwritten/BillboardLit).
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }

    // Our private scratch stencil bit is 64 (0x40). It's cleared (Pass 0) before use, so its prior contents never
    // matter; the write mask (64) means we leave every other bit untouched. (Literal, not a macro — ShaderLab
    // fixed-function Stencil values don't reliably expand #defines.)

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            half _Cutoff;
        CBUFFER_END

        struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        // Skinned positions arrive already posed in object space (the SkinnedMeshRenderer bakes the skin), so the
        // standard transform renders the live animation pose.
        Varyings Vert (Attributes IN)
        {
            Varyings OUT;
            OUT.positionCS = GetVertexPositionInputs(IN.positionOS).positionCS;
            OUT.uv = IN.uv;
            return OUT;
        }

        // Every pass keeps only the pixels the body really paints — the same cut-out test the flat body's own
        // material makes — so all three agree on the body's exact shape.
        half4 FragColor (Varyings IN) : SV_Target
        {
            clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a - _Cutoff);
            return _OutlineColor;
        }
        ENDHLSL

        // Pass 0 — clear our stencil bit across the whole on-screen silhouette (ZTest Always = every covered pixel,
        // occluded or not). Cull Off so open/thin parts (bow, hair) are covered too. No colour written.
        Pass
        {
            Name "ClearArea"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Always ZWrite Off Cull Off ColorMask 0
            Stencil { Ref 0 WriteMask 64 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragColor
            ENDHLSL
        }

        // Pass 1 — mark VISIBLE. ZTest LEqual passes only where the player is at (or in front of) the stored scene
        // depth, i.e. actually visible. We only ever SET the bit (Pass Replace, ZFail Keep), so it's order-independent:
        // any visible fragment at a pixel latches it on. Occluded pixels leave the bit at 0 (cleared by Pass 0).
        Pass
        {
            Name "MarkVisible"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest LEqual ZWrite Off Cull Off ColorMask 0
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp Always Pass Replace ZFail Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragColor
            ENDHLSL
        }

        // Pass 2 — paint. ZTest Always covers the whole silhouette area; the stencil test keeps only pixels where our
        // bit is NOT set = the player is here but no part of it is visible = hidden behind scene geometry. Pass Replace
        // sets the bit after painting so overlapping fragments don't blend the colour twice (matters for alpha < 1).
        Pass
        {
            Name "PaintOccluded"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Always ZWrite Off Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp NotEqual Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragColor
            ENDHLSL
        }
    }

    Fallback Off
}
