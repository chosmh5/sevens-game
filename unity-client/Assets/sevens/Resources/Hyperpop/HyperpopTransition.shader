// 하이퍼팝 화면 전환 / 하이라이트 펄스 — HyperpopTransition.cs 가 화면 맨 위 오버레이 캔버스에서 그린다.
// 화면 캡처(_MainTex)와 절차적 도형만 쓰므로 Built-in / URP 공통.
//
//  _Phase 0(OUT) : 이전 화면 → 덮개.   _P 0→1, P=1 이면 화면 전체가 덮개(잉크 또는 흰색).
//  _Phase 1(IN)  : 덮개 → 새 화면.     _P 0→1, 덮개가 걷히며 아래의 새 씬이 드러난다(알파).
//  _Phase 2      : 하이라이트 펄스(화면 위아래 가장자리 글리치 바 + 양옆 색종이). 가운데(카드 영역)는 비운다.
//
//  _Style 0 Blocks : 색 블록이 도장처럼 찍히며 덮고, 흩어지며 걷힌다
//         1 Melt   : 화면이 녹아내리듯 번지고 픽셀이 커지다 흰 화면 → 체커·무지개 칸으로 부서지며 걷힌다
//         2 Bars   : 가로 바가 좌우에서 밀려 들어와 덮고, 반대편으로 빠지며 걷힌다(머리 부분 체커/무지개)
//         3 Tiles  : 화면이 사진 창(타일)으로 쪼개져 줄어들고, 검은 화면의 창들이 커지며 새 화면이 드러난다
//         4 Fade   : 동작 줄이기 모드 — 단순 페이드
Shader "Sevens/HyperpopTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Snapshot", 2D) = "white" {}
        _Photo0 ("Photo 0", 2D) = "white" {}
        _Photo1 ("Photo 1", 2D) = "white" {}
        _PhotoCount ("Photo Count", Float) = 0
        _Photo0ST ("Photo 0 Fit", Vector) = (1,1,0,0)
        _Photo1ST ("Photo 1 Fit", Vector) = (1,1,0,0)

        _P ("Progress", Range(0,1)) = 0
        _Phase ("Phase", Float) = 0
        _Style ("Style", Float) = 0
        _Seed ("Seed", Float) = 0
        _Res ("Screen Size (script)", Vector) = (1920,1080,0,0)

        _Ink ("Ink", Color) = (0.051,0.043,0.094,1)
        _Paper ("Paper", Color) = (0.98,0.97,0.95,1)
        _C0 ("Pink", Color) = (1.00,0.33,0.62,1)
        _C1 ("Cyan", Color) = (0.20,0.90,0.96,1)
        _C2 ("Lime", Color) = (0.72,0.96,0.30,1)
        _C3 ("Yellow", Color) = (1.00,0.88,0.30,1)
        _C4 ("Blue", Color) = (0.36,0.45,1.00,1)

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        ZWrite Off
        Lighting Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex, _Photo0, _Photo1;
            float4 _Photo0ST, _Photo1ST;
            float _PhotoCount, _P, _Phase, _Style, _Seed;
            float4 _Res;
            float4 _Ink, _Paper, _C0, _C1, _C2, _C3, _C4;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // ── 유틸 ─────────────────────────────────────────────
            float hash12 (float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float2 hash22 (float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }
            float EaseOut (float x) { x = saturate(x); float k = 1.0 - x; return 1.0 - k * k * k; }

            float3 Ramp (float x)        // 핑크 → 노랑 → 라임 → 시안 → 파랑 → 핑크
            {
                x = frac(x) * 5.0;
                float3 c = lerp(_C0.rgb, _C3.rgb, saturate(x));
                c = lerp(c, _C2.rgb, saturate(x - 1.0));
                c = lerp(c, _C1.rgb, saturate(x - 2.0));
                c = lerp(c, _C4.rgb, saturate(x - 3.0));
                c = lerp(c, _C0.rgb, saturate(x - 4.0));
                return c;
            }
            float3 Pal (float h)         // 납작한 단색 블록용
            {
                float i = floor(saturate(h) * 4.999);
                return i < 1 ? _C1.rgb : i < 2 ? _C0.rgb : i < 3 ? _C2.rgb : i < 4 ? _C3.rgb : _C4.rgb;
            }
            float3 Checker (float2 px, float s)
            {
                float c = fmod(floor(px.x / s) + floor(px.y / s), 2.0);
                return lerp(float3(1, 1, 1), float3(0.80, 0.80, 0.80), c);
            }
            float3 Snap (float2 uv) { return tex2Dlod(_MainTex, float4(uv, 0, 0)).rgb; }

            float3 PhotoAt (float2 uv, float h)
            {
                if (_PhotoCount < 0.5)
                    return _Phase < 0.5 ? Snap(frac(uv + float2(h * 0.53, h * 0.31))) : Ramp(uv.y * 0.8 + uv.x * 0.4 + h);
                if (_PhotoCount > 1.5 && h > 0.5)
                    return tex2Dlod(_Photo1, float4(uv * _Photo1ST.xy + _Photo1ST.zw, 0, 0)).rgb;
                return tex2Dlod(_Photo0, float4(uv * _Photo0ST.xy + _Photo0ST.zw, 0, 0)).rgb;
            }

            // 이전 화면 + 가로 찢김 + RGB 분리 + 무지개 색 번짐 (g: 0~1 세기)
            float3 GlitchSnap (float2 uv, float2 px, float unit, float g, float tick)
            {
                float row = floor(px.y / (unit * 0.45));
                if (hash12(float2(row, tick)) < 0.30 * g)
                    uv.x += (hash12(float2(row + 5.3, tick)) - 0.5) * 0.10 * g;
                float s = 0.010 * g;
                float3 c = float3(Snap(uv + float2(s, 0)).r, Snap(uv).g, Snap(uv - float2(s, 0)).b);
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                return lerp(c, Ramp(lum * 0.9 + uv.y * 0.35), 0.4 * g);
            }

            // 바 머리(이동하는 끝부분) 색: 체커 / 무지개 / 흰색
            float3 HeadColor (float h, float2 px, float x, float unit)
            {
                if (h < 0.34) return Checker(px, unit * 0.22);
                if (h < 0.67) return Ramp(x * 2.0 + h);
                return _Paper.rgb;
            }

            // ── OUT : 이전 화면 → 덮개 ─────────────────────────────
            float3 PhaseOut (int style, float2 uv, float2 px, float2 res, float unit, float P, float seed, float tick)
            {
                if (style == 4) return lerp(Snap(uv), _Ink.rgb, P);
                if (P >= 0.985) return style == 1 ? _Paper.rgb : _Ink.rgb;

                float g = P * P;
                float3 col = GlitchSnap(uv, px, unit, g, tick);

                if (style == 0)          // Blocks
                {
                    float2 size = unit * float2(3.2, 2.0);
                    float rj = hash12(float2(floor(px.y / size.y), seed)) * size.x;
                    float2 q = float2(px.x + rj, px.y);
                    float2 cell = floor(q / size), lc = frac(q / size);
                    float h = hash12(cell + seed), h2 = hash12(cell + seed + 17.3);
                    float age = EaseOut((P - h * 0.55) / 0.10);
                    bool inked = P > 0.60 + h2 * 0.30;
                    float inset = inked ? -0.01 : 0.04 + 0.46 * (1.0 - age);
                    float d = max(abs(lc.x - 0.5), abs(lc.y - 0.5));
                    if (age > 0.0 && d < 0.5 - inset)
                        col = inked ? _Ink.rgb : (h2 < 0.7 ? Pal(hash12(cell + seed + 4.1)) : PhotoAt(uv, h));
                }
                else if (style == 1)     // Melt
                {
                    float m = saturate(P / 0.75);
                    float pix = max(1.0, unit * 0.7 * m * m);
                    float2 u2 = (floor(px / pix) + 0.5) * pix / res;
                    u2.y += hash12(float2(floor(px.x / (unit * 0.5)), seed)) * 0.22 * m * m;
                    float2 cell = floor(px / (unit * float2(2.0, 1.0)));
                    if (hash12(cell + tick) < 0.45 * m)
                        u2 += (hash22(cell + tick) - 0.5) * float2(0.18, 0.05) * m;
                    col = GlitchSnap(u2, px, unit, m, tick);
                    float lum = dot(col, float3(0.299, 0.587, 0.114));
                    col = lerp(col, Ramp(floor(lum * 5.0) / 5.0 + u2.y * 0.2), 0.5 * m * m);
                    col = lerp(col, _Paper.rgb, smoothstep(0.72, 0.95, P));
                }
                else if (style == 2)     // Bars
                {
                    float row = floor(px.y / (unit * 0.9));
                    float h = hash12(float2(row, seed)), h2 = hash12(float2(row, seed + 9.1));
                    float ext = EaseOut((P - h * 0.5) / 0.38);
                    float x = h2 < 0.5 ? uv.x : 1.0 - uv.x;
                    float headW = (unit * 2.8 / res.x) * saturate(ext * (1.0 - ext) * 4.0);
                    if (x < ext)
                        col = (ext - x) < headW ? HeadColor(hash12(float2(row, seed + 2.7)), px, x, unit) : _Ink.rgb;
                }
                else                     // Tiles
                {
                    float2 size = unit * float2(3.4, 2.4);
                    float2 off = (res - floor(res / size) * size) * 0.5;
                    float2 q = px - off;
                    float2 cell = floor(q / size), lc = frac(q / size);
                    float h = hash12(cell + seed), h2 = hash12(cell + seed + 3.3);
                    float gutter = unit * 0.12 * saturate(P / 0.12);
                    float shrink = EaseOut((P - h * 0.4) / 0.5);
                    float2 halfPx = (size * 0.5 - gutter) * (1.0 - shrink);
                    float2 dp = abs(lc - 0.5) * size;
                    if (dp.x < halfPx.x && dp.y < halfPx.y)
                    {
                        float2 cuv = ((cell + 0.5) * size + off) / res;
                        float2 u2 = cuv + (uv - cuv) * (1.0 - 0.15 * shrink);
                        col = h2 < 0.25 ? PhotoAt(u2, h) : GlitchSnap(u2, px, unit, g * 0.5, tick);
                    }
                    else col = _Ink.rgb;
                }
                return col;
            }

            // ── IN : 덮개 → 새 화면 (rgb, alpha) ───────────────────
            float4 PhaseIn (int style, float2 uv, float2 px, float2 res, float unit, float P, float seed)
            {
                if (style == 4) return float4(_Ink.rgb, 1.0 - P);
                if (P >= 0.985) return float4(0, 0, 0, 0);
                float4 ink = float4(_Ink.rgb, 1);

                if (style == 0)          // Blocks: 잉크 → 색 블록 → 사라짐
                {
                    float2 size = unit * float2(3.2, 2.0);
                    float rj = hash12(float2(floor(px.y / size.y), seed)) * size.x;
                    float2 q = float2(px.x + rj, px.y);
                    float2 cell = floor(q / size), lc = frac(q / size);
                    float h = hash12(cell + seed), h2 = hash12(cell + seed + 17.3);
                    float tA = h * 0.45;
                    float tB = min(tA + 0.18 + h2 * 0.25, 0.9);
                    if (P < tA) return ink;
                    if (P < tB)
                    {
                        float age = EaseOut((tB - P) / 0.08);
                        float inset = 0.04 + 0.46 * (1.0 - age);
                        float d = max(abs(lc.x - 0.5), abs(lc.y - 0.5));
                        if (d < 0.5 - inset)
                            return float4(h2 < 0.7 ? Pal(hash12(cell + seed + 4.1)) : PhotoAt(uv, h), 1);
                    }
                    return float4(0, 0, 0, 0);
                }
                if (style == 1)          // Melt: 흰 화면 → 체커·무지개 칸 → 사라짐
                {
                    float s = unit * 0.9;
                    float2 cell = floor(px / s);
                    float h = hash12(cell + seed), h2 = hash12(cell + seed + 6.6);
                    float t = h * 0.6;
                    if (P < t) return float4(_Paper.rgb, 1);
                    if (P < t + 0.14 && h2 < 0.6)
                        return float4(h2 < 0.3 ? Checker(px, unit * 0.22) : Ramp(uv.y + uv.x * 0.5 + h), 1);
                    return float4(0, 0, 0, 0);
                }
                if (style == 2)          // Bars: 잉크 바가 반대편으로 빠진다
                {
                    float row = floor(px.y / (unit * 0.9));
                    float h = hash12(float2(row, seed)), h2 = hash12(float2(row, seed + 9.1));
                    float r = EaseOut((P - h * 0.45) / 0.4);
                    float x = h2 < 0.5 ? uv.x : 1.0 - uv.x;
                    float headW = (unit * 2.8 / res.x) * saturate(r * (1.0 - r) * 4.0);
                    if (x >= r)
                        return (x - r) < headW ? float4(HeadColor(hash12(float2(row, seed + 2.7)), px, x, unit), 1) : ink;
                    return float4(0, 0, 0, 0);
                }
                // Tiles: 검은 화면에 창이 열리고 커지며 새 화면이 드러난다
                {
                    float2 size = unit * float2(3.4, 2.4);
                    float2 off = (res - floor(res / size) * size) * 0.5;
                    float2 q = px - off;
                    float2 cell = floor(q / size), lc = frac(q / size);
                    float h = hash12(cell + seed), h2 = hash12(cell + seed + 3.3);
                    float t0 = h * 0.35;
                    if (P < t0) return ink;
                    float pop = EaseOut((P - t0) / 0.08);
                    float grow = EaseOut((P - t0 - 0.12) / 0.5);
                    float2 halfPx = size * 0.5 * lerp(0.4 * pop, 1.02, grow);
                    float2 dp = abs(lc - 0.5) * size;
                    if (dp.x >= halfPx.x || dp.y >= halfPx.y) return ink;
                    if (P < t0 + 0.12 && h2 < 0.6) return float4(PhotoAt(uv, h), 1);
                    return float4(0, 0, 0, 0);
                }
            }

            // ── PULSE : 하이라이트 순간, 가장자리만 ────────────────
            float4 PhasePulse (float2 uv, float2 px, float2 res, float unit, float P, float seed)
            {
                float env = saturate(P / 0.12) * (1.0 - smoothstep(0.65, 1.0, P));
                float4 o = 0;

                // 위·아래 띠(각 화면 높이 7%): 가로로 미끄러지는 글리치 바
                float edge = min(uv.y, 1.0 - uv.y);
                if (edge < 0.07)
                {
                    float rowH = unit * 0.28;
                    float row = floor(px.y / rowH);
                    float h = hash12(float2(row, seed)), h2 = hash12(float2(row, seed + 5.5));
                    float segW = unit * (1.5 + h * 4.0);
                    float slide = (h2 < 0.5 ? 1.0 : -1.0) * P * unit * (6.0 + h * 10.0);
                    float sx = px.x + slide + h * 997.0;
                    float seg = floor(sx / segW);
                    float hs = hash12(float2(seg, row + seed));
                    float fill = step(hs, 0.45 * env + 0.1 * (1.0 - edge / 0.07) * env);
                    if (fill > 0.5)
                    {
                        float3 c = hs < 0.15 ? Checker(px, unit * 0.14) : hs < 0.35 ? Ramp(sx / (unit * 8.0) + h) : _Paper.rgb;
                        o = float4(c, 0.9);
                    }
                }

                // 양옆(화면 폭 18%): 작은 색종이가 팝 하고 살짝 떨어지며 사라짐
                float side = min(uv.x, 1.0 - uv.x);
                if (o.a < 0.01 && side < 0.18)
                {
                    float s = unit * 0.7;
                    float2 q = px + float2(0, P * unit * 1.2);      // 아래로 살짝 흐름
                    float2 cell = floor(q / s), lc = frac(q / s);
                    float h = hash12(cell + seed + 11.0);
                    if (h < 0.35)
                    {
                        float2 c = hash22(cell + seed) * 0.5 + 0.25;
                        float ang = h * 40.0 + P * 6.0;
                        float2 d = lc - c;
                        d = float2(d.x * cos(ang) - d.y * sin(ang), d.x * sin(ang) + d.y * cos(ang));
                        float pop = EaseOut((P - h * 0.6) / 0.15) * (1.0 - smoothstep(0.6, 1.0, P));
                        float2 sz = float2(0.16, 0.09) * pop;
                        if (abs(d.x) < sz.x && abs(d.y) < sz.y) o = float4(Pal(hash12(cell + 2.2)), 1);
                    }
                }
                o.a *= env;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 res = max(_Res.xy, float2(16, 16));
                float2 uv = i.uv;
                float2 px = uv * res;
                float unit = min(res.x, res.y) / 12.0;
                float P = saturate(_P);
                float tick = fmod(floor(_Time.y * 20.0), 1024.0) + _Seed;
                int style = (int)round(_Style);

                float4 o;
                if (_Phase < 0.5)      o = float4(PhaseOut(style, uv, px, res, unit, P, _Seed, tick), 1);
                else if (_Phase < 1.5) o = PhaseIn(style, uv, px, res, unit, P, _Seed + 50.0);
                else                   o = PhasePulse(uv, px, res, unit, P, _Seed);

                o.a *= i.color.a;
                return float4(o.rgb * o.a, o.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
