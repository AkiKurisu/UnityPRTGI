using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UObject = UnityEngine.Object;

public class PRTCompositePass : ScriptableRenderPass, IDisposable
{
    public PRTCompositePass()
    {
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        _blitMaterial = CoreUtils.CreateEngineMaterial("CasualPRT/Composite");
        profilingSampler = new ProfilingSampler("PRT Composite (Preview)");
    }

    private readonly Material _blitMaterial;
    private RTHandle _tempRTHandle;
    private RTHandle _blitSrc;

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        cameraTargetDescriptor.depthBufferBits = (int)DepthBits.None;
        RenderingUtils.ReAllocateIfNeeded(ref _tempRTHandle, cameraTargetDescriptor,
            wrapMode: TextureWrapMode.Clamp, name: "RPTLightingTexture");

        _blitSrc = renderingData.cameraData.renderer.cameraColorTargetHandle;

        ConfigureTarget(_blitSrc);
        ConfigureClear(ClearFlag.None, Color.clear);
    }

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        ConfigureInput(ScriptableRenderPassInput.Depth);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, profilingSampler))
        {
            ProbeVolume volume = UObject.FindFirstObjectByType<ProbeVolume>();
            if (volume != null && volume.IsActivate())
            {
                Blitter.BlitCameraTexture(cmd, _blitSrc, _tempRTHandle, _blitMaterial, 0);
                Blitter.BlitCameraTexture(cmd, _tempRTHandle, _blitSrc);
            }
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public void Dispose()
    {
        _tempRTHandle?.Release();
    }
}



