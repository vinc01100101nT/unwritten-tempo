// Opaque, lit GROUND shader for a town's own TILE LIST (any number of tiles, GroundTileArray): its tiles are one texture
// array, and every triangle carries its three corners' tile numbers (uv2) and tilings (uv3) and each point which corner it
// is (vertex colour rgb). Inside one tile's ground it reads ONE texture; where tiles meet, the three corners' tiles blend
// across the triangle exactly as the five-look ground's vertex colours do. Lit and shadowed like Unwritten/GroundBlend,
// with the same depth, shadow and ink-stencil passes.
//
// The world fades a town's edge into its own grass and roads: uv4.x how far into the world's ground (0 the town's own,
// 1 the world's), uv4.y how much of that is road — written by the world builder, onto its copy of the mesh, with the
// world's grass and road in _RimGrassMap and _RimRoadMap on its copy of this material.
Shader "Unwritten/GroundTiles"
{
    Properties
    {
        _Tiles          ("Tiles (texture array)", 2DArray) = "" {}
        _RimGrassMap    ("World Grass (rim)", 2D) = "white" {}
        _RimGrassTiling ("World Grass Tiling (tiles/world-unit)", Float) = 0.4
        _RimRoadMap     ("World Road (rim)", 2D) = "white" {}
        _RimRoadTiling  ("World Road Tiling (tiles/world-unit)", Float) = 0.4
        _Color          ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        // As Unwritten/GroundBlend: drawn after every other solid thing, so the ink's ground stencil lands on visible ground only.
        Tags { "RenderType"="Opaque" "Queue"="Geometry+500" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back
            Stencil { Ref 32 WriteMask 32 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Lib/AdditionalLights.hlsl"

            TEXTURE2D_ARRAY(_Tiles);
            TEXTURE2D(_RimGrassMap);
            TEXTURE2D(_RimRoadMap);
            // Trilinear + repeat whatever the import says, as GroundBlend does (the tiles come in sprite-style, clamped).
            SAMPLER(sampler_trilinear_repeat);
            float _RimGrassTiling, _RimRoadTiling;
            float4 _Color;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // which corner of its triangle the point is: (1,0,0), (0,1,0) or (0,0,1)
                float3 tiles      : TEXCOORD2;  // the triangle's three corner tiles
                float3 tilings    : TEXCOORD3;  // their tilings
                float2 rim        : TEXCOORD4;  // into the world's ground, and how much of it road
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 corner     : TEXCOORD2;
                nointerpolation float3 tiles   : TEXCOORD3;
                nointerpolation float3 tilings : TEXCOORD4;
                float2 rim        : TEXCOORD5;
                float  fogCoord   : TEXCOORD6;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.corner     = IN.color.rgb;
                OUT.tiles      = IN.tiles;
                OUT.tilings    = IN.tilings;
                OUT.rim        = IN.rim;
                OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            // One tile at this point, read with the slope of the ground's own position so a tile read inside a branch
            // still picks the right mipmap.
            half3 Tile(float tile, float tiling, float2 xz, float2 dx, float2 dy)
            {
                return SAMPLE_TEXTURE2D_ARRAY_GRAD(_Tiles, sampler_trilinear_repeat, xz * tiling, tile, dx * tiling, dy * tiling).rgb;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 xz = IN.positionWS.xz, dx = ddx(xz), dy = ddy(xz);
                float3 t = round(IN.tiles);
                half3 albedo = Tile(t.x, IN.tilings.x, xz, dx, dy);
                if (t.y != t.x || t.z != t.x)
                {
                    float3 w = saturate(IN.corner);
                    w /= max(w.x + w.y + w.z, 1e-4);
                    albedo = albedo * w.x + Tile(t.y, IN.tilings.y, xz, dx, dy) * w.y + Tile(t.z, IN.tilings.z, xz, dx, dy) * w.z;
                }
                if (IN.rim.x > 0.001)
                {
                    half3 grass = SAMPLE_TEXTURE2D_GRAD(_RimGrassMap, sampler_trilinear_repeat, xz * _RimGrassTiling, dx * _RimGrassTiling, dy * _RimGrassTiling).rgb;
                    half3 road  = SAMPLE_TEXTURE2D_GRAD(_RimRoadMap,  sampler_trilinear_repeat, xz * _RimRoadTiling,  dx * _RimRoadTiling,  dy * _RimRoadTiling).rgb;
                    albedo = lerp(albedo, lerp(grass, road, saturate(IN.rim.y)), saturate(IN.rim.x));
                }
                albedo *= _Color.rgb;

                // The same light as GroundBlend: Lambert, ambient, the main light's shadow, and the lamps.
                float3 N = normalize(IN.normalWS);
                Light ml = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half  ndl = saturate(dot(N, ml.direction));
                half3 lighting = ml.color * (ndl * ml.shadowAttenuation) + SampleSH(N);
                lighting += ApplyAdditionalLightsDiffuse(IN.positionWS, N, false);
                half3 col = MixFog(albedo * lighting, IN.fogCoord);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V vert (A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag (V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };

            V vert (A i)
            {
                V o;
                float3 wp = TransformObjectToWorld(i.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(i.normalOS);
                float4 pos = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    pos.z = min(pos.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    pos.z = max(pos.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = pos;
                return o;
            }
            half4 frag (V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
