// HAND-DRAWN colour grade. A fullscreen BLIT material run by HandDrawnGradeFeature: it samples the whole camera
// image (_BlitTexture) and rewrites each pixel's colour — lowering SATURATION (toward its own luminance, never to
// pure grey) and nudging CONTRAST. Unlike an alpha-blended overlay, a blit can actually READ and MODIFY the scene
// colour, so this is what desaturates the world AND the sprite characters together. Driven by HandDrawnFX (statics).
//
// Pass 0 = the grade. Pass 1 = a plain copy, used to write the graded temp back into the real camera-colour target
// (so the Render Graph keeps the work — reassigning cameraColor can get culled when post-processing is off, which
// shows up as a fully BLACK screen).
Shader "Unwritten/HandDrawnGrade"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        // ---- Pass 0: saturation + contrast grade ----
        Pass
        {
            Name "HandDrawnGrade"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Provides the fullscreen Vert, the Varyings struct (with .texcoord), _BlitTexture + sampler_LinearClamp.
            // (Core 17.x moved Blit.hlsl from ShaderLibrary/ to Runtime/Utilities/.)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Saturation;   // 1 = unchanged, <1 = muted, 0 = grey (only while the player is dead; see HandDrawnFX.DeathGrey)
            float _Contrast;     // 0 = unchanged; small +/- shifts around mid-grey

            half4 frag (Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, IN.texcoord);

                // Saturation: lerp each pixel toward its OWN luminance (Rec.709). The art style's mute keeps a tint;
                // only a death takes it to 0, which is pure greyscale.
                float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
                col.rgb = lerp(luma.xxx, col.rgb, _Saturation);

                // Contrast around mid-grey.
                col.rgb = (col.rgb - 0.5) * (1.0 + _Contrast) + 0.5;

                return col;
            }
            ENDHLSL
        }

        // ---- Pass 1: plain copy (temp → camera colour) ----
        Pass
        {
            Name "HandDrawnGradeCopy"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 frag (Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, IN.texcoord);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
