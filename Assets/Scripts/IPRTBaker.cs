using UnityEngine;

namespace PRTGI
{
    /// <summary>
    /// RPT bake cubemap resolution
    /// </summary>
    public enum PRTBakeResolution
    {
        _256 = 256,
        _512 = 512
    }

    public interface IPRTBaker
    {
        /// <summary>
        /// Check if the baker is properly initialized.
        /// </summary>
        /// <returns>True if initialized</returns>
        bool IsInitialized();

        /// <summary>
        /// Update baking progress.
        /// </summary>
        /// <param name="status">Status message</param>
        /// <param name="progress">Progress value</param>
        void UpdateProgress(string status, float progress);
        
        /// <summary>
        /// Bake G-buffer cubemaps at the specified position.
        /// </summary>
        /// <param name="position">Position to capture cubemaps from</param>
        /// <returns>Bake result cubemaps</returns>
        (RenderTexture WorldPosRT, RenderTexture NormalRT, RenderTexture AlbedoRT) BakeAtPoint(Vector3 position);
    }
}