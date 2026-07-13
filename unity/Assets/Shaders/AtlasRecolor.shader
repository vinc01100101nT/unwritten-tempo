// Fullscreen blit that recolors a Quaternius character atlas PER CLOTH PART, then is assigned back to the
// renderer's _BaseMap (URP Lit keeps doing the lighting). This is the INVERSE of the ninja protect-list:
// a pixel is recolored ONLY if it matches one of the listed SOURCE part-colors, in which case its hue+sat
// become that part's TARGET while its own brightness (shading) is preserved. Skin, eyes and any unlisted
// color simply pass through untouched — so "everything except skin & eyes" is automatic.
//
// Color matching is done in GAMMA space (authored sRGB), the same convention ClothPreviewUI proved correct
// under the project's linear color space — pass _Src/_Dst as authored sRGB Colors.
Shader "Unwritten/AtlasRecolor"
{
    Properties { _MainTex ("Atlas", 2D) = "white" {} }

    SubShader
    {
        Cull Off  ZWrite Off  ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define MAXPARTS 8

            sampler2D _MainTex;
            float4    _MainTex_ST;
            half4     _Src[MAXPARTS];   // authored sRGB source part-colors
            half4     _Dst[MAXPARTS];   // authored sRGB chosen colors
            int       _Count;
            half      _Tol;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

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

            // TRUE piecewise sRGB transfer (not the pow-2.2 approximation). The approximation diverges most
            // on DARK colors — enough to land a sampled #1F1F1F at ~#242424, i.e. CLOSER to a neighboring
            // #272727 part than to its own source — which silently reassigns a dark part's pixels to the
            // neighbor's Voronoi cell (dye "does nothing" / neighbor over-applies). The exact curve
            // reconstructs each texel to its authored byte value, so matching is exact per hex.
            half3 LinToGamma(half3 c)
            {
                c = max(c, 0.0h);
                return lerp(12.92h * c, 1.055h * pow(c, 1.0h / 2.4h) - 0.055h, step(0.0031308h, c));
            }
            half3 GammaToLin(half3 c)
            {
                c = max(c, 0.0h);
                return lerp(c / 12.92h, pow((c + 0.055h) / 1.055h, 2.4h), step(0.04045h, c));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 col = tex2D(_MainTex, i.uv);
                half3 g    = LinToGamma(col.rgb);        // sampled → authored sRGB space
                half  tol2 = _Tol * _Tol * 3.0h;

                // Assign this pixel to its NEAREST listed part (Voronoi), not the first within tol. _Src lists
                // EVERY detected part — dyed or not — so each part claims its own pixels and no part can bleed
                // into a neighbor that owns a swatch. Recolor only if that nearest part is actually dyed
                // (_Dst != _Src); undyed parts (and anything past tol, e.g. skin/eyes) pass through untouched.
                int  best  = -1;
                half bestD = 1.0e9h;
                [loop]
                for (int k = 0; k < _Count; k++)
                {
                    half3 d = g - _Src[k].rgb;
                    half  dd = dot(d, d);
                    if (dd < bestD) { bestD = dd; best = k; }
                }

                if (best >= 0 && bestD <= tol2)
                {
                    half3 ss = _Src[best].rgb, ds = _Dst[best].rgb;
                    half3 dd2 = ds - ss;
                    if (dot(dd2, dd2) > 1.0e-6h)   // part is dyed → recolor; else leave the pixel as-is
                    {
                        // Keep the pixel's own brightness relative to the part's base value, so folds/shading
                        // survive: base part-color → chosen color, shadows darker, highlights lighter.
                        half3 srcHSV = RGBtoHSV(ss);
                        half3 dstHSV = RGBtoHSV(ds);
                        half3 pixHSV = RGBtoHSV(g);
                        half  v      = saturate(pixHSV.z * (dstHSV.z / max(srcHSV.z, 0.001h)));
                        col.rgb = GammaToLin(HSVtoRGB(half3(dstHSV.x, dstHSV.y, v)));
                    }
                }
                return col;
            }
            ENDCG
        }
    }
}
