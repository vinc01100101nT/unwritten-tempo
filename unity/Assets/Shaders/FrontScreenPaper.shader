// THE PAGE LOOK over the front screens' pictures (FrontBackdrop.cs): the world's own paper grain (PaperGrain.hlsl)
// and a soft dark edge around the screen. One full-screen image draws both in one pass, in ink colour.
// _EdgeWidth is the edge's width as a share of the screen across and down, so it is the same distance on every side.
Shader "Unwritten/FrontScreenPaper"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _InkColor ("Ink Color", Color) = (0.08, 0.07, 0.06, 1)
        _PaperStrength ("Paper Grain", Range(0,1)) = 0.3
        _EdgeWidth ("Edge Width (share of screen)", Vector) = (0.02, 0.035, 0, 0)
        _EdgeDarkness ("Edge Darkness", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "PaperGrain.hlsl"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            fixed4 _InkColor;
            float _PaperStrength, _EdgeDarkness;
            float4 _EdgeWidth;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float grain = PaperGrainAlpha(i.vertex.xy, _PaperStrength);
                // 0 at the screen's edge, 1 once a full edge width in, on whichever side is nearest.
                float2 inX = float2(i.uv.x, 1.0 - i.uv.x) / max(_EdgeWidth.x, 1e-5);
                float2 inY = float2(i.uv.y, 1.0 - i.uv.y) / max(_EdgeWidth.y, 1e-5);
                float inward = saturate(min(min(inX.x, inX.y), min(inY.x, inY.y)));
                float edge = (1.0 - smoothstep(0.0, 1.0, inward)) * _EdgeDarkness;
                float a = 1.0 - (1.0 - grain) * (1.0 - edge);
                return fixed4(_InkColor.rgb, a * _InkColor.a * i.color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
