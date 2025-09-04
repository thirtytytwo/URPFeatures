Shader "Unlit/RayMarchingShader"
{

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD0;
            };

            float2 _DepthParams;
            float _Transmittance;
            float _PhaseG;
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            TEXTURE2D(_NoiseTexture);
            SAMPLER(sampler_NoiseTexture);

            float DepthToWorldDistance(float2 screenPos, float depth)
            {
                float2 pos = (screenPos.xy * 2.0f - 1.0f) * _DepthParams.xy;
                float3 ray = float3(pos.xy, 1);
                return LinearEyeDepth(depth, _ZBufferParams) * length(ray);
            }

            float SampleOffset(float2 screenPos)
            {
                return SAMPLE_TEXTURE2D(_NoiseTexture, sampler_NoiseTexture, screenPos * 10.f).r * 2.0f - 1.0f;
            }

            float GetPhg(float g, float cosTheta)
            {
                float Numer = 1.0f - g * g;
                float Denom = max(0.001, 1.0f + g * g - 2.0f * g * cosTheta);
                return Numer / (4.0f * PI * Denom * sqrt(Denom)); //1.5 = 1 + 0.5 = x * sqrt(x)
            }

            half4 Scattering(float3 ray, float near, float far)
            {
                float tr = 1.0f;
                float3 result = 0.0f;
                float stepSize = (far - near) / 16.0f;
                float3 lightDir = _MainLightPosition.xyz;
                [loop]
                for (int i = 1; i <= 16; ++i)
                {
                    float3 pos = _WorldSpaceCameraPos + ray * (near + stepSize * i);
                    tr *= exp(-stepSize * _Transmittance);

                    float cosTheta = dot(lightDir, ray);
                    float phaseG = GetPhg(_PhaseG, cosTheta);

                    float3 shadowCoord = TransformWorldToShadowCoord(pos);
                    float shadow = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture,
                                              sampler_MainLightShadowmapTexture, shadowCoord);

                    result += _MainLightColor.rgb * phaseG * shadow * tr * stepSize;
                }
                return half4(result, tr);
            }

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = float4(i.positionOS.xy, 0.0f, 1.0f);
                float4 pos = mul(unity_MatrixInvVP, o.positionCS);
                o.positionWS = pos.xyz / pos.w;
                o.uv = i.positionOS.xy * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1.0f - o.uv.y;
                #endif
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 screenPos = i.uv;
                float3 ray = normalize(i.positionWS - _WorldSpaceCameraPos);
                float depth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, screenPos).r;
                float near = _ProjectionParams.y;
                float far = _ProjectionParams.z;

                float cameraWorldDepth = DepthToWorldDistance(screenPos, depth);
                far = min(far, cameraWorldDepth);

                float offset = SampleOffset(screenPos) * (far - near) / 16.0f;
                far += offset;
                near += offset;

                half4 color = Scattering(ray, near, far);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            float4 _TextureSize;

            TEXTURE2D(_Source);
            SAMPLER(sampler_Source);
            TEXTURE2D(_VolumeLightRT);
            SAMPLER(sampler_VolumeLightRT);

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = float4(i.positionOS.xy, 0.0f, 1.0f);
                o.uv = i.positionOS.xy * 0.5f + 0.5f;
                #if UNITY_REVERSED_Z
                o.uv.y = 1.0f - o.uv.y;
                #endif
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_Source, sampler_Source, i.uv);
                float4 sampleOffset1 = i.uv.xyxy + float4(-1.0f, 0.0f, 1.0f, 0.0f) * _TextureSize.xyxy;
                float4 sampleOffset2 = i.uv.xyxy + float4(0.0f, -1.0f, 0.0f, 1.0f) * _TextureSize.xyxy;
                float4 volumeLight = (SAMPLE_TEXTURE2D(_VolumeLightRT, sampler_VolumeLightRT, sampleOffset1.xy) +
                    SAMPLE_TEXTURE2D(_VolumeLightRT, sampler_VolumeLightRT, sampleOffset1.zw) +
                    SAMPLE_TEXTURE2D(_VolumeLightRT, sampler_VolumeLightRT, sampleOffset2.xy) +
                    SAMPLE_TEXTURE2D(_VolumeLightRT, sampler_VolumeLightRT, sampleOffset2.zw)) * 0.25f;
                return half4(color * volumeLight.a + volumeLight.rgb, 1.0f);
            }
            ENDHLSL
        }
    }
}