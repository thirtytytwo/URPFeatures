using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class FrustemPreviewFeature : ScriptableRendererFeature
{
    public ComputeShader VolumetricLightShader;
    [Range(10f, 100f)]public float FarClipPlane = 100;
    public int VolumeSize = 64;
    
    private FrustemPreviewPass volumetricLightPass;
    public override void Create()
    {
        volumetricLightPass = new FrustemPreviewPass(VolumetricLightShader);
        volumetricLightPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData.camera;
        if (camera.CompareTag("MainCamera"))
        {
            volumetricLightPass.Setup(FarClipPlane, VolumeSize);
            renderer.EnqueuePass(volumetricLightPass);
        }
    }
    
    public class FrustemPreviewPass : ScriptableRenderPass
    {
        public static List<Vector4> VolumePositions = new List<Vector4>();
        
        private float farClipPlane = 100f;
        private int volumeSize = 64;
        private ComputeShader volumetricLightShader;
        public FrustemPreviewPass(ComputeShader cs)
        {
            volumetricLightShader = cs;
        }
        
        public void Setup(float far, int size)
        {
            farClipPlane = far;
            volumeSize = size;
            // Clear previous volume positions
            VolumePositions.Clear();
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            
            var cameraData = renderingData.cameraData;
            var volumeMatrixVP = GetVolumeMatrixVP(cameraData);
            Vector4 decodeParams = ComputeLogarithmicDepthDecodingParams(cameraData.camera.nearClipPlane, farClipPlane, 0.5f);
            
            int kernelIndex = volumetricLightShader.FindKernel("CSMain");
            
            volumetricLightShader.SetMatrix("_VolumeMatrixInvVP", volumeMatrixVP);
            volumetricLightShader.SetVector("_VolumeSize", new Vector3(volumeSize, volumeSize, volumeSize));
            volumetricLightShader.SetVector("_DecodeParams", decodeParams);
            
            Matrix4x4[] matrices = new Matrix4x4[LocalVolume.AllVolumes.Count];
            for(int i = 0; i < LocalVolume.AllVolumes.Count; i++)
            {
                var volume = LocalVolume.AllVolumes[i];
                if (volume == null) continue;
                
                matrices[i] = volume.VolumeMatrix;
            }
            
            volumetricLightShader.SetMatrixArray("_VolumeMatrices", matrices);
            volumetricLightShader.SetInt("_LocalVolumeCount", matrices.Length);
            
            int bufferSize = (volumeSize * volumeSize * volumeSize);
            ComputeBuffer volumeBuffer = new ComputeBuffer(bufferSize, sizeof(float) * 4);
            // cmd.SetComputeBufferParam(volumetricLightShader, kernelIndex, "Result", volumeBuffer);
            int groupSize = volumeSize / 8;
            // cmd.DispatchCompute(volumetricLightShader, kernelIndex, groupSize, groupSize, groupSize);
            volumetricLightShader.SetBuffer(kernelIndex, "Result", volumeBuffer);
            volumetricLightShader.Dispatch(kernelIndex, groupSize, groupSize, groupSize);

            Vector4[] volumePositionsArray = new Vector4[bufferSize];
            volumeBuffer.GetData(volumePositionsArray);
            volumeBuffer.Dispose();
            
            VolumePositions.Clear();
            VolumePositions.AddRange(volumePositionsArray);
        }

        public Matrix4x4 GetVolumeMatrixVP(CameraData cameraData)
        {
            var camera = cameraData.camera;
            var viewMatrix = camera.worldToCameraMatrix;
            var projectionMatrix = Matrix4x4.Perspective(camera.fieldOfView, camera.aspect, camera.nearClipPlane, farClipPlane);
            
            return viewMatrix.inverse * projectionMatrix.inverse;
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
        
    }
}
