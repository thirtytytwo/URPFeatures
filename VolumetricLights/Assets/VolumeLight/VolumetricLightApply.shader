Shader "VolumetricLight/VolumetricLightApply"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        ZTest Always
        ZWrite Off
        Cull Off

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

            TEXTURE2D(_CameraColorTexture);
            SAMPLER(sampler_CameraColorTexture);
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            TEXTURE3D(_VolumetricLightTexture);
            SAMPLER(sampler_VolumetricLightTexture);

            float4 _EncodeParams;

            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = float4(i.positionOS.x, i.positionOS.y, 0.0f, 1.0f);
                float u = i.positionOS.x * 0.5f + 0.5f;
                float v = i.positionOS.y * 0.5f + 0.5f;
                #if UNITY_UV_STARTS_AT_TOP
                v = 1.0f - v;
                #endif
                o.uv = float2(u, v);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 mainColor = SAMPLE_TEXTURE2D(_CameraColorTexture, sampler_CameraColorTexture, i.uv);
                float ndcZ = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv).r;
                float z = EncodeLogarithmicDepthGeneralized(ndcZ, _EncodeParams);
                
                float4 calculateResult = SAMPLE_TEXTURE3D(_VolumetricLightTexture, sampler_VolumetricLightTexture, float3(i.uv, z));
                float3 integral = calculateResult.rgb;
                float tr = calculateResult.a;

                return half4(calculateResult.rgb, mainColor.a);
            }
            ENDHLSL
        }
    }
}
