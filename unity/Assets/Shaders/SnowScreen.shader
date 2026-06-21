// Full-screen SCREEN-SPACE snow overlay. Twin of RainScreen: a camera-locked quad (placed by SnowOverlay.cs)
// drawn LAST (Overlay queue, after the billboards) procedurally paints DRIFTING FLAKES per-pixel — so there
// are ZERO flake objects and the GPU cost is fixed regardless of how many mobs fill the screen.
//
// Unlike WetGround/SnowGround (which sample scene DEPTH to re-shade world surfaces), this is a pure 2D overlay:
// it never reads depth — it's the "sticker on the monitor glass". Flakes are soft round dots scattered across a
// few parallax sheets, falling slowly and SWAYING sideways (sine on _Phase) so they flutter like real snow.
// _Phase is advanced in C# so it's framerate-independent and freezes with the game; _Drift is the wind sway.
Shader "Unwritten/SnowScreen"
{
    Properties
    {
        _Color     ("Snow Color", Color) = (0.96, 0.98, 1.0, 0.9)
        _Intensity ("Intensity", Range(0,1)) = 0
        _Phase     ("Phase (scroll)", Float) = 0
        _Drift     ("Drift (wind sway)", Float) = 0.25
        _Density   ("Density (columns)", Float) = 40
        _FlakeSize ("Flake Size", Range(0.05,0.9)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha   // standard alpha; flakes tint toward _Color
            ZWrite Off
            ZTest Always                       // full-screen overlay, no depth interaction
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Color;
            float _Intensity, _Phase, _Drift, _Density, _FlakeSize;
            float2 _Parallax;   // camera pan/rotate offset (screen UV), accumulated in C# — anchors snow to the world

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            // Verts span [-1,1] in object XY; emit straight to clip space so the quad always fills the screen.
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

            // One parallax sheet of RANDOMLY-SCATTERED falling flakes.
            //   cols/rows = density of the (invisible) cell grid each flake lives in
            //   sway      = sideways flutter amplitude for this sheet (near sheets sway wider)
            //   big       = flake-radius multiplier (near sheets = bigger, softer flakes)
            //   sparse    = per-cell existence cutoff 0..1 (HIGHER = fewer flakes → bigger gaps)
            // Each grid cell holds at most one flake at a RANDOM position; the row coordinate scrolls down with
            // _Phase, so flakes fall. A per-cell phase offsets each flake's sway so they don't sway in lockstep.
            float sheet(float2 uv, float cols, float rows, float speed, float seed,
                        float sway, float big, float sparse, float par)
            {
                uv += _Parallax * par;   // camera parallax in SCREEN space — near sheets (high par) shift more = depth
                float2 g    = float2(uv.x * cols, uv.y * rows + _Phase * speed);   // scrolling cell grid
                float2 cell = floor(g);
                float2 f    = frac(g);

                float exists = step(sparse, hash21(cell + float2(seed + 11.0, seed)));   // gaps
                float rx     = hash21(cell + float2(seed,       seed + 3.0));             // random x in cell
                float ry     = hash21(cell + float2(seed + 7.0, seed + 1.0));             // random y in cell
                float bright = lerp(0.55, 1.0, hash21(cell + float2(seed + 5.0, seed + 9.0)));

                // Flake centre, with a per-flake sideways sway driven by the global phase (wind flutter).
                float ph    = hash21(cell + float2(seed + 2.0, seed + 8.0)) * 6.2831853;
                float cx     = lerp(0.2, 0.8, rx) + sin(_Phase * 0.9 + ph) * sway;
                float cy     = lerp(0.2, 0.8, ry);

                float2 d = (f - float2(cx, cy));
                d.x *= 0.85;                                                  // flakes a touch taller than wide
                float r = length(d);
                float radius = _FlakeSize * big * 0.18;
                float flake = smoothstep(radius, radius * 0.25, r);          // soft round dot

                return flake * exists * bright;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Robust screen UV straight from the pixel position (no helper-function dependency).
                float2 uv = IN.positionHCS.xy / max(float2(1.0, 1.0), _ScreenParams.xy);
                uv.x *= _ScreenParams.x / max(1.0, _ScreenParams.y);   // aspect-correct so flakes aren't stretched

                // Depth-layered: NEAR = few, BIG, bright, FAST, wide-swaying, high-parallax flakes; FAR = many,
                // tiny, dim, SLOW, low-parallax. Speed scales with size (big flakes fall faster) for depth.
                //                  cols          rows  speed seed  sway          big  sparse  par  weight
                float s = 0.0;
                s += sheet(uv, _Density * 0.45,  7.0, 0.72,  1.0, 0.120 * _Drift, 1.6, 0.74, 1.00) * 1.00;  // foreground
                s += sheet(uv, _Density * 0.80, 10.0, 0.48,  7.0, 0.090 * _Drift, 1.1, 0.66, 0.68) * 0.78;  // mid
                s += sheet(uv, _Density * 1.40, 14.0, 0.30, 19.0, 0.060 * _Drift, 0.8, 0.58, 0.42) * 0.52;  // back
                s += sheet(uv, _Density * 2.30, 20.0, 0.18, 29.0, 0.040 * _Drift, 0.6, 0.55, 0.26) * 0.30;  // far haze

                float a = saturate(s) * _Intensity * _Color.a;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
