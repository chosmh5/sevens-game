Shader "Sevens/CyberpunkNeon"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.05, 0.05, 0.1, 1)

        [Header(Neon Glow)]
        _NeonColor1 ("Neon Color 1 (Blue)", Color) = (0.2, 0.5, 1.0, 1)
        _NeonColor2 ("Neon Color 2 (Red/Pink)", Color) = (1.0, 0.2, 0.5, 1)
        _NeonIntensity ("Neon Intensity", Range(0, 5)) = 2.0

        [Header(Edge Glow)]
        _FresnelPower ("Edge Glow Power", Range(0.5, 8)) = 2.5
        _FresnelIntensity ("Edge Glow Intensity", Range(0, 3)) = 1.5

        [Header(Warm Ambient)]
        _WarmColor ("Warm Ambient", Color) = (1.0, 0.85, 0.6, 1)
        _WarmIntensity ("Warm Intensity", Range(0, 1)) = 0.15

        [Header(Emission Pulse)]
        _PulseSpeed ("Pulse Speed", Range(0, 5)) = 1.0
        _PulseMin ("Pulse Min", Range(0, 1)) = 0.5

        [Header(Toon)]
        _ShadowColor ("Shadow Color", Color) = (0.08, 0.04, 0.15, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 1)) = 0.4

        [Header(Dither Fade)]
        [HideInInspector] _FadeAmount ("Fade Amount", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _NeonColor1;
                float4 _NeonColor2;
                float _NeonIntensity;
                float _FresnelPower;
                float _FresnelIntensity;
                float4 _WarmColor;
                float _WarmIntensity;
                float _PulseSpeed;
                float _PulseMin;
                float4 _ShadowColor;
                float _ShadowThreshold;
                float _FadeAmount;
            CBUFFER_END

            // 4x4 Bayer dither
            static const float ditherMatrix[16] = {
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            };

            // _FadeAmount: 0 = 불투명(기본), 1 = 완전 투명
            void ApplyDitherFade(float4 positionCS)
            {
                if (_FadeAmount > 0.001)
                {
                    float2 screenPos = positionCS.xy;
                    int2 pixel = int2(fmod(abs(screenPos.x), 4), fmod(abs(screenPos.y), 4));
                    float threshold = ditherMatrix[pixel.y * 4 + pixel.x];
                    // _FadeAmount가 threshold보다 크면 해당 픽셀 clip
                    clip(threshold - _FadeAmount);
                }
            }
        ENDHLSL

        Pass
        {
            Name "CyberpunkNeon"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = normInputs.normalWS;
                OUT.positionWS = posInputs.positionWS;
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
                OUT.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                ApplyDitherFade(IN.positionCS);

                // 기본 텍스처
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 baseColor = texColor.rgb * _BaseColor.rgb;

                // 라이팅 (툰 스타일)
                Light mainLight = GetMainLight();
                float NdotL = dot(normalize(IN.normalWS), mainLight.direction);
                float toonLight = smoothstep(_ShadowThreshold - 0.02, _ShadowThreshold + 0.02, NdotL * 0.5 + 0.5);

                // 그림자 색
                half3 shadowColor = _ShadowColor.rgb;
                half3 litColor = lerp(shadowColor, baseColor, toonLight);

                // 프레넬 엣지 글로우 (네온)
                float fresnel = pow(1.0 - saturate(dot(normalize(IN.normalWS), normalize(IN.viewDirWS))), _FresnelPower);

                // 네온 색상 펄스 (파랑 ↔ 핑크 전환)
                float pulse = lerp(_PulseMin, 1.0, (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5));
                float colorMix = sin(_Time.y * _PulseSpeed * 0.7 + IN.positionWS.y * 0.3) * 0.5 + 0.5;
                half3 neonColor = lerp(_NeonColor1.rgb, _NeonColor2.rgb, colorMix);

                half3 edgeGlow = fresnel * _FresnelIntensity * neonColor * _NeonIntensity * pulse;

                // 따뜻한 앰비언트 (힐링)
                half3 warmAmbient = _WarmColor.rgb * _WarmIntensity * (1.0 - fresnel);

                // 높이 기반 네온 그라데이션 (아래=따뜻, 위=사이버)
                float heightFactor = saturate(IN.positionWS.y * 0.05);
                half3 heightNeon = neonColor * heightFactor * 0.3 * pulse;

                // 최종 합성
                half3 finalColor = litColor + edgeGlow + warmAmbient + heightNeon;

                // 포그
                finalColor = MixFog(finalColor, IN.fogFactor);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // Shadow Caster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            float3 _LightDirection;

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                posWS = posWS + _LightDirection * 0.01;
                OUT.positionCS = TransformWorldToHClip(posWS);
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_Target
            {
                ApplyDitherFade(IN.positionCS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 DepthFrag(Varyings IN) : SV_Target
            {
                ApplyDitherFade(IN.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }
}
