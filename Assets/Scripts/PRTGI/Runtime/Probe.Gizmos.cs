#if UNITY_EDITOR
using UnityEngine;

namespace PRTGI
{
    public partial class Probe
    {
        /// <summary>
        /// Update MeshRenderer visibility based on debug mode
        /// </summary>
        private void UpdateMeshRendererVisibility()
        {
            if (!_renderer)
                return;

            // Show irradiance sphere only when IrradianceSphere debug mode is enabled
            bool shouldShowIrradianceSphere = _volume.debugMode == ProbeVolumeDebugMode.ProbeRadiance;
            bool isSelected = _volume.selectedProbeIndex == indexInProbeVolume && indexInProbeVolume != -1;
            // Hide when is selected and using other debug modes
            shouldShowIrradianceSphere &= !isSelected || _volume.selectedProbeDebugMode == ProbeDebugMode.IrradianceSphere;
            _renderer.enabled = shouldShowIrradianceSphere;

            // Update material properties if sphere is visible
            if (shouldShowIrradianceSphere)
            {
                UpdateIrradianceSphereShader();
            }
        }

        /// <summary>
        /// Update irradiance sphere shader properties
        /// </summary>
        private void UpdateIrradianceSphereShader()
        {
            if (!_renderer || !_renderer.sharedMaterial)
                return;

            _renderer.sharedMaterial.shader = Shader.Find("CasualPRT/SHDebug");

            if (_coefficientSH9 != null)
            {
                _matPropBlock.SetBuffer(CoefficientSH9, _coefficientSH9);
                _renderer.SetPropertyBlock(_matPropBlock);
            }
        }

        /// <summary>
        /// Draw debug visualization based on current debug mode
        /// </summary>
        /// <param name="debugMode">Debug mode for visualization</param>
        /// <param name="probePos">Position of the probe</param>
        public void DrawDebugVisualization(ProbeDebugMode debugMode, Vector3 probePos)
        {
            if (!ValidateDebugBuffers())
                return;

            // Read back data from GPU
            surfels.GetData(readBackBuffer);
            _surfelRadiance.GetData(_radianceDebugBuffer);

            // Draw based on debug mode
            switch (debugMode)
            {
                case ProbeDebugMode.SphereDistribution:
                    DrawSphereDistribution(probePos);
                    break;
                case ProbeDebugMode.SampleDirection:
                    DrawSampleDirections(probePos);
                    break;
                case ProbeDebugMode.Surfel:
                    DrawSurfels(probePos);
                    break;
                case ProbeDebugMode.SurfelRadiance:
                    DrawSurfelRadiance(probePos);
                    break;
            }
        }

        /// <summary>
        /// Validate that debug buffers are properly initialized
        /// </summary>
        /// <returns>True if buffers are valid</returns>
        private bool ValidateDebugBuffers()
        {
            if (surfels == null || _surfelRadiance == null)
            {
                Debug.LogWarning($"Debug buffers not initialized for probe {name}");
                return false;
            }

            if (readBackBuffer == null || _radianceDebugBuffer == null)
            {
                Debug.LogWarning($"Debug readback buffers not initialized for probe {name}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Draw sphere distribution debug visualization
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        private void DrawSphereDistribution(Vector3 probePos)
        {
            for (int i = 0; i < RayNum; i++)
            {
                Vector3 dir = GetSurfelDirection(readBackBuffer[i], probePos);
                bool isSky = IsSky(readBackBuffer[i]);

                Gizmos.color = isSky ? skyColor : defaultColor;
                Gizmos.DrawSphere(dir + probePos, sphereSize);
            }
        }

        /// <summary>
        /// Draw sample directions debug visualization
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        private void DrawSampleDirections(Vector3 probePos)
        {
            for (int i = 0; i < RayNum; i++)
            {
                Surfel surfel = readBackBuffer[i];
                Vector3 dir = GetSurfelDirection(surfel, probePos);
                bool isSky = IsSky(surfel);

                Gizmos.color = isSky ? skyColor : defaultColor;

                if (isSky)
                {
                    Gizmos.DrawLine(probePos, probePos + dir * SkyRayLength);
                }
                else
                {
                    Gizmos.DrawLine(probePos, surfel.position);
                    Gizmos.DrawSphere(surfel.position, SurfelSize);
                }
            }
        }

        /// <summary>
        /// Draw surfels debug visualization
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        private void DrawSurfels(Vector3 probePos)
        {
            Gizmos.color = defaultColor;

            for (int i = 0; i < RayNum; i++)
            {
                Surfel surfel = readBackBuffer[i];
                if (IsSky(surfel))
                    continue;

                // Draw surfel position
                Gizmos.DrawSphere(surfel.position, SurfelSize);

                // Draw normal
                Gizmos.color = normalColor;
                Gizmos.DrawLine(surfel.position, surfel.position + surfel.normal * NormalLength);
                Gizmos.color = defaultColor;
            }
        }

        /// <summary>
        /// Draw surfel radiance debug visualization
        /// </summary>
        /// <param name="probePos">Position of the probe</param>
        private void DrawSurfelRadiance(Vector3 probePos)
        {
            for (int i = 0; i < RayNum; i++)
            {
                Surfel surfel = readBackBuffer[i];
                if (IsSky(surfel))
                    continue;

                Vector3 radiance = _radianceDebugBuffer[i];
                Gizmos.color = new Color(radiance.x, radiance.y, radiance.z, 1.0f);
                Gizmos.DrawSphere(surfel.position, SurfelSize);
            }
        }

        /// <summary>
        /// Get normalized direction from probe to surfel
        /// </summary>
        /// <param name="surfel">Surfel data</param>
        /// <param name="probePos">Probe position</param>
        /// <returns>Normalized direction vector</returns>
        private Vector3 GetSurfelDirection(Surfel surfel, Vector3 probePos)
        {
            Vector3 dir = surfel.position - probePos;
            return dir.normalized;
        }

        /// <summary>
        /// Check if surfel represents sky
        /// </summary>
        /// <param name="surfel">Surfel data</param>
        /// <returns>True if surfel is sky</returns>
        private bool IsSky(Surfel surfel)
        {
            return surfel.skyMask >= SkyMaskThreshold;
        }
    }
}
#endif