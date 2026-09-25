// Full-screen CLOUD LAYER: patchy white clouds floating over the map at treetop height. A screen-covering quad
// (placed by CloudLayerOverlay.cs) reads the scene DEPTH texture and, for every pixel, takes the stretch of the view
// ray from the camera to the surface drawn there (capped at _MaxDistance where nothing was drawn) and cuts out the
// part that lies inside the cloud slab between world heights _Bottom and _Top. The longer that part, and the denser
// the clouds at its middle, the more opaque the pixel — a cheap stand-in for looking THROUGH a layer of cloud:
//   - a tree top poking above the slab has little or no cloud in front of it;
//   - the ground shows through wherever the noise leaves a gap;
//   - it works with the camera above, inside or below the slab (below it, the clouds hang overhead).
// The density is world-pinned value-noise FBM (like FogScreen), so the clouds sit in the world and you walk under
// them; _Phase scrolls it and is advanced in C#. Around _Focus (the point on the ground the camera looks at) the
// clouds fade out between _ClearRadius and _ClearFadeEnd, so the player is never hidden.
// Needs the URP depth texture ON (already required by FogScreen/WetGround).
//
// Queue "Overlay+50": after the hand-drawn ink (Overlay) and BEFORE the fog veil (Overlay+100), so a biome's fog
// still hazes the clouds by distance.
Shader "Unwritten/CloudLayer"
{
    Properties
    {
        _Color          ("Cloud Colour (lit)", Color) = (1.0, 1.0, 1.0, 1.0)
        _ShadeColor     ("Cloud Colour (shaded)", Color) = (0.76, 0.80, 0.88, 1.0)
        _LightTint      ("Day/Night Light Tint", Color) = (1, 1, 1, 1)
        _Intensity      ("Intensity", Range(0,1)) = 0
        _Phase          ("Phase (scroll)", Float) = 0
        _Bottom         ("Cloud Bottom (world Y)", Float) = 2.0
        _Top            ("Cloud Top (world Y)", Float) = 4.5
        _Scale          ("Cloud Noise Scale (1/size)", Float) = 0.0625
        _ClearShare     ("Share Left Clear", Range(0,1)) = 0.5
        _OpacityPerMetre("Opacity Per Metre Of Cloud", Float) = 0.7
        _MaxDistance    ("Max Distance (nothing drawn)", Float) = 80
        _Focus          ("Clear Centre (world XYZ)", Vector) = (0, 0, 0, 0)
        _ClearRadius    ("Clear Radius", Float) = 6
        _ClearFadeEnd   ("Clear Fade End", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Color, _ShadeColor, _LightTint, _Focus;
            float _Intensity, _Phase, _Bottom, _Top, _Scale, _ClearShare, _OpacityPerMetre, _MaxDistance;
            float _ClearRadius, _ClearFadeEnd;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

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

            float fbm(float2 p)
            {
                float s = 0.0, amp = 0.5;
                for (int i = 0; i < 4; i++) { s += amp * vnoise(p); p *= 2.03; amp *= 0.5; }
                return s;
            }

            // Two FBM fields drifting different ways, so the clouds roll and change shape instead of sliding.
            float billows(float2 p)
            {
                float n1 = fbm(p + float2( 0.020, 0.012) * _Phase);
                float n2 = fbm(p * 1.8 + float2(-0.015, 0.022) * _Phase + 23.7);
                return saturate(n1 * 0.7 + n2 * 0.5);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);
                float depth = SampleSceneDepth(uv);
                bool nothingDrawn = depth == UNITY_RAW_FAR_CLIP_VALUE;

                // This pixel's view ray, and how far along it the drawn surface is.
                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float3 toSurf = wp - cam;
                float dist = max(1e-4, length(toSurf));
                float3 dir = toSurf / dist;
                float tEnd = nothingDrawn ? _MaxDistance : min(dist, _MaxDistance);

                // The stretch of the ray [t0, t1] that lies inside the cloud slab.
                float t0, t1;
                if (abs(dir.y) < 1e-4)
                {
                    bool inside = cam.y >= _Bottom && cam.y <= _Top;
                    t0 = 0.0;
                    t1 = inside ? tEnd : 0.0;
                }
                else
                {
                    float ta = (_Bottom - cam.y) / dir.y;
                    float tb = (_Top    - cam.y) / dir.y;
                    t0 = max(0.0, min(ta, tb));
                    t1 = min(tEnd, max(ta, tb));
                }
                float len = t1 - t0;
                if (len <= 0.0) return half4(0, 0, 0, 0);

                // Cloud density at the middle of that stretch: world-pinned, patchy (the noise's lower share is gap).
                float3 mid = cam + dir * ((t0 + t1) * 0.5);
                float b = billows(mid.xz * _Scale);
                // billows() runs from about 0.39 (5th percentile) to 0.74 (95th), measured offline; the cut sits 0.075
                // below that percentile so density passes 0.5 there, and _ClearShare reads as roughly the share of the
                // map left clear.
                float cut = lerp(0.31, 0.66, _ClearShare);
                float density = smoothstep(cut, cut + 0.15, b);

                // Keep the ground around the player clear.
                float keep = smoothstep(_ClearRadius, max(_ClearRadius + 0.01, _ClearFadeEnd), distance(mid.xz, _Focus.xz));

                float a = (1.0 - exp(-len * _OpacityPerMetre * density)) * keep * _Intensity;

                // Sunlit near the slab's top and in the thick middles; shaded underneath and at thin edges.
                float up = saturate((mid.y - _Bottom) / max(0.01, _Top - _Bottom));
                float lit = saturate(0.5 * up + 0.5 * smoothstep(cut, cut + 0.3, b));
                float3 col = lerp(_ShadeColor.rgb, _Color.rgb, lit);
                return half4(col * _LightTint.rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
