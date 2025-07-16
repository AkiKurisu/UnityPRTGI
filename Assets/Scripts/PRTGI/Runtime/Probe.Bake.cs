#if UNITY_EDITOR
using UnityEngine;

namespace PRTGI
{
    public partial class Probe
    {
        /// <summary>
        /// Bake surfels data using PRTBaker
        /// </summary>
        /// <param name="prtBaker">PRTBaker instance to use for capture</param>
        internal void BakeData(IPRTBaker prtBaker)
        {
            if (prtBaker == null || !prtBaker.IsInitialized())
            {
                Debug.LogError("PRTBaker is null or not initialized");
                return;
            }

            ReAllocateIfNeeded();

            // Use PRTBaker to capture cubemaps
            var bakeResult = prtBaker.BakeAtPoint(transform.position);

            // Sample surfels using PRTBaker's render textures
            SampleSurfels(bakeResult.WorldPosRT, bakeResult.NormalRT, bakeResult.AlbedoRT);
        }

        /// <summary>
        /// sample surfel from gbuffer cubemaps
        /// </summary>
        /// <param name="worldPosCubemap"></param>
        /// <param name="normalCubemap"></param>
        /// <param name="albedoCubemap"></param>
        private void SampleSurfels(RenderTexture worldPosCubemap, RenderTexture normalCubemap,
            RenderTexture albedoCubemap)
        {
            var kid = surfelSampleCS.FindKernel("CSMain");

            // set necessary data and start sample
            Vector3 p = gameObject.transform.position;
            surfelSampleCS.SetVector(ShaderProperties.ProbePos, new Vector4(p.x, p.y, p.z, 1.0f));
            surfelSampleCS.SetFloat(ShaderProperties.RandSeed, Random.Range(0.0f, 1.0f));
            surfelSampleCS.SetTexture(kid, ShaderProperties.WorldPosCubemap, worldPosCubemap);
            surfelSampleCS.SetTexture(kid, ShaderProperties.NormalCubemap, normalCubemap);
            surfelSampleCS.SetTexture(kid, ShaderProperties.AlbedoCubemap, albedoCubemap);
            surfelSampleCS.SetBuffer(kid, ShaderProperties.Surfels, _surfels);

            // start CS
            surfelSampleCS.Dispatch(kid, 1, 1, 1);

            // readback
            _surfels.GetData(ReadBackBuffer);
        }
    }
}
#endif