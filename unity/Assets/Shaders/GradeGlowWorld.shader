// THE FLOOR'S GRADE SPHERE (GradeGlow.cs): a sphere of light behind a graded piece lying on the floor. Drawn on a flat
// square that this shader turns to face the camera itself, so the game's code never turns it. One material per grade
// (_Level = the grade's share of the ladder, F = 1/6 … A = 1): a faint inside, a strong bright outline at its edge and a
// soft halo past it, brighter, wider and faster-pulsing the higher the grade; grade A adds four white sparkles circling
// the outline. Premultiplied so it shows on a pale floor as well as a dark one. Depth-tested and in the ordinary
// see-through place, so walls hide it and the piece's picture, drawn after it, sits in front. Never writes depth.
Shader "Unwritten/GradeGlowWorld"
{
    Properties
    {
        _Color ("Colour", Color) = (0.45, 0.82, 1, 1)
        _Level ("Grade share of the ladder", Range(0, 1)) = 0.5
        _TowardCamera ("Pulled toward the camera, share of its size", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Level;
                float _TowardCamera;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 centreWS = TransformObjectToWorld(float3(0, 0, 0));
                float size = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float3 centreVS = TransformWorldToView(centreWS);
                centreVS.z += size * _TowardCamera;
                float3 posVS = centreVS + float3(IN.positionOS.xy * size, 0);
                OUT.positionHCS = TransformWViewToHClip(posVS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 d = IN.uv - 0.5;
                float r = length(d) * 2.0;
                if (r >= 1.0) return half4(0, 0, 0, 0);
                const float rimAt = 0.78;
                float inside = saturate(1.0 - r / rimAt) * 0.45 + 0.18 * step(r, rimAt);
                float rim = exp(-pow((r - rimAt) / lerp(0.045, 0.09, _Level), 2.0));
                float halo = r > rimAt ? saturate(1.0 - (r - rimAt) / (1.0 - rimAt)) * 0.35 : 0.0;
                float pulse = 0.5 + 0.5 * sin(_Time.y * lerp(1.5, 5.0, _Level));
                float bright = lerp(0.55, 1.0, _Level) * lerp(0.7, 1.0, pulse);

                float sparkle = 0.0;
                if (_Level > 0.99)
                {
                    float around = atan2(d.y, d.x) / 6.2831853 + 0.5;
                    for (int k = 0; k < 4; k++)
                    {
                        float at = frac(_Time.y * 0.5 + k * 0.25);
                        float apart = abs(frac(around - at + 0.5) - 0.5);
                        sparkle += saturate(1.0 - apart / 0.04) * saturate(1.0 - abs(r - rimAt) / 0.08);
                    }
                }
                // Premultiplied, and never brighter than it covers: on a pale floor a glow that only ADDS light fades to
                // white and vanishes, so the sky blue replaces what is behind it by as much as it shows — plus a little
                // extra light, so it still glows on a dark one. The outline is the strongest and the least washed out.
                float cover = saturate((inside * 0.8 + halo * 0.8 + rim * 1.4) * bright + sparkle);
                float3 tint = lerp(_Color.rgb, float3(1, 1, 1), saturate(sparkle));
                return half4(tint * cover * 1.15, cover * 0.95);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
