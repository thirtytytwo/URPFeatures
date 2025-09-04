using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricLightRayMarchingFeature : ScriptableRendererFeature
{
    class VolumetricLightRayMarchingPass : ScriptableRenderPass
    {
        private Material mat;
        private ProfilingSampler sampler = new ProfilingSampler("VolumetricLightRayMarchingPass");
        private int VolumeLightRTID = Shader.PropertyToID("_VolumeLightRT");
        private int NoiseTextureID = Shader.PropertyToID("_NoiseTexture");
        private Texture2D[] noises;
        
        private float phaseG;
        private float transmittance;
        public VolumetricLightRayMarchingPass(Material material,Texture2D[] noises)
        {
            mat = material;
            this.noises = noises;
        }

        public void Setup(float g, float tr)
        {
            phaseG = g;
            transmittance = tr;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            desc.width >>= 1;
            desc.height >>= 1;
            cmd.GetTemporaryRT(VolumeLightRTID, desc, FilterMode.Bilinear);
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            var camera = renderingData.cameraData.camera;
            using (new ProfilingScope(cmd, sampler))
            {
                cmd.SetGlobalTexture(NoiseTextureID, noises[Time.frameCount % noises.Length]);
                cmd.SetRenderTarget(VolumeLightRTID);
                var tanFov = Mathf.Tan(camera.fieldOfView / 2 * Mathf.Deg2Rad);
                var tanFovWidth = tanFov * camera.aspect;
                cmd.SetGlobalVector("_DepthParams", new  Vector4(tanFovWidth, tanFov, 0, 0));
                cmd.SetGlobalFloat("_PhaseG", phaseG);
                cmd.SetGlobalFloat("_Transmittance", transmittance);
                cmd.DrawMesh(RenderingUtils.fastfullscreenMesh, Matrix4x4.identity, mat, 0, 0);
                cmd.SetRenderTarget(renderingData.cameraData.renderer.GetCameraColorFrontBuffer(cmd));
                cmd.SetGlobalTexture("_Source", renderingData.cameraData.renderer.cameraColorTarget);
                var textureSize = new Vector4(1.0f / renderingData.cameraData.cameraTargetDescriptor.width, 1.0f / renderingData.cameraData.cameraTargetDescriptor.height, 
                    renderingData.cameraData.cameraTargetDescriptor.width, renderingData.cameraData.cameraTargetDescriptor.height);
                cmd.SetGlobalVector("_TextureSize", textureSize);
                cmd.SetGlobalTexture("_VolumeLightRT", VolumeLightRTID);
                cmd.DrawMesh(RenderingUtils.fastfullscreenMesh, Matrix4x4.identity, mat, 0, 1);
                renderingData.cameraData.renderer.SwapColorBuffer(cmd);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(VolumeLightRTID);
        }
    }

    VolumetricLightRayMarchingPass executePass;
    public Material RayMarchingMaterial;
    public Texture2D[] Noises;
    [Range(-1.0f, 1.0f)]public float PhaseG = 0.5f;
    [Range(0.0f, 1.0f)]public float Transmittance = 0.5f;

    /// <inheritdoc/>
    public override void Create()
    {
        if (RayMarchingMaterial == null)
        {
            Debug.LogError("RayMarchingMaterial is null");
            return;
        }
        executePass = new VolumetricLightRayMarchingPass(RayMarchingMaterial, Noises);
        executePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        executePass.Setup(PhaseG, Transmittance);
        renderer.EnqueuePass(executePass);
    }
}


