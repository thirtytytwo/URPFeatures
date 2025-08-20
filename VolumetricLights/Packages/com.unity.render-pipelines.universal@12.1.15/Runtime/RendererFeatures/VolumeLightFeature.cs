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
        [Range(0.0f, 1000.0f)]public float GlobalExtinction = 0.0f;
        [Range(-1.0f, 1.0f)] public float GlobalPhaseG = 0.0f;
        [Range(1.0f, 10.0f)] public float LightIntensity = 2.0f;
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
                m_VolumeLightPass.Setup(FarClipPlane, VolumeSize, LightIntensity,
                    new Vector4(GlobalOutScatter.r, GlobalOutScatter.g, GlobalOutScatter.b, GlobalExtinction), GlobalPhaseG);
                renderer.EnqueuePass(m_VolumeLightPass);
                // m_VolumeLightApplyPass.Setup(FarClipPlane);
                // renderer.EnqueuePass(m_VolumeLightApplyPass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            m_VolumeLightPass.CleanupLightTexture();
        }


        public class VolumeLightPass : ScriptableRenderPass
        {
            private float farClipPlane = 100f;
            private Vector3 volumeSize = Vector3.one;
            private Vector4 m_GlobalOutScatterAndExtinction;
            private float phaseG = 0.0f;
            private ComputeShader volumetricLightShader;
            private float lightIntensity = 2.0f;

            private RenderTargetIdentifier mediumTexture0;
            private RenderTargetIdentifier mediumTexture1;
            private RenderTargetIdentifier calculateTexture;
            
            // temporal
            private RenderTexture[] lightTextures = new RenderTexture[2];
            private Matrix4x4[] volumeMatrices = new Matrix4x4[2];
            private float width, height, depth;
            public VolumeLightPass(ComputeShader cs)
            {
                volumetricLightShader = cs;
                mediumTexture0 = new RenderTargetIdentifier(Shader.PropertyToID("_MediumTexture0"));
                mediumTexture1 = new RenderTargetIdentifier(Shader.PropertyToID("_MediumTexture1"));
                calculateTexture = new RenderTargetIdentifier(Shader.PropertyToID("_CalculateTexture"));
                volumeMatrices[0] = Matrix4x4.identity;
                volumeMatrices[1] = Matrix4x4.identity;
            }
            public void Setup(float far, Vector3 size, float intensity,Vector4 globalOutScatterAndExtinction, float g)
            {
                farClipPlane = far;
                volumeSize = size;
                m_GlobalOutScatterAndExtinction = globalOutScatterAndExtinction;
                phaseG = g;
                lightIntensity = intensity;
            }

            public void GenerateLightTexture()
            {
                if (lightTextures[0] == null)
                {
                    lightTextures[0] = new RenderTexture((int)volumeSize.x, (int)volumeSize.y, 0);
                    lightTextures[0].dimension = TextureDimension.Tex3D;
                    lightTextures[0].volumeDepth = (int)volumeSize.z;
                    lightTextures[0].graphicsFormat = GraphicsFormat.R16G16B16A16_UNorm;
                    lightTextures[0].enableRandomWrite = true;
                    lightTextures[0].filterMode = FilterMode.Trilinear;
                    lightTextures[0].Create();
                }

                if (lightTextures[1] == null)
                {
                    lightTextures[1] = new RenderTexture((int)volumeSize.x, (int)volumeSize.y, 0);
                    lightTextures[1].dimension = TextureDimension.Tex3D;
                    lightTextures[1].volumeDepth = (int)volumeSize.z;
                    lightTextures[1].graphicsFormat = GraphicsFormat.R16G16B16A16_UNorm;
                    lightTextures[1].enableRandomWrite = true;
                    lightTextures[1].filterMode = FilterMode.Trilinear;
                    lightTextures[1].Create();
                }
            }
            
            public void CleanupLightTexture()
            {
                lightTextures[0]?.Release();
                lightTextures[1]?.Release();
                lightTextures[0] = null;
                lightTextures[1] = null;
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
            
                cmd.GetTemporaryRT(Shader.PropertyToID("_MediumTexture0"), descriptor, FilterMode.Trilinear);
                cmd.GetTemporaryRT(Shader.PropertyToID("_MediumTexture1"), descriptor, FilterMode.Trilinear);
                cmd.GetTemporaryRT(Shader.PropertyToID("_CalculateTexture"), descriptor, FilterMode.Trilinear);
                
                GenerateLightTexture();
            } 

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get();
                var lightData = renderingData.lightData;
                var sh = RenderSettings.ambientProbe;
                

                using (new ProfilingScope(cmd, new ProfilingSampler("VolumeLightPass")))
                {
                    var cameraData = renderingData.cameraData;
                    GetVolumeMatrixVP(cameraData, out volumeMatrices[Time.frameCount % 2], out Matrix4x4 volumeMatrixInvVP);
                    Vector4 decodeParams = ComputeLogarithmicDepthDecodingParams(cameraData.camera.nearClipPlane, farClipPlane, 0.5f);
                    Vector4 encodeParams = ComputeLogarithmicDepthEncodingParams(cameraData.camera.nearClipPlane, farClipPlane, 0.5f);
                
                    cmd.SetComputeMatrixParam(volumetricLightShader,"_VolumeMatrixInvVP", volumeMatrixInvVP);
                    cmd.SetComputeMatrixParam(volumetricLightShader, "_PreVolumeMatrixVP", volumeMatrices[(Time.frameCount + 1) % 2]);
                    cmd.SetGlobalMatrix("_VolumeMatrixVP", volumeMatrices[Time.frameCount % 2]);
                
                    cmd.SetComputeVectorParam(volumetricLightShader,"_VolumeSize", volumeSize);
                    cmd.SetComputeVectorParam(volumetricLightShader,"_DecodeParams", decodeParams);
                    cmd.SetComputeVectorParam(volumetricLightShader, "_EncodeParams", encodeParams);
                    cmd.SetComputeIntParam(volumetricLightShader, "_Flag", Time.frameCount == 0 ? 0 : 1);

                    var result = PackCoefficients(sh);
                    cmd.SetGlobalVector("_SHR", result[0]);
                    cmd.SetGlobalVector("_SHG", result[1]);
                    cmd.SetGlobalVector("_SHB", result[2]);
            
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
                    
                    cmd.SetComputeIntParam(volumetricLightShader, "_AddLightCount", lightData.additionalLightsCount);
                    cmd.SetComputeFloatParam(volumetricLightShader, "_AddLightIntensity", lightIntensity);
                    
                    int index = (Time.frameCount + 1) % 2;
                    cmd.SetComputeMatrixArrayParam(volumetricLightShader,"_VolumeMatrices", matrices);
                    cmd.SetComputeVectorArrayParam(volumetricLightShader, "_LocalEmissionAndPhaseG", emissionAndPhaseG);
                    cmd.SetComputeVectorArrayParam(volumetricLightShader, "_LocalOutScatterAndExtinctions", outScatterAndExtinction);
                    cmd.SetComputeVectorParam(volumetricLightShader, "_GlobalOutScatterAndExtinction", m_GlobalOutScatterAndExtinction);
                    cmd.SetComputeIntParam(volumetricLightShader, "_LocalVolumeCount", matrices.Length);
                    cmd.SetComputeVectorParam(volumetricLightShader, "_JitterOffset", new Vector4(HaltonSequence.Get(index, 2),
                        HaltonSequence.Get(index, 3), 
                        HaltonSequence.Get(index, 5), 0));
                    cmd.SetComputeFloatParam(volumetricLightShader, "_GlobalPhaseG", phaseG);
            
                    Vector3 groupSize = new Vector3(volumeSize.x / 8, volumeSize.y / 8, volumeSize.z / 8);
            
                    int kernelIndex0 = volumetricLightShader.FindKernel("Compute0");
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex0, "_MediumTexture0", mediumTexture0);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex0, "_MediumTexture1", mediumTexture1);
                    cmd.DispatchCompute(volumetricLightShader, kernelIndex0, (int)groupSize.x, (int)groupSize.y, (int)groupSize.z);
                
                    int kernelIndex1 = volumetricLightShader.FindKernel("Compute1");
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex1, "_MediumTexture0", mediumTexture0);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex1, "_MediumTexture1", mediumTexture1);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex1, "_LightTexture", lightTextures[Time.frameCount % 2]);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex1, "_PreLightTexture", lightTextures[(Time.frameCount + 1) % 2]);
                    cmd.DispatchCompute(volumetricLightShader, kernelIndex1, (int)groupSize.x, (int)groupSize.y, (int)groupSize.z);
                
                    int kernelIndex2 = volumetricLightShader.FindKernel("Compute2");
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex2, "_LightTexture", lightTextures[Time.frameCount % 2]);
                    cmd.SetComputeTextureParam(volumetricLightShader, kernelIndex2, "_CalculateTexture", calculateTexture);
                    cmd.DispatchCompute(volumetricLightShader, kernelIndex2, (int)groupSize.x, (int)groupSize.y, 1);
                
                    cmd.SetGlobalTexture("_VolumetricLightTexture",  calculateTexture);
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

            public Vector4[] PackCoefficients(SphericalHarmonicsL2 sh)
            {
                Vector4[] result = new Vector4[3];
                for(int c = 0; c < 3; c++)
                {
                    result[c].Set(sh[c, 3], sh[c, 1], sh[c, 2], sh[c, 0] - sh[c, 6]);
                }

                return result;
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
