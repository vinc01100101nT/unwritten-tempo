// STANDING AND RUNNING WATER of a seamless world: a river's or a pond's surface, one sheet at the water's height laid
// over a hollow in the ground and reaching in under its banks (built by the seamless world builder), so its edge is
// wherever the bank rises out of it — never the sheet's own corners.
//   - How deep the water is at a pixel is read from the depth of the ground behind it. Where it is shallow the sheet
//     fades out, so the water melts into the bank; a little deeper a broken band of foam lies along the shore; deeper
//     still it darkens from shallow to deep colour.
//   - Waves are two layers of noise sliding along the flow (uv0: metres per second along world X and Z — zero for a pond,
//     which then only stirs in place). Their slopes tilt the surface, so the sun glints off it and the far water
//     brightens toward the sky the flatter you look across it.
//   - Every foot coming down in it leaves a ring (WaterSheet.Ripple sets _WaterRipples: x, z, start time, size).
// Lit by the main light and the ambient light like the ground, so it darkens at night with everything else. Drawn after
// the ground and before the hand-drawn ink and the fog; it writes no depth, so the fog hazes the bed through it.
// Cost: one transparent pass over the water's own pixels — one depth read, four noise lookups, eight ring tests.
Shader "Unwritten/SeamlessWater"
{
    Properties
    {
        _ShallowColor ("Shallow Colour", Color) = (0.30, 0.60, 0.66, 1)
        _DeepColor    ("Deep Colour", Color) = (0.08, 0.24, 0.38, 1)
        _DeepAt       ("Depth That Reads As Deep (m)", Float) = 1.0
        _ShoreFade    ("Depth Over Which The Edge Fades In (m)", Float) = 0.25
        _Opacity      ("Opacity Of Deep Water", Range(0,1)) = 0.85
        _FoamColor    ("Foam Colour", Color) = (0.92, 0.97, 1.0, 1)
        _FoamDepth    ("Depth Of The Foam Band (m)", Float) = 0.35
        _SkyColor     ("Sky Colour Seen Across The Water", Color) = (0.70, 0.84, 0.95, 1)
        _WaveScale    ("Wave Size (1/m)", Float) = 0.55
        _WaveTilt     ("How Much The Waves Tilt The Surface", Float) = 0.35
        _Gloss        ("Glint Sharpness", Float) = 90
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _FoamColor, _SkyColor;
                float _DeepAt, _ShoreFade, _Opacity, _FoamDepth, _WaveScale, _WaveTilt, _Gloss;
            CBUFFER_END

            // Rings left by feet, set by WaterSheet.Ripple: x, z, start time, size (0 = none).
            float4 _WaterRipples[8];

            struct Attributes { float4 positionOS : POSITION; float2 flow : TEXCOORD0; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float2 flow : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.screenPos = ComputeScreenPos(o.positionHCS);
                o.flow = IN.flow;
                o.fogCoord = ComputeFogFactor(o.positionHCS.z);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            // Height of the waves at a point: two layers sliding along the flow, the second smaller and crossing it.
            float Waves(float2 xz, float2 flow, float t)
            {
                float2 p = xz * _WaveScale;
                float a = Noise(p - flow * _WaveScale * t + float2(0.0, t * 0.07));
                float b = Noise(p * 2.1 + float2(-t * 0.13, t * 0.09) - flow * _WaveScale * 1.6 * t);
                return a * 0.65 + b * 0.35;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / IN.screenPos.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float depth = max(0.0, sceneEye - IN.screenPos.w);

                float t = _Time.y;
                float2 xz = IN.positionWS.xz;

                // The surface's slope from the waves, plus every ring a foot left: a ripple travelling out at 1.2 m/s
                // for 1.6 s, its crest fading as it spreads.
                const float e = 0.15;
                float h0 = Waves(xz, IN.flow, t);
                float hx = Waves(xz + float2(e, 0), IN.flow, t);
                float hz = Waves(xz + float2(0, e), IN.flow, t);
                float2 slope = float2(hx - h0, hz - h0) / e * _WaveTilt;
                float ring = 0.0;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float4 r = _WaterRipples[i];
                    float age = t - r.z;
                    if (r.w <= 0.0 || age < 0.0 || age > 1.6) continue;
                    float2 d = xz - r.xy;
                    float dist = length(d);
                    float front = age * 1.2;
                    float crest = exp(-pow((dist - front) * 7.0, 2.0)) * (1.0 - age / 1.6) * saturate(r.w);
                    ring += crest;
                    slope += (d / max(dist, 0.001)) * crest * 0.9;
                }
                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));

                // Colour by depth; the sky across the water the flatter it is seen.
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fresnel = pow(1.0 - saturate(dot(n, V)), 4.0);
                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / max(0.01, _DeepAt)));
                col = lerp(col, _SkyColor.rgb, fresnel * 0.6);

                Light sun = GetMainLight();
                half3 lit = sun.color * saturate(dot(n, sun.direction)) * 0.5 + SampleSH(n) + 0.35;
                col *= lit;
                float3 H = normalize(sun.direction + V);
                col += sun.color * pow(saturate(dot(n, H)), _Gloss) * 1.4;

                // Foam: a broken band along the shore, and the crest of every ring.
                float foamNoise = Noise(xz * 2.3 - IN.flow * t * 2.3 + t * 0.2);
                float shoreFoam = (1.0 - saturate(depth / max(0.01, _FoamDepth))) * smoothstep(0.35, 0.65, foamNoise);
                float foam = saturate(shoreFoam + ring * 0.8);
                col = lerp(col, _FoamColor.rgb * (0.6 + 0.4 * sun.color), foam);

                // The edge: nothing at the waterline, fading in over the first few centimetres of depth.
                float alpha = lerp(_Opacity * 0.55, _Opacity, saturate(depth / max(0.01, _DeepAt)));
                alpha = max(alpha, foam * 0.9) * saturate(depth / max(0.01, _ShoreFade));

                col = MixFog(col, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
