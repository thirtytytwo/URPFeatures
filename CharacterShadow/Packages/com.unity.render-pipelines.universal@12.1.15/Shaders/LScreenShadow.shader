Shader "LURP/Feature/LScreenShadow"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        Pass
        {
            Name "Background Shadow"
            Blend Off
            ZWrite Off
            Cull Off
            ZTest Always
            
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float3 positionOS: POSITION;
            };

            struct Varyings
            {
                float4 positionCS: SV_POSITION;
                float2 uv        : TEXCOORD0;
            };
            

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            
            real SampleShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float3 shadowCoord)
            {
                real attenuation;
            
                    // 1-tap hardware comparison
                    attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord));
            
                // Shadow coords that fall out of the light frustum volume must always return attenuation 1.0
                // TODO: We could use branch here to save some perf on some platforms.
                return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
            }
            
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionCS = float4(i.positionOS.x, i.positionOS.y, 0.0f, 1.0f);
                o.uv = i.positionOS.xy * 0.5f + 0.5f;
                #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1.0f - o.uv.y;
                #endif
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                //�������ͼ��ԭ��ӦZ����
            #if UNITY_REVERSED_Z
                float deviceDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv.xy);
            #else
                float deviceDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, i.uv.xy);
                deviceDepth = deviceDepth * 2.0 - 1.0;
            #endif
                //�ع���������
                float3 positionWS = ComputeWorldSpacePosition(i.uv.xy, deviceDepth, unity_MatrixInvVP);
                //ת��shadowmap�ռ�
                float4 shadowCoord = TransformWorldToShadowCoord(positionWS.xyz);
                return SampleShadowmap(_MainLightShadowmapTexture, sampler_MainLightShadowmapTexture, shadowCoord).xxxx;
            }
            ENDHLSL
        }
        
        Pass
        {
            Name"Per Character Shadow"
            ZWrite Off
            ZTest Off
            Cull Off
            Stencil
            {
                Ref 3
                Comp NotEqual
                Pass Keep
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            struct Attribute
            {
                float4 positionOS : POSITION;
            };

            struct Varying
            {
                float4 positionCS : SV_POSITION;
                float4 positionSS : TEXCOORD0;
            };
            TEXTURE2D(_CameraDepthTexture);
            TEXTURE2D(_CharacterShadowmap);
            SAMPLER(sampler_PointClamp);

            float4x4 _WorldToShadowMatrix[4];
            float4 _CharacterShadowmapSize;
            float _ShadowDepthBias;
            int _CharacterID;
            
            float3 TransformWorldToCharacterShadow(float3 positionWS)
            {
                float4 ret = mul(_WorldToShadowMatrix[_CharacterID], float4(positionWS, 1.0f));
                return ret.xyz / ret.www; 
            }

            half SampleCharacterShadowmap(float3 pos)
            {
                half val = SAMPLE_TEXTURE2D(_CharacterShadowmap, sampler_PointClamp, float2(pos.xy)).r;
                half ret = smoothstep(0, 0.1f, pos.z - val);
                ret = step(0.99f, ret);
                return BEYOND_SHADOW_FAR(pos) ? 1.0 : ret;
            }
            Varying vert(Attribute i)
            {
                Varying o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.positionSS = ComputeScreenPos(o.positionCS);
                return o;
            }
            half4 frag(Varying i):SV_Target
            {
                float3 pos = i.positionSS.xyw;

                float2 uv = pos.xy / pos.z;
                #if UNITY_REVERSED_Z
                float deviceDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_PointClamp, uv);
            #else
                float deviceDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_PointClamp, uv);
                deviceDepth = deviceDepth * 2.0 - 1.0;
            #endif

                float3 positionWS = ComputeWorldSpacePosition(uv, deviceDepth, unity_MatrixInvVP);
                
                float3 shadowCoord = TransformWorldToCharacterShadow(positionWS);
                // return half4(shadowCoord, 1.0f);
                float clampXMin = (_CharacterID % 2) == 0 ? 0 : 0.5;
                float clampXMax = (_CharacterID % 2) == 0 ? 0.5 : 1;
                float clampYMin = (_CharacterID / 2) == 0 ? 0 : 0.5;
                float clampYMax = (_CharacterID / 2) == 0 ? 0.5 : 1;
                float clampZMin = 0;
                float clampZMax = 1;
                bool flag = (shadowCoord.x >= clampXMin && shadowCoord.x <= clampXMax) &&
                    (shadowCoord.y >= clampYMin && shadowCoord.y <= clampYMax);
                if (!flag) clip(-1);

                return SampleCharacterShadowmap(float3(shadowCoord.xy, shadowCoord.z + _ShadowDepthBias)).xxxx;
            }
            ENDHLSL
        }

        Pass
        {
            Name"Combine to Camera"
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attribute
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 _ShadowCombineParam;
            TEXTURE2D(_LScreenShadowTexture);
            TEXTURE2D(_CameraColorTexture);
            SAMPLER(sampler_LinearClamp);

            Varyings vert(Attribute i)
            {
                Varyings o;
                o.positionCS = float4(i.positionOS.x, i.positionOS.y, 0.0f, 1.0f);
                o.uv = i.positionOS.xy * 0.5f + 0.5f;
                #if UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1.0f - o.uv.y;
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 sourceColor = SAMPLE_TEXTURE2D(_CameraColorTexture, sampler_LinearClamp, i.uv);
                half shadow = SAMPLE_TEXTURE2D(_LScreenShadowTexture, sampler_LinearClamp, i.uv);
                half3 resultColor = sourceColor.rgb;
                if (shadow >= 0.f && shadow < 0.5f)
                {
                    shadow = smoothstep(0, 0.5, shadow);
                    resultColor = lerp(half3(0,0,0) + _ShadowCombineParam.www,_ShadowCombineParam.xyz, shadow) * sourceColor.rgb;
                }
                return half4(resultColor, sourceColor.a);
            }
            ENDHLSL
        }
    }        
}
