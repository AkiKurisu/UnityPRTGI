using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UObject = UnityEngine.Object;

public class PRTRelightPass : ScriptableRenderPass
{
    public PRTRelightPass()
    {
        profilingSampler = new ProfilingSampler("PRT Relight");
        renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }
    
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = CommandBufferPool.Get();
        
        DoRelight(cmd);

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    private static void DoRelight(CommandBuffer cmd)
    {
        ProbeVolume volume = UObject.FindFirstObjectByType<ProbeVolume>();
        if (volume == null || !volume.IsActivate()) return;
        
        volume.SwapLastFrameCoefficientVoxel();
        volume.ClearCoefficientVoxel(cmd);

        Vector3 corner = volume.GetVoxelMinCorner();
        Vector4 voxelCorner = new Vector4(corner.x, corner.y, corner.z, 0);
        Vector4 voxelSize = new Vector4(volume.probeSizeX, volume.probeSizeY, volume.probeSizeZ, 0);
        cmd.SetGlobalFloat("_coefficientVoxelGridSize", volume.probeGridSize);
        cmd.SetGlobalVector("_coefficientVoxelSize", voxelSize);
        cmd.SetGlobalVector("_coefficientVoxelCorner", voxelCorner);
        cmd.SetGlobalBuffer("_coefficientVoxel", volume.CoefficientVoxel);
        cmd.SetGlobalBuffer("_lastFrameCoefficientVoxel", volume.LastFrameCoefficientVoxel);
        cmd.SetGlobalFloat("_skyLightIntensity", volume.skyLightIntensity);
        cmd.SetGlobalFloat("_GIIntensity", volume.indirectIntensity);
            
        foreach (var probe in volume.Probes)
        {
            if (probe == null) continue;
            probe.TryInit();
            probe.ReLight(cmd);
        }
    }
}