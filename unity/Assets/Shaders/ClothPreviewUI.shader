// UI-compatible sprite shader for the character customise preview.
// Mirrors the HSV-dye + whitelist-protect logic from BillboardLit so the in-panel
// preview looks identical to the in-game character. Pure UI (no shadow passes).
// Unity's stencil block is included so it works inside masked Scroll Views / panels.
Shader "Unwritten/ClothPreviewUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        // ── HSV dye (identical semantics to BillboardLit) ──────────────
        _HueShift   ("Hue Shift",  Range(0,1)) = 0
        _Saturation ("Saturation", Range(0,2)) = 1
        _Brightness ("Brightness", Range(0,2)) = 1

        // ── Absolute-hue recolor ───────────────────────────────────────
        // When _UseTargetHue > 0.5, every non-protected pixel's hue is SET to _TargetHue
        // (keeping its own S/V so shading survives), worked out in GAMMA space so the result
        // hue equals the authored sRGB swatch exactly. This replaces the old hue-rotation,
        // which smeared a multi-hue outfit across the wheel. Off (0) = legacy _HueShift path.
        _TargetHue    ("Target Hue (absolute)", Range(0,1)) = 0
        _TargetSat    ("Target Saturation",     Range(0,1)) = 1
        _TargetVal    ("Target Value",          Range(0,1)) = 0.5
        _BaseValue    ("Cloth Base Value (midtone anchor)", Range(0.01,1)) = 0.53
        _UseTargetHue ("Use Target Hue",        Float)      = 0

        // ── Whitelist protect — up to 4 art colors that the dye must NOT recolor.
        //    Same property names as BillboardLit so the same values drive both shaders.
        _ProtectColor0 ("Protect Color 0", Color) = (0,0,0,1)
        _ProtectColor1 ("Protect Color 1", Color) = (0,0,0,1)
        _ProtectColor2 ("Protect Color 2", Color) = (0,0,0,1)
        _ProtectColor3 ("Protect Color 3", Color) = (0,0,0,1)
        _ProtectEnabled ("Protect Enabled (xyzw = slots 0-3)", Vector) = (0,0,0,0)
        _ProtectTol     ("Protect Tolerances",                 Vector) = (0.12,0.12,0.12,0.12)

        // ── Unity UI mask stencil ───────────────────────────────────────
        [HideInInspector] _StencilComp      ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil          ("Stencil ID",         Float) = 0
        [HideInInspector] _StencilOp        ("Stencil Operation",  Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask  ("Stencil Read Mask",  Float) = 255
        [HideInInspector] _ColorMask        ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull     Off
        Lighting Off
        ZWrite   Off
        ZTest    [unity_GUIZTestMode]
        Blend    SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _ClipRect;

            half  _HueShift, _Saturation, _Brightness;
            half  _TargetHue, _UseTargetHue, _TargetSat, _TargetVal, _BaseValue;
            half4 _ProtectColor0, _ProtectColor1, _ProtectColor2, _ProtectColor3;
            half4 _ProtectEnabled, _ProtectTol;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex   = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color    = v.color;
                return o;
            }

            // ── HSV helpers (same implementation as BillboardLit) ──────────
            half3 RGBtoHSV(half3 c)
            {
                half4 K = half4(0, -1.0h/3.0h, 2.0h/3.0h, -1.0h);
                half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half  d = q.x - min(q.w, q.y);
                half  e = 1.0e-10h;
                return half3(abs(q.z + (q.w - q.y) / (6.0h * d + e)), d / (q.x + e), q.x);
            }

            half3 HSVtoRGB(half3 c)
            {
                half4 K = half4(1.0h, 2.0h/3.0h, 1.0h/3.0h, 3.0h);
                half3 p = abs(frac(c.xxx + K.xyz) * 6.0h - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            half3 LinToGamma(half3 c) { return pow(max(c, 0.0h), 0.4545h); }
            half3 GammaToLin(half3 c) { return pow(max(c, 0.0h), 2.2h);    }

            // Whitelist protect (same formula as BillboardLit.ApplyProtect): keep the ORIGINAL
            // colour for pixels that match a protected colour, compared in gamma space.
            half3 ApplyProtect(half3 orig, half3 dyed)
            {
                if (!any(_ProtectEnabled)) return dyed;   // uniform branch — skip when nothing protected
                half3 g = LinToGamma(orig);
                half3 r = dyed; half3 d;
                d = g - _ProtectColor0.rgb; r = lerp(r, orig, _ProtectEnabled.x * step(dot(d,d), _ProtectTol.x * _ProtectTol.x * 3.0h));
                d = g - _ProtectColor1.rgb; r = lerp(r, orig, _ProtectEnabled.y * step(dot(d,d), _ProtectTol.y * _ProtectTol.y * 3.0h));
                d = g - _ProtectColor2.rgb; r = lerp(r, orig, _ProtectEnabled.z * step(dot(d,d), _ProtectTol.z * _ProtectTol.z * 3.0h));
                d = g - _ProtectColor3.rgb; r = lerp(r, orig, _ProtectEnabled.w * step(dot(d,d), _ProtectTol.w * _ProtectTol.w * 3.0h));
                return r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.texcoord) * i.color;

                half3 orig = col.rgb;                 // original art colour (pre-dye, linear)
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

                // Restore the four protected colours so they are never re-tinted.
                col.rgb = ApplyProtect(orig, dyed);

                // UI clipping mask.
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                clip(col.a - 0.001h);

                return col;
            }
            ENDCG
        }
    }
}
