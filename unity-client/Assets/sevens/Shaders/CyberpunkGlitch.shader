Shader "Sevens/CyberpunkGlitch"
{
    // 풀스크린 글리치 효과 - URP Renderer Feature에서 Blit으로 사용
    Properties
    {
        _MainTex ("Screen Texture", 2D) = "white" {}

        [Header(Chromatic Aberration)]
        _ChromaStrength ("Chroma Strength", Range(0, 0.02)) = 0.003
        _ChromaPulse ("Chroma Pulse Speed", Range(0, 5)) = 1.5

        [Header(Scanlines)]
        _ScanlineCount ("Scanline Count", Range(50, 800)) = 300
        _ScanlineAlpha ("Scanline Alpha", Range(0, 0.3)) = 0.05

        [Header(Glitch)]
        _GlitchIntensity ("Glitch Intensity", Range(0, 0.05)) = 0.01
        _GlitchSpeed ("Glitch Speed", Range(0, 20)) = 8
        _GlitchBlockSize ("Block Size", Range(2, 64)) = 16

        [Header(Vignette)]
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.3
        _VignetteColor ("Vignette Color", Color) = (0.05, 0.02, 0.1, 1)

        [Header(Pixelation)]
        _PixelSize ("Pixel Size", Range(1, 16)) = 4
        _PixelSmooth ("Pixel Smoothness", Range(0, 1)) = 0.3

        [Header(Color Tint)]
        _TintColor ("Warm Tint", Color) = (1.0, 0.95, 0.9, 1)
        _TintStrength ("Tint Strength", Range(0, 0.5)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CyberpunkGlitch"
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _ChromaStrength;
                float _ChromaPulse;
                float _ScanlineCount;
                float _ScanlineAlpha;
                float _GlitchIntensity;
                float _GlitchSpeed;
                float _GlitchBlockSize;
                float _VignetteIntensity;
                float4 _VignetteColor;
                float _PixelSize;
                float _PixelSmooth;
                float4 _TintColor;
                float _TintStrength;
            CBUFFER_END

            // 랜덤 해시
            float hash(float n) { return frac(sin(n) * 43758.5453); }
            float hash2(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float time = _Time.y;

                // === 픽셀화 (모자이크) ===
                float2 screenRes = _ScreenParams.xy;
                float pixelScale = max(1.0, _PixelSize);
                float2 pixelGrid = screenRes / pixelScale;
                float2 pixelUV = floor(uv * pixelGrid) / pixelGrid;
                // 원본과 픽셀화를 블렌드 (Smoothness로 조절)
                uv = lerp(uv, pixelUV, _PixelSmooth);

                // === 글리치 오프셋 ===
                float glitchLine = floor(uv.y * _GlitchBlockSize);
                float glitchRandom = hash(glitchLine + floor(time * _GlitchSpeed));
                float glitchTrigger = step(0.95, glitchRandom); // 5% 확률로 글리치 발동
                float glitchOffset = (glitchRandom - 0.5) * _GlitchIntensity * glitchTrigger;
                uv.x += glitchOffset;

                // === 색수차 (Chromatic Aberration) ===
                float chromaPulse = sin(time * _ChromaPulse) * 0.5 + 0.7;
                float chromaOffset = _ChromaStrength * chromaPulse;

                half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(chromaOffset, 0)).r;
                half g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).g;
                half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(chromaOffset, 0)).b;
                half3 color = half3(r, g, b);

                // === 스캔라인 ===
                float scanline = sin(uv.y * _ScanlineCount * 3.14159) * 0.5 + 0.5;
                color -= scanline * _ScanlineAlpha;

                // === 따뜻한 색조 보정 (힐링) ===
                color = lerp(color, color * _TintColor.rgb, _TintStrength);

                // === 비네트 (사이버펑크 보라) ===
                float2 vignetteUV = uv - 0.5;
                float vignette = 1.0 - dot(vignetteUV, vignetteUV) * 2.0;
                vignette = saturate(pow(vignette, 1.5));
                color = lerp(_VignetteColor.rgb, color, lerp(1.0, vignette, _VignetteIntensity));

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
