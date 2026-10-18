// THE GRADE LOOK on an item's picture in a window (GradeGlow.cs, GradeLookFilm.cs). A RawImage shows its look's cell
// of the one filmed picture; this blends it over the item's picture. The film is drawn over a clear background, so a
// glowing (additive) particle leaves colour where no alpha was written, and light added to a white panel is still
// white — the armour aura vanished on the tooltip that way. So a pixel covers as much as its brightest channel, never
// less than its own alpha, and is blended as premultiplied (One, OneMinusSrcAlpha): a smoke keeps its cover and a glow
// shows its colour on light and dark alike. The picture's vertex alpha (a fading CanvasGroup) fades it as a whole.
//
// Built like the stock UI shader (stencil for masks, clip rect for RectMask2D, the GUI depth test) so it sits inside
// scrolling grids exactly as the pictures do.
Shader "Unwritten/GradeGlowUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Film", 2D) = "black" {}
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
        Blend One OneMinusSrcAlpha
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ClipRect;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                c.a = max(c.a, max(c.r, max(c.g, c.b)));
                float fade = i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                fade *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                c *= fade;
                #ifdef UNITY_UI_ALPHACLIP
                clip(max(c.a, max(c.r, max(c.g, c.b))) - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
