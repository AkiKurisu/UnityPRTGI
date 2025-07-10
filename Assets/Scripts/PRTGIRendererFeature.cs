using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleRendererFeature("PRT Global Illumination")]
public class PRTGIRendererFeature : ScriptableRendererFeature
{
    private PRTRelightPass _relightPass;

    private PRTCompositePass _compositePass;
    
    public override void Create()
    {
        _relightPass = new PRTRelightPass();
        _compositePass = new PRTCompositePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType is CameraType.Reflection or CameraType.Preview) return;
        renderer.EnqueuePass(_relightPass);
        renderer.EnqueuePass(_compositePass);
    }
}

