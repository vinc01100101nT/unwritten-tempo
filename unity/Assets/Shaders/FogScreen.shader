// Full-screen DEPTH FOG. A screen-covering quad (placed by FogOverlay.cs) reads the scene DEPTH texture and fogs
// each pixel by its TRUE distance from the camera — so far trees/houses/ground genuinely dissolve into haze while
// what's near the player stays clear. This is the real thing, not the old "sticker on the glass": the fade follows
// actual depth, the way you'd expect distance fog to.
//
// It draws LAST (Overlay queue, after the billboard characters) with ZTest Always, so it tints over EVERYTHING —
// world meshes AND the sprite mobs/player. Sprites are transparent and usually aren't in the depth texture, so for
// a sprite pixel we sample the opaque surface directly behind it (the ground it stands on), which is ~its own
// distance — so characters fog by roughly their distance too. Sky / nothing-rendered pixels (depth at the far
// plane) are treated as infinitely far → fully fogged, so the horizon washes out into the fog colour.
//
// The veil is textured with world-pinned value-noise FBM (sampled in world X/Z like CloudShadow), so the fog
// billows are anchored to the world and you move THROUGH them — no screen-stuck smear. _Phase scrolls the noise and
// is advanced in C# (framerate-independent, freezes with the game). Needs the URP depth texture ON (same as
// WetGround/CloudShadow — already enabled for them).
Shader "Unwritten/FogScreen"
{
    Properties
    {
        _Color     ("Fog Color", Color) = (0.80, 0.82, 0.85, 1.0)
        _LightTint ("Day/Night Light Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0,1)) = 0
        _Phase     ("Phase (scroll)", Float) = 0
        _Scale     ("World Noise Scale (1/patchSize)", Float) = 0.05
        _Coverage  ("Coverage (max opacity)", Range(0,1)) = 0.9
        _Floor     ("Floor (min veil in fog)", Range(0,1)) = 0.5
        _FogStart  ("Fog Start Distance", Float) = 8
        _FogEnd    ("Fog Full Distance", Float) = 45
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha   // standard alpha; the veil tints the scene toward _Color
            ZWrite Off
            ZTest Always                       // full-screen overlay; distance comes from the sampled depth
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Color, _LightTint;
            float _Intensity, _Phase, _Scale, _Coverage, _Floor, _FogStart, _FogEnd;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            // Verts span [-1,1] in object XY; emit straight to clip space so the quad always fills the screen
            // (FogOverlay gives it huge bounds so it's never frustum-culled).
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

            // Smooth value noise (bilinear-interpolated hashed lattice).
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Fractal billows — 3 octaves of value noise.
            float fbm(float2 p)
            {
                float s = 0.0, amp = 0.5;
                for (int i = 0; i < 3; i++) { s += amp * vnoise(p); p *= 2.02; amp *= 0.5; }
                return s;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);
                float depth = SampleSceneDepth(uv);

                float f;          // 0 = clear (near), 1 = fully fogged (far)
                float2 nuv;       // noise coords (world X/Z where we have depth; screen proxy for the sky)

                if (depth == UNITY_RAW_FAR_CLIP_VALUE)
                {
                    f = 1.0;                                  // sky / nothing = infinitely far = full fog
                    nuv = uv * 12.0;                          // screen-space noise so the horizon isn't a flat wall
                }
                else
                {
                    float eye = LinearEyeDepth(depth, _ZBufferParams);          // distance from camera along view
                    f = saturate((eye - _FogStart) / max(0.01, _FogEnd - _FogStart));
                    float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                    nuv = wp.xz * _Scale;                                       // world-pinned billows
                }

                // Two FBM fields drifting in different directions so the fog rolls and breathes.
                float n1 = fbm(nuv + float2( 0.020, 0.011) * _Phase);
                float n2 = fbm(nuv * 1.7 + float2(-0.013, 0.024) * _Phase + 19.3);
                float billow = saturate(n1 * 0.65 + n2 * 0.55);

                // Keep the veil above a floor in fogged areas so the distance reads as solid mist, not patchy gaps.
                float veil = lerp(_Floor, 1.0, billow);

                float a = saturate(f * veil) * _Coverage * _Intensity * _Color.a;
                // Dim the fog at night to match the darkened world (white tint by day = no change).
                return half4(_Color.rgb * _LightTint.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
