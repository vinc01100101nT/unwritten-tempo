// Wind-swayed foliage for the 3D Pxltgr props (flowers / grass / ferns / bushes). A faithful, flat-lit
// stand-in for the URP/Lit material the props normally use (Lambert main light + soft shadow + SH ambient,
// alpha-clipped foliage), PLUS a vertex-stage WIND BEND so plants lean and flutter with the world wind.
//
// The bend is driven by ONE shader global, _GlobalWind (written once per frame by WindGlobals.cs from the
// same wind that drifts the cloud shadows), so every plant on the map sways the same direction in sync —
// zero per-object CPU cost. The object's pivot (seated at the plant's base by PropPlacement) stays planted;
// sway scales with height above it, so stems root and only the tops wave. World-position phase desyncs
// neighbours so they don't move as one rigid mat.
//
// Applied at GENERATE time by RecipeGenerator: a soft (non-solid) prop group's renderers get a cloned
// material on this shader (Base Map + cutoff copied across), cached per source material so all flowers share
// one material and stay batched. Add to Project Settings ▸ Graphics ▸ Always Included Shaders for builds
// (RecipeGenerator does this automatically when it bakes a swaying map).
Shader "Unwritten/FoliageWind"
{
    Properties
    {
        [MainTexture] _BaseMap   ("Base Map", 2D)        = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color)   = (1,1,1,1)
        _Cutoff      ("Alpha Cutoff", Range(0,1))        = 0.5

        [Header(Wind)]
        _SwayHeight    ("Full-bend height (world units above base)", Float) = 1.0
        _SwayStiffness ("Base stiffness (higher = only the tips move)", Range(0.5, 4)) = 1.6
        _SwayAmount    ("Per-material sway amount", Range(0, 1))             = 0.08
        _SwaySpeed     ("Sway speed", Range(0, 5))                          = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "TransparentCutout"
            "Queue"           = "AlphaTest"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // Shared by every pass: the per-material constants + the wind-bend function (so the lit pass and the
        // shadow caster bend identically and the shadow tracks the plant).
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _Cutoff;
            float  _SwayHeight;
            half   _SwayStiffness;
            half   _SwayAmount;
            half   _SwaySpeed;
        CBUFFER_END

        // Global wind (WindGlobals.cs): xy = unit world-XZ direction, z = strength, w = gust amount.
        float4 _GlobalWind;
        // CPU-accumulated wind clock (WindGlobals.cs). Replaces _Time.y so ramping the storm's speed is a smooth
        // RATE change — multiplying the huge _Time.y by a changing speed popped a ~2s fast-forward at storm onset.
        float  _GlobalWindPhase;

        // Bend a world-space vertex with the wind. The object origin (plant base) stays put; height above it
        // drives the sway, biased by stiffness so the base barely moves and the top waves most.
        float3 ApplyFoliageWind(float3 positionWS)
        {
            float pivotY = unity_ObjectToWorld._m13;                 // world Y of the object's origin (its base)
            float h      = max(positionWS.y - pivotY, 0.0);
            float stiff  = pow(saturate(h / max(_SwayHeight, 1e-3)), _SwayStiffness);

            float2 dir      = _GlobalWind.xy;
            float  strength = _GlobalWind.z * _SwayAmount;
            float  gustAmt  = _GlobalWind.w;                         // storminess: ~0.25 calm → ~1.25 full gale

            // Per-branch desync: vary the wave PHASE strongly across world position (all 3 axes, branch-scale
            // frequency ≈ one full cycle every ~1.8 m) so different branches / leaf clusters lead and lag each
            // other instead of the whole canopy moving as one rigid sheet. Leaves close together stay roughly in
            // step (same branch); a branch away they drift out of phase.
            float phase = dot(positionWS, float3(1.9, 1.3, 2.5));

            // Animation clock from WindGlobals (CPU-accumulated). The storm "beats faster" effect is baked into its
            // RATE there, so here we just scale by the per-material base speed — no _Time.y, no onset pop.
            float t     = _GlobalWindPhase * _SwaySpeed;
            float wave  = sin(t + phase) + 0.35 * sin(t * 2.3 + phase * 1.7);
            float gust  = gustAmt * 0.6 * sin(t * 0.7 + phase * 0.5);
            float bend  = (wave + gust) * strength * stiff;

            // Raindrop patter: in a storm, heavy drops pelt the leaves and knock them DOWN in a quick, choppy
            // vertical jitter — driven only by the storminess above the calm baseline, so a clear day stays flat.
            // Higher frequency than the lean (rain hammers fast) and always biased downward (springs back up).
            float storm  = max(gustAmt - 0.25, 0.0);
            float patter = sin(t * 5.3 + phase * 3.1) + 0.5 * sin(t * 8.7 + phase * 2.3);
            float dropY  = storm * 0.04 * (0.5 + 0.5 * patter);      // ~0 calm → a few cm of downward knock at full storm

            positionWS.xz += dir * bend;
            positionWS.y  -= (abs(bend) * 0.15 + dropY) * stiff;     // bob from the lean + storm raindrop patter
            return positionWS;
        }
        ENDHLSL

        // ---- forward: lambert main light + soft shadow + SH ambient, alpha-clipped ----------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off            // foliage cards are double-sided
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

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
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS);
                posWS = ApplyFoliageWind(posWS);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(tex.a - _Cutoff);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light  mainLight   = GetMainLight(shadowCoord);

                half3 n   = normalize(IN.normalWS);
                // Double-sided: light whichever face points at the sun, so leaf-card backs aren't black.
                half  ndl = max(saturate(dot(n, mainLight.direction)), saturate(dot(-n, mainLight.direction)));

                half3 diffuse = mainLight.color * (ndl * mainLight.shadowAttenuation);
                half3 ambient = SampleSH(n);
                return half4(tex.rgb * (ambient + diffuse), 1.0h);
            }
            ENDHLSL
        }

        // ---- shadow caster: same wind bend so the plant's shadow sways with it --------------------------
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

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
                posWS = ApplyFoliageWind(posWS);
                float3 nWS   = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 shadowFrag (Varyings IN) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).a * _BaseColor.a;
                clip(a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
