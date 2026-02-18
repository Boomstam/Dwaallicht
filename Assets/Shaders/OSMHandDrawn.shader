Shader "OSM/HandDrawn"
{
    Properties
    {
        _MainTex ("Map Tile", 2D) = "white" {}

        [Header(Paper and Ink)]
        _PaperColor ("Paper Color", Color) = (0.96, 0.93, 0.85, 1.0)
        _InkColor ("Ink / Line Color", Color) = (0.12, 0.08, 0.04, 1.0)
        _EdgeStrength ("Edge (Ink Line) Strength", Range(0, 5)) = 1.8
        _EdgeThreshold ("Edge Detection Threshold", Range(0, 1)) = 0.15

        [Header(Color Style)]
        _Saturation ("Color Saturation", Range(0, 2)) = 0.45
        _Brightness ("Brightness", Range(0.5, 2)) = 1.05
        _WarmTint ("Warm Tint Amount", Range(0, 1)) = 0.18
        _PaperBlend ("Paper Blend (0=full color, 1=paper only)", Range(0, 1)) = 0.12

        [Header(Pencil Texture)]
        _PencilScale ("Pencil Noise Scale", Range(10, 500)) = 180.0
        _PencilStrength ("Pencil Grain Strength", Range(0, 1)) = 0.28
        _HatchStrength ("Hatching Strength", Range(0, 1)) = 0.12
        _HatchScale ("Hatching Scale", Range(10, 200)) = 60.0

        [Header(Wobbly Lines)]
        _WobbleStrength ("UV Wobble Strength", Range(0, 0.02)) = 0.004
        _WobbleScale ("UV Wobble Scale", Range(1, 50)) = 12.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            float4    _MainTex_TexelSize;

            float4 _PaperColor;
            float4 _InkColor;
            float  _EdgeStrength;
            float  _EdgeThreshold;
            float  _Saturation;
            float  _Brightness;
            float  _WarmTint;
            float  _PaperBlend;
            float  _PencilScale;
            float  _PencilStrength;
            float  _HatchStrength;
            float  _HatchScale;
            float  _WobbleStrength;
            float  _WobbleScale;

            float hash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash(i + float2(0,0));
                float b = hash(i + float2(1,0));
                float c = hash(i + float2(0,1));
                float d = hash(i + float2(1,1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                for (int i = 0; i < 5; i++)
                {
                    v += amp * valueNoise(p * freq);
                    freq *= 2.1;
                    amp  *= 0.5;
                }
                return v;
            }

            float sobelEdge(sampler2D tex, float2 uv, float2 texelSize)
            {
                float tl = Luminance(tex2D(tex, uv + texelSize * float2(-1, 1)).rgb);
                float t  = Luminance(tex2D(tex, uv + texelSize * float2( 0, 1)).rgb);
                float tr = Luminance(tex2D(tex, uv + texelSize * float2( 1, 1)).rgb);
                float l  = Luminance(tex2D(tex, uv + texelSize * float2(-1, 0)).rgb);
                float r  = Luminance(tex2D(tex, uv + texelSize * float2( 1, 0)).rgb);
                float bl = Luminance(tex2D(tex, uv + texelSize * float2(-1,-1)).rgb);
                float b  = Luminance(tex2D(tex, uv + texelSize * float2( 0,-1)).rgb);
                float br = Luminance(tex2D(tex, uv + texelSize * float2( 1,-1)).rgb);

                float gx = -tl - 2.0*l - bl + tr + 2.0*r + br;
                float gy = -tl - 2.0*t - tr + bl + 2.0*b + br;
                return sqrt(gx*gx + gy*gy);
            }

            float hatching(float2 uv, float scale, float angle)
            {
                float2 rotUV;
                float s = sin(angle), c = cos(angle);
                rotUV.x = uv.x * c - uv.y * s;
                rotUV.y = uv.x * s + uv.y * c;
                float lines = abs(sin(rotUV.x * scale * 3.14159));
                return pow(lines, 8.0);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // 1. Wobble UVs
                float2 wobbleUV = uv * _WobbleScale;
                float wobbleX = fbm(wobbleUV + float2(1.7, 9.2)) - 0.5;
                float wobbleY = fbm(wobbleUV + float2(8.3, 2.8)) - 0.5;
                float2 warpedUV = uv + float2(wobbleX, wobbleY) * _WobbleStrength;

                // 2. Sample tile
                float4 col = tex2D(_MainTex, warpedUV);

                // 3. Desaturate + color style
                float lum = Luminance(col.rgb);
                float3 desat = lerp(col.rgb, float3(lum, lum, lum), 1.0 - _Saturation);
                float3 warmTint = float3(1.0, 0.92, 0.78);
                desat = lerp(desat, desat * warmTint, _WarmTint);
                desat *= _Brightness;
                desat = lerp(desat, _PaperColor.rgb, _PaperBlend);

                // 4. Pencil grain
                float grain = fbm(uv * _PencilScale);
                desat = desat - (grain - 0.5) * _PencilStrength * 0.15;

                // 5. Hatching
                float darkness = 1.0 - lum;
                float hatch1 = hatching(uv, _HatchScale, 0.785);
                float hatch2 = hatching(uv, _HatchScale * 0.9, -0.392);
                float hatchMask = max(hatch1, hatch2 * 0.6);
                float hatchContrib = hatchMask * saturate(darkness - 0.2) * _HatchStrength;
                desat = lerp(desat, _InkColor.rgb, hatchContrib);

                // 6. Sobel edge -> ink lines
                float2 texelSize = _MainTex_TexelSize.xy;
                float edge = sobelEdge(_MainTex, warpedUV, texelSize);
                edge = smoothstep(_EdgeThreshold, _EdgeThreshold + 0.15, edge);
                float edgeNoise = valueNoise(uv * 300.0);
                edge *= lerp(0.7, 1.0, edgeNoise);
                edge = saturate(edge * _EdgeStrength);
                float3 finalColor = lerp(desat, _InkColor.rgb, edge);

                // 7. Paper texture + vignette
                float paper = fbm(uv * 420.0) * 0.5 + 0.5;
                finalColor += (paper - 0.5) * 0.04;
                float2 borderUV = abs(uv - 0.5) * 2.0;
                float vignette = 1.0 - smoothstep(0.85, 1.0, max(borderUV.x, borderUV.y));
                finalColor *= vignette * 0.05 + 0.95;
                finalColor = saturate(finalColor);

                fixed4 result = fixed4(finalColor, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, result);
                return result;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}