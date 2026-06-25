// HAND-DRAWN "INK" overlay. A screen-covering quad (placed by HandDrawnFX.cs) reads the scene DEPTH texture and
// draws dark INK OUTLINES wherever the depth signal says there's an edge — object silhouettes (big depth jumps)
// AND interior creases (kinks in the surface), plus a faint PAPER grain over everything. This is the linework half
// of the hand-drawn look; it deliberately does NOT touch colour (you can't desaturate behind an alpha-blended quad)
// — saturation is handled separately by HandDrawnFX driving a URP Color-Adjustments volume.
//
// Same depth-quad pattern as Unwritten/FogScreen: it draws LAST (Overlay queue, ZTest Always) so the ink sits over
// the world meshes AND the sprite mobs/player. Edges come from FIVE depth taps (centre + 4 neighbours): the FIRST
// difference (|L-R|,|U-D|) finds silhouettes, the SECOND difference (|L+R-2C|) finds creases — so we get both
// without a normals prepass. Sky (depth at the far plane) is skipped so the horizon isn't outlined. Needs the URP
// depth texture ON (already enabled for fog/wet-ground). GPU cost is one fixed pass regardless of mob count, and
// the quad is disabled entirely when intensity = 0 (zero draw calls). Tune live from DevSettings (P) ▸ "Hand-Drawn".
Shader "Unwritten/InkScreen"
{
    Properties
    {
        _InkColor    ("Ink Color", Color) = (0.08, 0.07, 0.06, 1.0)
        _Intensity   ("Master Intensity", Range(0,1)) = 0
        _LineStrength("Line Strength", Range(0,1)) = 0.85
        _DepthEdge   ("Silhouette Sensitivity", Float) = 30
        _NormalEdge  ("Crease Sensitivity", Float) = 14
        _Thickness   ("Line Thickness (px)", Float) = 1.2
        _EdgeThresh  ("Edge Threshold", Range(0,1)) = 0.12
        _EdgeSoft    ("Edge Softness", Range(0.001,0.5)) = 0.12
        _PaperStrength("Paper Grain", Range(0,1)) = 0.10
        _PaperScale  ("Paper Scale (px)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha   // standard alpha; the ink darkens the scene toward _InkColor
            ZWrite Off
            ZTest Always                       // full-screen overlay; depth comes from the sampled texture
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _InkColor;
            float _Intensity, _LineStrength, _DepthEdge, _NormalEdge, _Thickness, _EdgeThresh, _EdgeSoft;
            float _PaperStrength, _PaperScale;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            // Verts span [-1,1] in object XY; emit straight to clip space so the quad always fills the screen
            // (HandDrawnFX gives it huge bounds so it's never frustum-culled).
            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = float4(IN.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                return OUT;
            }

            // Linear eye depth at a screen UV (distance from camera). Far-plane (sky) returns a huge value via the
            // raw==far check done by the caller.
            float EyeDepth(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);

                float rawC = SampleSceneDepth(uv);
                // Sky / nothing-rendered: no ink (don't outline the horizon), but still allow paper grain.
                bool isSky = (rawC == UNITY_RAW_FAR_CLIP_VALUE);

                float2 texel = _Thickness / _ScreenParams.xy;   // neighbour offset in UV
                float dC = LinearEyeDepth(rawC, _ZBufferParams);
                float dL = EyeDepth(uv - float2(texel.x, 0));
                float dR = EyeDepth(uv + float2(texel.x, 0));
                float dU = EyeDepth(uv + float2(0, texel.y));
                float dD = EyeDepth(uv - float2(0, texel.y));

                float invC = 1.0 / max(0.05, dC);               // scale-invariant: far edges aren't over-detected
                // FIRST difference → silhouettes (big depth jumps between sides).
                float silh   = (abs(dL - dR) + abs(dU - dD)) * invC;
                // SECOND difference → creases (surface kinks where the first difference is ~flat).
                float crease = (abs(dL + dR - 2.0 * dC) + abs(dU + dD - 2.0 * dC)) * invC;

                float e = silh * _DepthEdge + crease * _NormalEdge;
                e = smoothstep(_EdgeThresh, _EdgeThresh + _EdgeSoft, e);
                if (isSky) e = 0.0;

                // Paper grain — faint static fibre noise (screen-space, low strength) so flat areas read as "on paper".
                float paper = hash21(floor(IN.positionHCS.xy / max(1.0, _PaperScale)));
                float paperA = (1.0 - paper) * _PaperStrength;

                float a = saturate(max(e * _LineStrength, paperA)) * _Intensity * _InkColor.a;
                return half4(_InkColor.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
