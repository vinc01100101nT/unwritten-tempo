// THE GRADE GLOW on an item's picture in a window (GradeGlow.cs). One material for every grade: the picture's vertex
// colour carries the glow's colour in rgb and the grade's share of the ladder in alpha (F = 1/6 … A = 1), so every
// glowing square on a canvas batches together. The glow hugs the square's edge, wider, brighter and faster-pulsing
// the higher the grade; grade A adds four white sparkles running round the rim. Everything moves from the clock in
// this shader, so the game's code does nothing per frame for it.
//
// Built like the stock UI shader (stencil for masks, clip rect for RectMask2D, the GUI depth test) so it sits inside
// scrolling grids exactly as the pictures do. Drawn with an Image that has no sprite: its UVs run 0..1 across it.
Shader "Unwritten/GradeGlowUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; };

            float4 _ClipRect;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float level = i.color.a;
                float2 uv = i.uv;
                // 0 on the rim, 0.5 in the middle.
                float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float width = lerp(0.07, 0.2, level);
                float ring = saturate(1.0 - edge / width);
                ring *= ring;
                float pulse = 0.5 + 0.5 * sin(_Time.y * lerp(1.5, 5.0, level));
                float glow = ring * lerp(0.45, 1.0, level) * lerp(0.55, 1.0, pulse);

                float sparkle = 0.0;
                if (level > 0.99)
                {
                    float around = atan2(uv.y - 0.5, uv.x - 0.5) / 6.2831853 + 0.5;
                    for (int k = 0; k < 4; k++)
                    {
                        float at = frac(_Time.y * 0.35 + k * 0.25);
                        float apart = abs(frac(around - at + 0.5) - 0.5);
                        sparkle += saturate(1.0 - apart / 0.025);
                    }
                    sparkle *= saturate(1.0 - edge / 0.06);
                }

                fixed4 c = fixed4(lerp(i.color.rgb, fixed3(1, 1, 1), saturate(sparkle)), saturate(glow + sparkle));
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
