// PROCEDURAL SKYBOX — wraps the whole scene in a seamless sphere of sky (no flat plane, no edges, no corners).
// Rendered by Unity's DrawSkybox pass for any camera whose clearFlags = Skybox (main + the planar-reflection
// mirror cam, so puddles reflect the sky too). Everything is computed PER VIEW DIRECTION (the view ray), which is
// mathematically a sphere of directions — so it's edge-free and infinitely far, never reachable.
//
// Per-pixel: (1) a horizon→zenith gradient, (2) a sun disk + soft halo along _SunDir, (3) two cloud layers
// at different virtual altitudes (high cirrus + low cumulus) with domain warping to break tiling. Both layers
// reuse CloudShadows' procedural cloud texture. SkyboxSky.cs drives all params from the TimeOfDay clock, the
// Sun, and the weather Cloud/Rain channels.
Shader "Unwritten/SkyboxClouds"
{
    Properties
    {
        _CloudTex     ("Cloud Texture (R: 1=clear, low=cloud)", 2D) = "white" {}
        _HorizonColor ("Horizon Color", Color) = (0.55, 0.70, 0.85, 1)
        _ZenithColor  ("Zenith Color",  Color) = (0.20, 0.42, 0.78, 1)
        _GradPower    ("Gradient Power", Range(0.2, 4)) = 1.4
        _SunDir       ("Sun Direction (world)", Vector) = (0, 0.7, 0.7, 0)
        _SunColor     ("Sun Color", Color) = (1, 0.95, 0.85, 1)
        _SunSharp     ("Sun Disk Sharpness", Float) = 600
        _SunGlow      ("Sun Halo Sharpness", Float) = 12
        _SunIntensity ("Sun Intensity", Range(0, 3)) = 1
        _MoonDir      ("Moon Direction (world)", Vector) = (0, -0.7, -0.7, 0)
        _MoonColor    ("Moon Color", Color) = (0.78, 0.85, 1, 1)
        _MoonSharp    ("Moon Disk Sharpness", Float) = 2600
        _MoonGlow     ("Moon Halo Sharpness", Float) = 8
        _MoonIntensity("Moon Intensity", Range(0, 3)) = 0
        _CloudColor   ("Cloud Color", Color) = (1, 1, 1, 1)
        _LightTint    ("Day/Night Cloud Tint", Color) = (1, 1, 1, 1)
        _CloudScale   ("Cloud Scale", Float) = 0.04
        _ScrollX      ("Scroll X", Float) = 0
        _ScrollY      ("Scroll Y", Float) = 0
        _Density      ("Cloud Density", Range(0, 4)) = 1.4
        _SoftEdge     ("Cloud Edge Softness", Range(0.001, 1)) = 0.55
        _HorizonFade  ("Cloud Horizon Fade", Range(0.001, 1)) = 0.22
        _Strength     ("Cloud Cover (master)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudTex); SAMPLER(sampler_CloudTex);
            float4 _HorizonColor, _ZenithColor, _SunColor, _SunDir, _CloudColor, _LightTint;
            float4 _MoonColor, _MoonDir;
            float  _GradPower, _SunSharp, _SunGlow, _SunIntensity;
            float  _MoonSharp, _MoonGlow, _MoonIntensity;
            float  _CloudScale, _ScrollX, _ScrollY, _Density, _SoftEdge, _HorizonFade, _Strength;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;   // skybox box corner = view direction
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);

                // (1) Sky gradient: horizon → zenith. pow() bias keeps the deep blue near the top.
                float  up  = saturate(d.y);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, _GradPower));

                // (2) Sun: tight disk + soft halo, faded out at night via _SunIntensity.
                float  sd   = saturate(dot(d, normalize(_SunDir.xyz)));
                float  disk = pow(sd, _SunSharp);
                float  halo = pow(sd, _SunGlow);
                sky += _SunColor.rgb * (disk + halo * 0.35) * _SunIntensity;

                // (2b) Moon: a crisp disk + cool halo opposite the sun, faded IN at night via _MoonIntensity.
                // SkyboxSky feeds _MoonDir = -_SunDir, so the moon rides up exactly as the sun sets.
                float  mn    = saturate(dot(d, normalize(_MoonDir.xyz)));
                float  mdisk = pow(mn, _MoonSharp);
                float  mhalo = pow(mn, _MoonGlow);
                sky += _MoonColor.rgb * (mdisk + mhalo * 0.25) * _MoonIntensity;

                // (3) Clouds — TWO LAYERS at different virtual altitudes (high cirrus + low cumulus),
                //     with domain warping to break the horizontal-stripe tiling the bare gnomonic
                //     projection (d.xz / d.y) produces when the same texture tile repeats.
                //
                //     y-floor of 0.06: limits max UV stretch to ~17× near the horizon (was 1e-3 →
                //     1000×, which caused the aliasing stripe band). The horizon-fade smoothstep
                //     already hides pixels below this angle, so the clamp is invisible.

                float y = max(d.y, 0.06);

                // ── Domain warp: two offset samples at ~38% of cloud frequency build a 2D
                //    displacement field. It organically distorts the primary UVs, turning the
                //    regular stripe tile into varied puffs. Magnitude ±0.14 UV units. ─────────────
                float2 warpBase = (d.xz / y) * (_CloudScale * 0.38)
                                + float2(_ScrollX * 0.55, _ScrollY * 0.55);
                float  wA   = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, warpBase + float2(0.31, 0.17)).r;
                float  wB   = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, warpBase + float2(0.73, 0.59)).r;
                float2 warp = float2(wA, wB) * 0.28 - 0.14;

                // ── HIGH clouds (cirrus-like): thin, fast, bright. ────────────────────────────────
                //    Virtual altitude above the low layer: a larger y divisor compresses the
                //    projection, so these clouds spread less at a given elevation angle — they appear
                //    as if they're sitting higher up / farther away. ──────────────────────────────
                float  y1  = max(d.y * 1.45, 0.06);
                float2 uv1 = (d.xz / y1) * _CloudScale
                           + float2(_ScrollX, _ScrollY) + warp * 0.55;
                float  t1  = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv1).r;
                float  c1  = smoothstep(0.0, _SoftEdge, saturate((1.0 - t1) * _Density * 0.75));

                // ── LOW clouds (cumulus-like): puffier, broader, slightly darker underneath. ──────
                //    Smaller y divisor = more spreading at the same angle = appears lower/closer.
                //    Rotated ~27° to break visual alignment with layer 1 (eliminates tile seams). ──
                float  y2   = max(d.y * 0.62, 0.06);
                float2 dxzR = float2(d.xz.x * 0.891 - d.xz.y * 0.454,   // ~27° rotation
                                     d.xz.x * 0.454 + d.xz.y * 0.891);
                float2 uv2  = (dxzR / y2) * (_CloudScale * 0.60)
                            + float2(_ScrollX * 0.72, _ScrollY * 0.72) + warp;
                float  t2   = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, uv2).r;
                float  c2   = smoothstep(0.0, _SoftEdge * 1.25, saturate((1.0 - t2) * _Density));

                // ── Combine: lit tops (cirrus = sun-bleached bright), shadowed bases (cumulus
                //    underside = dark). Where both layers overlap → dense column core, pulled toward
                //    the darker underside colour. ─────────────────────────────────────────────────
                float3 cloudCol  = _CloudColor.rgb * _LightTint.rgb;
                float3 highCol   = cloudCol * 1.08;     // cirrus: bleached by direct sun
                float3 lowCol    = cloudCol * 0.70;     // cumulus: shadow underside

                float  shadow    = saturate(c2 * 1.5);
                float3 finalCol  = lerp(highCol, lowCol, shadow * 0.50);

                // Soft-additive blend: avoids double-counting when both layers are fully opaque.
                float total = saturate(c1 * 0.72 + c2 * 0.88 - c1 * c2 * 0.50);

                // Fade everything into the sky near the horizon (projection pile-up).
                total *= smoothstep(0.0, _HorizonFade, d.y);

                sky = lerp(sky, finalCol, saturate(total * _Strength));

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
