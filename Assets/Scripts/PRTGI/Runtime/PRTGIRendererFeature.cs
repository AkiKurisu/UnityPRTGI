using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PRTGI
{
    [DisallowMultipleRendererFeature("PRT Global Illumination")]
    public class PRTGIRendererFeature : ScriptableRendererFeature
    {
        private PRTRelightPass _relightPass;

        private PRTCompositePass _compositePass;

        [SerializeField]
        private bool enablePreview;

        public override void Create()
        {
            _relightPass = new PRTRelightPass();
            _compositePass = new PRTCompositePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType is CameraType.Reflection or CameraType.Preview) return;
            renderer.EnqueuePass(_relightPass);
            if (enablePreview)
            {
                renderer.EnqueuePass(_compositePass);
            }
        }
    }
}

