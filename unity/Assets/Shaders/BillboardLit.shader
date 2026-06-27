// Flat-shaded billboard sprite that RECEIVES main-light shadows (so a character walking under a
// tree gets darkened, including partial / half-body shade from the shadowmap) and CASTS a clean
// cutout shadow — without applying N·L directional shading, which would look wrong on a camera-
// facing quad. Pixel-art friendly: alpha-clipped (hard edges, no blending), point filtering kept.
//
// The sprite texture arrives as [PerRendererData] _MainTex from the SpriteRenderer; vertex color
// carries the SpriteRenderer tint. Assigned to player/mobs by Build25DSpike. See the 2.5D depth
// notes: because this renders in the AlphaTest (opaque) queue it writes depth, so sprites also
// z-test against the 3D meshes for free.
//
// _HueShift / _Saturation / _Brightness are per-renderer (MaterialPropertyBlock) HSV knobs driven
// by CharacterDye. All default to identity (no change) so existing sprites are unaffected.
Shader "Unwritten/BillboardLit"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _ShadeFloor ("Shadowed Brightness", Range(0,1)) = 0.45
        // _SelfShadowBias is driven GLOBALLY by SpriteAddLight.cs (Shader.SetGlobalFloat), live-tunable
        // from the DEV panel (P ▸ Shadows) — NOT per-material — so one slider tunes every billboard.

        // Real-time additional-light tuning (_AddLightStrength/_AddLightShape/_AddLightBulge/_AddLightAlbedoTint)
        // is driven GLOBALLY by SpriteAddLight.cs (Shader.SetGlobalFloat), live-tunable from the DEV panel
        // (P ▸ Sprite Light) — NOT per-material — so one set of sliders tunes every billboard at once.

        // HSV dye knobs — set per-renderer via MaterialPropertyBlock (CharacterDye.cs).
        // Identity values: HueShift=0, Saturation=1, Brightness=1.
        [PerRendererData] _HueShift   ("Hue Shift",   Range(0,1)) = 0
        [PerRendererData] _Saturation ("Saturation",  Range(0,2)) = 1
        [PerRendererData] _Brightness ("Brightness",  Range(0,2)) = 1

        // Absolute-hue recolor: when _UseTargetHue > 0.5, every non-protected pixel's hue is SET
        // to _TargetHue (keeping its S/V), computed in gamma space so the result matches the sRGB
        // swatch. Default 0 → legacy _HueShift rotation, so mobs/untouched sprites are unaffected.
        [PerRendererData] _TargetHue    ("Target Hue (absolute)", Range(0,1)) = 0
        [PerRendererData] _TargetSat    ("Target Saturation",     Range(0,1)) = 1
        [PerRendererData] _TargetVal    ("Target Value",          Range(0,1)) = 0.5
        [PerRendererData] _BaseValue    ("Cloth Base Value (midtone anchor)", Range(0.01,1)) = 0.53
        [PerRendererData] _UseTargetHue ("Use Target Hue",        Float)      = 0

        // Palette swap — up to 4 slots (source → destination color replacement).
        // Swaps are applied AFTER HSV so the eyedropper samples the color the user sees.
        // All disabled by default (_SwapEnabled = 0000).
        [PerRendererData] _SwapSrc0 ("Swap Source 0", Color) = (1,1,1,1)
        [PerRendererData] _SwapSrc1 ("Swap Source 1", Color) = (1,1,1,1)
        [PerRendererData] _SwapSrc2 ("Swap Source 2", Color) = (1,1,1,1)
        [PerRendererData] _SwapSrc3 ("Swap Source 3", Color) = (1,1,1,1)
        [PerRendererData] _SwapDst0 ("Swap Dest 0",   Color) = (1,0,0,1)
        [PerRendererData] _SwapDst1 ("Swap Dest 1",   Color) = (1,0,0,1)
        [PerRendererData] _SwapDst2 ("Swap Dest 2",   Color) = (1,0,0,1)
        [PerRendererData] _SwapDst3 ("Swap Dest 3",   Color) = (1,0,0,1)
        [PerRendererData] _SwapEnabled    ("Swap Enabled (xyzw = slots 0-3)", Vector) = (0,0,0,0)
        [PerRendererData] _SwapTolerances ("Swap Tolerances", Vector) = (0.1,0.1,0.1,0.1)

        // Whitelist protect — up to 4 art colors that the HSV dye must NEVER recolor (skin,
        // outline, etc.). Matched against the ORIGINAL (pre-dye) color in GAMMA space, where the
        // protected colors stay well separated (in linear, darks/brights collapse together).
        [PerRendererData] _ProtectColor0 ("Protect Color 0", Color) = (0,0,0,1)
        [PerRendererData] _ProtectColor1 ("Protect Color 1", Color) = (0,0,0,1)
        [PerRendererData] _ProtectColor2 ("Protect Color 2", Color) = (0,0,0,1)
        [PerRendererData] _ProtectColor3 ("Protect Color 3", Color) = (0,0,0,1)
        [PerRendererData] _ProtectEnabled ("Protect Enabled (xyzw = slots 0-3)", Vector) = (0,0,0,0)
        [PerRendererData] _ProtectTol     ("Protect Tolerances", Vector) = (0.12,0.12,0.12,0.12)
    }

    SubShader
    {
        Tags
        {
            "RenderType"   = "TransparentCutout"
            "Queue"        = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // ---- forward: flat art color × main-light shadow attenuation -----------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Cutoff;
                half   _ShadeFloor;
                half   _HueShift;
                half   _Saturation;
                half   _Brightness;
                half   _TargetHue;
                half   _TargetSat;
                half   _TargetVal;
                half   _BaseValue;
                half   _UseTargetHue;
                half4  _SwapSrc0; half4 _SwapSrc1; half4 _SwapSrc2; half4 _SwapSrc3;
                half4  _SwapDst0; half4 _SwapDst1; half4 _SwapDst2; half4 _SwapDst3;
                half4  _SwapEnabled;
                half4  _SwapTolerances;
                half4  _ProtectColor0; half4 _ProtectColor1; half4 _ProtectColor2; half4 _ProtectColor3;
                half4  _ProtectEnabled;
                half4  _ProtectTol;
            CBUFFER_END

            // Global day/night sprite tint, driven by DayNightLight via Shader.SetGlobalColor("_SpriteEnv").
            // NOT in the per-material CBUFFER (it's a Shader-wide global). White = noon (art shown true);
            // a dim cool colour = night, so every billboard (player/mobs/loot) dims & cools after dark
            // WITHOUT N·L shading. DayNightLight seeds it white before the first frame (and in-editor),
            // so sprites are never black when no clock is present.
            half4 _SpriteEnv;

            // Additional-light tuning, set GLOBALLY by SpriteAddLight.cs (live from the DEV panel). Not per-
            // material, so one set of sliders tunes every billboard. Seeded to defaults before the first frame.
            half _AddLightStrength;
            half _AddLightShape;
            half _AddLightBulge;
            half _AddLightAlbedoTint;

            // Distance (world metres) the shadow-receive sample is pushed toward the main light, so the
            // billboard clears its OWN co-located ShadowsOnly proxy (BillboardShadow) instead of sampling
            // it as a dark band that climbs the body. Global (SpriteAddLight.cs), live from P ▸ Shadows.
            half _SelfShadowBias;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            // ---- HSV helpers ------------------------------------------------------------
            // Convert linear RGB → HSV (H in 0-1).
            half3 RGBtoHSV(half3 c)
            {
                half4 K = half4(0.0h, -1.0h/3.0h, 2.0h/3.0h, -1.0h);
                half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half  d = q.x - min(q.w, q.y);
                half  e = 1.0e-10h;
                return half3(abs(q.z + (q.w - q.y) / (6.0h * d + e)), d / (q.x + e), q.x);
            }

            // Convert HSV → linear RGB.
            half3 HSVtoRGB(half3 c)
            {
                half4 K = half4(1.0h, 2.0h/3.0h, 1.0h/3.0h, 3.0h);
                half3 p = abs(frac(c.xxx + K.xyz) * 6.0h - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            // Palette swap: each enabled slot replaces pixels whose color is within tolerance
            // of _SwapSrcN (measured as squared Euclidean distance in RGB, tol range 0-1).
            // Applied AFTER HSV so the DevSettings eyedropper samples the color it sees.
            half3 ApplySwaps(half3 rgb)
            {
                half3 r = rgb;
                half3 d;
                d = rgb - _SwapSrc0.rgb; r = lerp(r, _SwapDst0.rgb, _SwapEnabled.x * step(dot(d,d), _SwapTolerances.x * _SwapTolerances.x * 3.0h));
                d = rgb - _SwapSrc1.rgb; r = lerp(r, _SwapDst1.rgb, _SwapEnabled.y * step(dot(d,d), _SwapTolerances.y * _SwapTolerances.y * 3.0h));
                d = rgb - _SwapSrc2.rgb; r = lerp(r, _SwapDst2.rgb, _SwapEnabled.z * step(dot(d,d), _SwapTolerances.z * _SwapTolerances.z * 3.0h));
                d = rgb - _SwapSrc3.rgb; r = lerp(r, _SwapDst3.rgb, _SwapEnabled.w * step(dot(d,d), _SwapTolerances.w * _SwapTolerances.w * 3.0h));
                return r;
            }

            // Cheap linear→gamma for the protect comparison. URP samples the sRGB sprite as LINEAR,
            // but the protected colors arrive as their authored sRGB values, so we compare in gamma.
            half3 LinToGamma(half3 c) { return pow(max(c, 0.0h), 0.4545h); }
            half3 GammaToLin(half3 c) { return pow(max(c, 0.0h), 2.2h);    }

            // Whitelist protect: if the ORIGINAL (pre-dye) color matches a protected color within
            // tolerance, keep the original instead of the dyed color — so the dye recolors everything
            // EXCEPT the protected set. Exact pixel-art colors + gamma-space separation make this
            // reliable with a small tolerance.
            half3 ApplyProtect(half3 orig, half3 dyed)
            {
                // Uniform per-renderer branch: when no slot is enabled (every mob, and any sprite the
                // dye doesn't touch) skip the gamma conversion + four compares entirely.
                if (!any(_ProtectEnabled)) return dyed;
                half3 g = LinToGamma(orig);
                half3 r = dyed; half3 d;
                d = g - _ProtectColor0.rgb; r = lerp(r, orig, _ProtectEnabled.x * step(dot(d,d), _ProtectTol.x * _ProtectTol.x * 3.0h));
                d = g - _ProtectColor1.rgb; r = lerp(r, orig, _ProtectEnabled.y * step(dot(d,d), _ProtectTol.y * _ProtectTol.y * 3.0h));
                d = g - _ProtectColor2.rgb; r = lerp(r, orig, _ProtectEnabled.z * step(dot(d,d), _ProtectTol.z * _ProtectTol.z * 3.0h));
                d = g - _ProtectColor3.rgb; r = lerp(r, orig, _ProtectEnabled.w * step(dot(d,d), _ProtectTol.w * _ProtectTol.w * 3.0h));
                return r;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;
                clip(tex.a - _Cutoff);

                // HSV dye: shift hue, scale saturation and brightness.
                // All three are identity by default (0, 1, 1) so untouched sprites are free.
                half3 orig = tex.rgb;                  // original art color (pre-dye, linear)
                half3 dyed;
                if (_UseTargetHue > 0.5h)
                {
                    // Recolor to the swatch: apply the swatch's hue + saturation, and take the sprite's
                    // own brightness (gamma V) remapped so the outfit midtone (_BaseValue) lands on the
                    // swatch's value. Result: base = swatch, shadows darker, highlights lighter — coherent
                    // shading. Done in gamma space so the displayed color matches the authored sRGB swatch.
                    half3 hsv = RGBtoHSV(LinToGamma(orig));
                    half  v   = saturate(hsv.z * (_TargetVal / max(_BaseValue, 0.001h)));
                    dyed = GammaToLin(HSVtoRGB(half3(_TargetHue, _TargetSat, v)));
                }
                else
                {
                    half3 hsv = RGBtoHSV(orig);
                    hsv.x = frac(hsv.x + _HueShift);
                    hsv.y = saturate(hsv.y * _Saturation);
                    hsv.z = saturate(hsv.z * _Brightness);
                    dyed  = HSVtoRGB(hsv);
                }

                // Whitelist: protected art colors (skin / outline / etc.) keep their ORIGINAL color.
                dyed = ApplyProtect(orig, dyed);
                tex.rgb = dyed;

                // Palette swap (after HSV so the eyedropper picks the same color the user sees).
                tex.rgb = ApplySwaps(tex.rgb);

                // Per-pixel shadow: each pixel samples the shadowmap at its own world position, so a
                // tree's shadow falls across the sprite accurately (half-body shade). No N·L — art
                // colors stay true in light, dimming toward _ShadeFloor where the shadowmap says shade.
                // Sample slightly toward the light (directional: _MainLightPosition.xyz is the
                // direction TO the light) so the upright quad doesn't catch its own cast shadow,
                // which otherwise reads as a dark band climbing from the feet.
                float3 shadowWS = IN.positionWS + _MainLightPosition.xyz * _SelfShadowBias;
                float4 shadowCoord = TransformWorldToShadowCoord(shadowWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shade = lerp(_ShadeFloor, 1.0h, mainLight.shadowAttenuation);
                // × global env light: noon = white (true art), night = dim & cool. Tracks time of day.
                half3 lit = tex.rgb * shade * _SpriteEnv.rgb;

                // Real-time additional lights (lamps/torches/portals). Added AFTER the day/night env so a
                // warm lamp re-brightens a sprite at night. No N·L (flat billboard); instead a cheap
                // lit-side term brightens the half of the sprite facing the light — the "angle" cue — and
                // the add is only partly tinted by albedo so a near-black charcoal body still glows warm
                // instead of staying black next to the lamp.
                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                if (lightCount > 0u && _AddLightStrength > 0.0h)
                {
                    // Camera basis in world space (the billboard faces the camera, so right/up span the quad
                    // and fwd points back toward the camera). uv runs 0→1, remapped to −1..+1 across the sprite.
                    half3 camRight = (half3)UNITY_MATRIX_I_V._m00_m10_m20;
                    half3 camUp    = (half3)UNITY_MATRIX_I_V._m01_m11_m21;
                    half3 camFwd   = (half3)UNITY_MATRIX_I_V._m02_m12_m22;
                    half  sx = (IN.uv.x - 0.5h) * 2.0h;
                    half  sy = (IN.uv.y - 0.5h) * 2.0h;
                    // Fake outward normal: the flat quad "bulges" toward the camera in the middle and turns to
                    // face sideways at the silhouette, so N·L lights only the half of the sprite facing the
                    // lamp and leaves the far side in shadow (the directional "which side is lit" cue).
                    half3 N = normalize(sx * camRight + sy * camUp + _AddLightBulge * camFwd);
                    half3 add = 0.0h;
                    for (uint li = 0u; li < lightCount; li++)
                    {
                        Light L = GetAdditionalLight(li, IN.positionWS);
                        half atten = L.distanceAttenuation * L.shadowAttenuation;
                        // Half-Lambert (wrap) instead of saturate(N·L): the lamp falls off SMOOTHLY across
                        // the sprite with no hard terminator, so the lit/unlit boundary can't read as a crisp
                        // shadow edge that climbs the body as you move relative to the lamp.
                        half ndl = dot(N, (half3)L.direction) * 0.5h + 0.5h;
                        // _AddLightShape blends flat fill (0 = whole sprite lit) → soft directional (1 = wrapped N·L).
                        add += L.color * (atten * lerp(1.0h, ndl, _AddLightShape));
                    }
                    half3 lampTint = lerp(half3(1.0h, 1.0h, 1.0h), tex.rgb, _AddLightAlbedoTint);
                    lit += lampTint * add * _AddLightStrength;
                }
                #endif

                return half4(lit, 1.0h);
            }
            ENDHLSL
        }

        // ---- shadow caster: clean cutout silhouette ---------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4  _Color;
                half   _Cutoff;
                half   _ShadeFloor;
                half   _HueShift;
                half   _Saturation;
                half   _Brightness;
                half   _TargetHue;
                half   _TargetSat;
                half   _TargetVal;
                half   _BaseValue;
                half   _UseTargetHue;
                half4  _SwapSrc0; half4 _SwapSrc1; half4 _SwapSrc2; half4 _SwapSrc3;
                half4  _SwapDst0; half4 _SwapDst1; half4 _SwapDst2; half4 _SwapDst3;
                half4  _SwapEnabled;
                half4  _SwapTolerances;
                half4  _ProtectColor0; half4 _ProtectColor1; half4 _ProtectColor2; half4 _ProtectColor3;
                half4  _ProtectEnabled;
                half4  _ProtectTol;
            CBUFFER_END

            float3 _LightDirection;

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
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 shadowFrag (Varyings IN) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a * _Color.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
