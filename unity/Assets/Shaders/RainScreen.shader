// Full-screen SCREEN-SPACE rain overlay. A camera-locked quad (placed by RainOverlay.cs) is drawn LAST
// (Overlay queue, after the billboards at 3000) and procedurally paints falling rain streaks per-pixel —
// so there are ZERO raindrop objects and the cost is fixed regardless of how many mobs are on screen.
//
// Unlike CloudShadow (which samples scene depth to project onto world surfaces), this is a pure 2D overlay:
// it does not read depth and does not care about the 3D world — it's the "sticker on the monitor glass".
// Streaks are generated from a cheap hash across a few parallax sheets, scrolled by _Phase (advanced in C#
// so it's framerate-independent and pauses with the game) and slanted by _Slant (wind).
Shader "Unwritten/RainScreen"
{
    Properties
    {
        _Color     ("Rain Color", Color) = (0.78, 0.85, 0.95, 0.5)
        _Intensity ("Intensity", Range(0,1)) = 0
        _Phase     ("Phase (scroll)", Float) = 0
        _Slant     ("Slant (wind)", Float) = 0.2
        _Density   ("Density (columns)", Float) = 60
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha   // standard alpha; rain tints toward _Color
            ZWrite Off
            ZTest Always                       // full-screen overlay, no depth interaction
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Color;
            float _Intensity, _Phase, _Slant, _Density;

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

            // One parallax sheet of RANDOMLY-SCATTERED falling streaks.
            //   cols/rows = density of the (invisible) cell grid each drop lives in
            //   narrow    = line-width exponent (HIGHER = thinner streak — use for far/background sheets)
            //   sparse    = per-cell existence cutoff 0..1 (HIGHER = fewer drops → bigger gaps)
            // Each grid cell holds at most one drop, placed at a RANDOM x with random length + brightness, so the
            // result has no visible columns. The dash is windowed (fades to 0 at both ends) so the per-cell random
            // regenerates invisibly as the grid scrolls — no popping.
            float sheet(float2 uv, float cols, float rows, float speed, float seed, float narrow, float sparse)
            {
                float2 g    = float2(uv.x * cols, uv.y * rows + _Phase * speed);   // scrolling cell grid
                float2 cell = floor(g);
                float2 f    = frac(g);

                float exists = step(sparse, hash21(cell + float2(seed + 11.0, seed)));     // gaps
                float rx     = hash21(cell + float2(seed,       seed + 3.0));               // random x in cell
                float rl     = hash21(cell + float2(seed + 7.0, seed + 1.0));               // random length
                float bright = lerp(0.5, 1.0, hash21(cell + float2(seed + 5.0, seed + 9.0)));

                float dropX = lerp(0.18, 0.82, rx);
                float fx    = abs(f.x - dropX);
                float lane  = pow(saturate(1.0 - fx * 2.0), narrow);             // thin vertical line at dropX

                float len   = lerp(0.35, 0.9, rl);                               // random dash length
                float dash  = smoothstep(0.0, 0.10, f.y) * (1.0 - smoothstep(len - 0.10, len, f.y));
                dash       *= lerp(0.6, 1.0, saturate(f.y / max(0.001, len)));    // brighter toward the leading tip

                return dash * lane * exists * bright;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Robust screen UV straight from the pixel position (no helper-function dependency).
                float2 uv = IN.positionHCS.xy / max(float2(1.0, 1.0), _ScreenParams.xy);
                uv.x *= _ScreenParams.x / max(1.0, _ScreenParams.y);   // aspect-correct so streaks aren't stretched
                uv.x += uv.y * _Slant;                                 // slant with the wind

                // Depth-layered: NEAR = few, widely-spaced, FAT, bright, fast streaks; FAR = many, THIN, dim,
                // slow ones. The width + brightness + speed falloff across layers sells the parallax/depth.
                //                  cols          rows  speed  seed  narrow sparse   weight
                float r = 0.0;
                r += sheet(uv, _Density * 0.45,  9.0, 1.50,  1.0,   6.0, 0.74) * 1.00;  // foreground (closest)
                r += sheet(uv, _Density * 0.80, 12.0, 1.10,  7.0,  12.0, 0.66) * 0.70;  // mid
                r += sheet(uv, _Density * 1.40, 17.0, 0.80, 19.0,  22.0, 0.58) * 0.45;  // back
                r += sheet(uv, _Density * 2.30, 24.0, 0.60, 29.0,  40.0, 0.55) * 0.26;  // far mist

                float a = saturate(r * 1.7) * _Intensity * _Color.a;
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
