// Full-screen cloud-shadow multiply. A screen-covering quad (placed by CloudShadows.cs) reads the scene
// DEPTH texture, reconstructs each pixel's true WORLD position, and MULTIPLY-darkens by the cloud texture
// sampled in world X/Z — so the pattern is pinned to the world (drifts only by the scroll offset, never
// rotating with the sun) and now falls on EVERYTHING the depth buffer holds: ground, houses, props.
//
// Why a screen quad instead of the old ground plane: the old plane was flat, so it only ever overlapped
// the ground — houses stuck up out of it and stayed unshaded. By reconstructing world pos from depth we
// project the clouds straight DOWN onto whatever surface is actually there (roof, wall, rock) at that XZ.
//
// Sky pixels (depth at the far plane) are left untouched. Drawn in the transparent queue with the multiply
// blend; billboards/characters render after this (queue 3000) so they stay untinted, matching the original.
// Occlusion is handled per-pixel from the sampled depth, so ZTest is Always and the quad fills the screen.
Shader "Unwritten/CloudShadow"
{
    Properties
    {
        _CloudTex ("Cloud", 2D) = "white" {}
        _Scale ("World Scale (1/patchSize)", Float) = 0.02
        _Strength ("Strength", Range(0,1)) = 1
        _ScrollX ("Scroll X", Float) = 0
        _ScrollY ("Scroll Y", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero      // multiply: result = dst * src
            ZWrite Off
            ZTest Always             // full-screen overlay; per-pixel occlusion comes from sampled depth
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);
            float _Scale;
            float _Strength;
            float _ScrollX;
            float _ScrollY;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            // The mesh verts span [-1,1] in object XY; emit them straight to clip space so the quad always
            // fills the screen regardless of the object's transform (CloudShadows gives it huge bounds so it
            // is never frustum-culled).
            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = float4(IN.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);
                float depth = SampleSceneDepth(uv);

                // Sky / nothing rendered: leave the pixel as-is (white = multiply by 1).
                if (depth == UNITY_RAW_FAR_CLIP_VALUE) return half4(1.0h, 1.0h, 1.0h, 1.0h);

                float3 wp = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float2 cuv = wp.xz * _Scale + float2(_ScrollX, _ScrollY);   // world-pinned cloud UVs

                half c   = SAMPLE_TEXTURE2D(_CloudTex, sampler_CloudTex, cuv).r;  // 1 = clear, <1 = shadow
                half lum = lerp(1.0h, c, _Strength);                              // master fade
                return half4(lum, lum, lum, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
