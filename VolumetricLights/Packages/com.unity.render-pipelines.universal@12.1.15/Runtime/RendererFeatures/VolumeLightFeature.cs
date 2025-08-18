using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class VolumeLightFeature : ScriptableRendererFeature
    {
        public ComputeShader VolumeLightShader;
        [Range(10.0f, 100.0f)] public float FarClipPlane = 100.0f;
        public Vector3 VolumeSize = Vector3.one;
        [Header("全局物理量")]
        public Color GlobalOutScatter = Color.white;
        [Range(0.0f, 1.0f)]public float GlobalExtinction = 0.0f;
        [Range(-1.0f, 1.0f)] public float GlobalPhaseG = 0.0f;
        public Material VolumeLightApplyMaterial;
    
        private VolumeLightPass m_VolumeLightPass;
        private VolumeLightApplyPass m_VolumeLightApplyPass;
        public override void Create()
        {
            m_VolumeLightPass = new VolumeLightPass(VolumeLightShader);
            m_VolumeLightPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;

            m_VolumeLightApplyPass = new VolumeLightApplyPass(VolumeLightApplyMaterial);
            m_VolumeLightApplyPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (camera.CompareTag("MainCamera"))
            {
                m_VolumeLightPass.Setup(FarClipPlane, VolumeSize, new Vector4(GlobalOutScatter.r * GlobalExtinction, GlobalOutScatter.g * GlobalExtinction, GlobalOutScatter.b * GlobalExtinction, GlobalExtinction), GlobalPhaseG);
                renderer.EnqueuePass(m_VolumeLightPass);
                // m_VolumeLightApplyPass.Setup(FarClipPlane);
                // renderer.EnqueuePass(m_VolumeLightApplyPass);
            }
        }
    
        public class VolumeLightPass : ScriptableRenderPass
        {
            private float farClipPlane = 100f;
            private Vector3 volumeSize = Vector3.one;
            private Vector4 m_GlobalOutScatterAndExtinction;
            private float phaseG = 0.0f;
            private ComputeShader volumetricLightShader;

            private RenderTargetIdentifier mediumTexture0;
            private RenderTargetIdentifier mediumTexture1;
            private RenderTargetIdentifier lightTexture;
            private RenderTargetIdentifier calculateTexture;
            public VolumeLightPass(ComputeShader cs)
            {
                volumetricLightShader = cs;
                mediumTexture0 = new RenderTargetIdentifier(Shader.PropertyToID("_MediumTexture0"));
                mediumTexture1 = new RenderTargetIdentifier(Shader.PropertyToID("_MediumTexture1"));
                lightTexture = new RenderTargetIdentifier(Shader.PropertyToID("_LightTexture"));
                calculateTexture = new RenderTargetIdentifier(Shader.PropertyToID("_CalculateTexture"));
            }
            public void Setup(float far, Vector3 size, Vector4 globalOutScatterAndExtinction, float g)
            {
                farClipPlane = far;
                volumeSize = size;
                m_GlobalOutScatterAndExtinction = globalOutScatterAndExtinction;
                phaseG = g;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor descriptor = new RenderTextureDescriptor();
                descriptor.dimension = TextureDimension.Tex3D;
                descriptor.width = (int)volumeSize.x;
                descriptor.height = (int)volumeSize.y;
                descriptor.volumeDepth = (int)volumeSize.z;
                descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_UNorm;
            
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.enableRandomWrite = true;
            
                cmd.GetTemporaryRT(Shader.PropertyToID("_MediumTexture0"), descriptor, FilterMode.Point);
                cmd.GetTemporaryRT(Shader.PropertyToID("_MediumTexture1"), descriptor, FilterMode.Point);
                cmd.GetTemporaryRT(Shader.PropertyToID("_LightTexture"), descriptor, FilterMode.Point);
                cmd.GetTemporaryRT(Shader.PropertyToID("_CalculateTexture"), descriptor, FilterMode.Point);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get();
                var lightData = renderingData.lightData;
                var lights = lightData.visibleLights;

                using (new ProfilingScope(cmd, new ProfilingSampler("VolumeLightPass")))
                {
                    var cameraData = renderingData.cameraData;
                    GetVolumeMatrixVP(cameraData, out Matrix4x4 volumeMatrixVP, out Matrix4x4 volumeMatrixInvVP);
                    Vector4 decodeParams = ComputeLogarithmicDepthDecodingParams(cameraData.camera.nearClipPlane, farClipPlane, 0.5f);
                
                    cmd.SetComputeMatrixParam(volumetricLightShader,"_VolumeMatrixInvVP", volumeMatrixInvVP);
                    cmd.SetGlobalMatrix("_VolumeMatrixVP", volumeMatrixVP);
                
                    cmd.SetComputeVectorParam(volumetricLightShader,"_VolumeSize", volumeSize);
                    cmd.SetComputeVectorParam(volumetricLightShader,"_DecodeParams", decodeParams);
            
                    Matrix4x4[] matrices = new Matrix4x4[LocalVolume.AllVolumes.Count];
                    Vector4[] outScatterAndExtinction = new Vector4[LocalVolume.AllVolumes.Count];
                    Vector4[] emissionAndPhaseG = new Vector4[LocalVolume.AllVolumes.Count];
                    for (int i = 0; i < LocalVolume.AllVolumes.Count; i++)
                    {
                        var volume = LocalVolume.AllVolumes[i];
                        if(volume == null) continue;

                        matrices[i] = volume.VolumeMatrix;
                        outScatterAndExtinction[i] = volume.GetOutScatterAndExtinction();
                        emissionAndPhaseG[i] = volume.GetEmissionAndPhaseG();
                    }
                
                    //Vector4[] lightPositions = new Vector4[lights.Length];
                    // Vector4[] lightColors = new Vector4[lights.Length-1];
                    // Vector4[] lightPositions = new Vector4[lights.Length-1];
                    // Vector4[] lightDistanceAndAngle = new Vector4[lights.Length-1];
                    // Vector4[] lightSpotDirections = new Vector4[lights.Length-1];
                    //additional lights
                    // for (int i = 1; i < lights.Length; i++)
                    // {
                    //     var light = lights[i].light;
                    //     lightColors[i - 1] = light.color;
                    //     lightPositions[i - 1] = light.transform.position;
                    //
                    //     float rangeSqr = light.range * light.range;
                    //     float fadeDistanceSqr = 0.64f * rangeSqr;
                    //     float innerAngle = Mathf.Cos(light.innerSpotAngle);
                    //     float outerAngle = Mathf.Cos(light.spotAngle);
                    //     float invAngleRange = 1.0f / (innerAngle - outerAngle);
                    //
                    //     float dx = 1.0f / (fadeDistanceSqr - rangeSqr);
                    //     float dy = -rangeSqr / (fadeDistanceSqr - rangeSqr);
                    //     float ax = invAngleRange;
                    //     float ay = -outerAngle * invAngleRange;
                    //     lightDistanceAndAngle[i - 1] = new Vector4(dx, dy, ax, ay);
                    //
                    //     lightSpotDirections[i-1]= light.transform.forward;
                    // }
                
                    // cmd.SetComputeIntParam(volumetricLightShader, "_AddLightCount", lights.Length -1);
                    // cmd.SetComputeVectorArrayParam(volumetricLightShader, "_AddLightColors", lightColors);
                    // cmd.SetComputeVectorArrayParam(volumetricLightShader, "_AddLightPosition", lightPositions);
                    // cmd.SetComputeVectorArrayParam(volumetricLightShader, "_AddLightDistanceAndAngle", lightDistanceAndAngle);
                    // cmd.SetComputeVectorArrayParam(volumetricLightShader, "_AddLightSpotDirections", lightSpotDirections);
                
                    cmd.SetComputeMatrixArrayParam(volumetricLightShader,"_VolumeMatrices", matrices);
                    cmd.SetComputeVectorArrayParam(volumetricLightShader, "_LocalEmissionAndPhaseG", emissionAndPhaseG);
                    cmd.SetComputeVectorArrayParam(volumetricLightShader, "_LocalOutScatterAndExtinctions", outScatterAndExtinction);
                    cmd.SetComputeVectorParam(volumetricLightShader, "_GlobalOutScatterAndExtinction", m_GlobalOutScatterAndExtinction);
                    cmd.SetComputeIntParam(volumetricLightShader, "_LocalVolumeCount", matrices.Length);
                    cmd.SetComputeIntParam(volumetricLightShader, "_FrameCount", Time.frameCount);
                    cmd.SetComputeVectorParam(volumetricLightShader, "_Jitter", new Vector4(0,0,0,0));
                    cmd.SetComputeFloatParam(volumetricLightShader, "_GlobalPhaseG", phaseG);
            
                    Vector3 groupSize = new Vector3(volumeSize.x / 8, volumeSize.y / 8, volumeSize.z / 8);
            
                    int kernelIndex0 = volumetricLightShader.FindKernel("Compute0");
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex0, "_MediumTexture0", mediumTexture0);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex0, "_MediumTexture1", mediumTexture1);
                    cmd.DispatchCompute(volumetricLightShader, kernelIndex0, (int)groupSize.x, (int)groupSize.y, (int)groupSize.z);
                
                    int kernelIndex1 = volumetricLightShader.FindKernel("Compute1");
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex1, "_MediumTexture0", mediumTexture0);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex1, "_MediumTexture1", mediumTexture1);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex1, "_LightTexture", lightTexture);
                    cmd.DispatchCompute(volumetricLightShader, kernelIndex1, (int)groupSize.x, (int)groupSize.y, (int)groupSize.z);
                
                    int kernelIndex2 = volumetricLightShader.FindKernel("Compute2");
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex2, "_LightTexture", lightTexture);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex2, "_CalculateTexture", calculateTexture);
                    cmd.DispatchCompute(volumetricLightShader, kernelIndex2, (int)groupSize.x, (int)groupSize.y, 1);
                
                    cmd.SetGlobalTexture("_VolumetricLightTexture", calculateTexture);
                    var encodeParams = ComputeLogarithmicDepthEncodingParams(renderingData.cameraData.camera.nearClipPlane, farClipPlane, 0.5f);
                    cmd.SetGlobalVector("_EncodeParams", encodeParams);
                }
            
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                cmd.Release();
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(Shader.PropertyToID("_MediumTexture0"));
                cmd.ReleaseTemporaryRT(Shader.PropertyToID("_MediumTexture1"));
                cmd.ReleaseTemporaryRT(Shader.PropertyToID("_LightTexture"));
                cmd.ReleaseTemporaryRT(Shader.PropertyToID("_CalculateTexture"));
            }

            public void GetVolumeMatrixVP(CameraData cameraData, out Matrix4x4 volumeMatrix, out Matrix4x4 volumeMatrixInv)
            {
                var camera = cameraData.camera;
                var viewMatrix = camera.worldToCameraMatrix;
                var projectionMatrix = Matrix4x4.Perspective(camera.fieldOfView, camera.aspect, camera.nearClipPlane, farClipPlane);

                volumeMatrixInv = viewMatrix.inverse * projectionMatrix.inverse;
                volumeMatrix = projectionMatrix * viewMatrix;
            }
        
            public Vector4 ComputeLogarithmicDepthDecodingParams(float nearPlane, float farPlane, float c)
            {
                Vector4 depthParams = new Vector4();

                float n = nearPlane;
                float f = farPlane;

                depthParams.x = 1.0f / c;
                depthParams.y = Mathf.Log(c * (f - n) + 1, 2);
                depthParams.z = n - 1.0f / c; // Same
                depthParams.w = 0.0f;

                return depthParams;
            }
            
            public Vector4 ComputeLogarithmicDepthEncodingParams(float nearPlane, float farPlane, float c)
            {
                Vector4 depthParams = new Vector4();

                float n = nearPlane;
                float f = farPlane;

                depthParams.y = 1.0f / Mathf.Log(c * (f - n) + 1, 2);
                depthParams.x = Mathf.Log(c, 2) * depthParams.y;
                depthParams.z = n - 1.0f / c; // Same
                depthParams.w = 0.0f;

                return depthParams;
            }
        
        }
    
        public class VolumeLightApplyPass : ScriptableRenderPass
        {
            float farClipPlane = 100f;
            Material volumeLightApplyMaterial;
            public VolumeLightApplyPass(Material mat)
            {
                volumeLightApplyMaterial = mat;
            }
            public void Setup(float far)
            {
                farClipPlane = far;
            }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get();
                var renderer = renderingData.cameraData.renderer;
                using (new ProfilingScope(cmd, new ProfilingSampler("VolumeLightApplyPass")))
                {
                    var encodeParams = ComputeLogarithmicDepthEncodingParams(renderingData.cameraData.camera.nearClipPlane, farClipPlane, 0.5f);
                    cmd.SetGlobalVector("_EncodeParams", encodeParams);
                    cmd.SetRenderTarget(renderer.GetCameraColorFrontBuffer(cmd));
                    cmd.DrawMesh(RenderingUtils.fastfullscreenMesh, Matrix4x4.identity, volumeLightApplyMaterial);
                    renderer.SwapColorBuffer(cmd);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        
            public Vector4 ComputeLogarithmicDepthEncodingParams(float nearPlane, float farPlane, float c)
            {
                Vector4 depthParams = new Vector4();

                float n = nearPlane;
                float f = farPlane;

                depthParams.y = 1.0f / Mathf.Log(c * (f - n) + 1, 2);
                depthParams.x = Mathf.Log(c, 2) * depthParams.y;
                depthParams.z = n - 1.0f / c; // Same
                depthParams.w = 0.0f;

                return depthParams;
            }
        }
    }
    
}
