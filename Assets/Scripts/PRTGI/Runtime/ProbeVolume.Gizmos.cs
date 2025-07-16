#if UNITY_EDITOR
using UnityEngine;

namespace PRTGI
{
    public partial class ProbeVolume
    {
        private void OnDrawGizmos()
        {
            if (!gameObject.scene.IsValid()) return;
            if (debugMode == ProbeVolumeDebugMode.None)
                return;

            if (Probes == null)
                return;

            for (int i = 0; i < Probes.Length; i++)
            {
                if (Probes[i] == null)
                    continue;

                Vector3 probePos = Probes[i].transform.position;

                // Draw probe index
                DrawProbeIndex(i, probePos);

                // Draw debug visualization based on mode
                if (debugMode == ProbeVolumeDebugMode.ProbeGrid)
                {
                    DrawProbeGrid(i, probePos);
                }
                else if (debugMode == ProbeVolumeDebugMode.ProbeRadiance)
                {
                    DrawProbeRadiance(i, probePos);
                }
            }

            if (selectedProbeIndex != -1 && debugMode == ProbeVolumeDebugMode.ProbeRadiance)
            {
                var probe = Probes[selectedProbeIndex];
                Vector3 probePos = probe.transform.position;

                // Draw debug visualization based on selected mode (excluding IrradianceSphere which is handled by MeshRenderer)
                if (selectedProbeDebugMode != ProbeDebugMode.IrradianceSphere)
                {
                    probe.DrawDebugVisualization(selectedProbeDebugMode, probePos);
                }
            }
        }

        /// <summary>
        /// Draw probe index in scene view
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private static void DrawProbeIndex(int probeIndex, Vector3 probePos)
        {
            // Draw probe index label
            UnityEditor.Handles.Label(probePos + Vector3.up * 0.5f, probeIndex.ToString());
        }

        /// <summary>
        /// Draw probe grid visualization
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeGrid(int probeIndex, Vector3 probePos)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.1f);

            // Draw connections to neighboring probes
            DrawProbeConnections(probeIndex, probePos);
        }

        /// <summary>
        /// Draw probe radiance visualization
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeRadiance(int probeIndex, Vector3 probePos)
        {
            // This would show radiance data if available
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(probePos, probeHandleSize * 0.08f);
        }

        /// <summary>
        /// Draw connections between neighboring probes
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeConnections(int probeIndex, Vector3 probePos)
        {
            var (x, y, z) = IndexToCoordinate(probeIndex);

            // Draw lines to neighboring probes
            Gizmos.color = Color.cyan * 0.5f;

            // X neighbors
            if (x > 0)
            {
                int neighborIndex = CoordinateToIndex(x - 1, y, z);
                Gizmos.DrawLine(probePos, Probes[neighborIndex].transform.position);
            }

            // Y neighbors
            if (y > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y - 1, z);
                Gizmos.DrawLine(probePos, Probes[neighborIndex].transform.position);
            }

            // Z neighbors
            if (z > 0)
            {
                int neighborIndex = CoordinateToIndex(x, y, z - 1);
                Gizmos.DrawLine(probePos, Probes[neighborIndex].transform.position);
            }
        }

        /// <summary>
        /// Convert probe index to 3D coordinates
        /// </summary>
        /// <param name="index">Probe index</param>
        /// <returns>3D coordinates (x, y, z)</returns>
        private (int x, int y, int z) IndexToCoordinate(int index)
        {
            int x = index / (probeSizeY * probeSizeZ);
            int remainder = index % (probeSizeY * probeSizeZ);
            int y = remainder / probeSizeZ;
            int z = remainder % probeSizeZ;
            return (x, y, z);
        }

        /// <summary>
        /// Convert 3D coordinates to probe index
        /// </summary>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="z">Z coordinate</param>
        /// <returns>Probe index</returns>
        private int CoordinateToIndex(int x, int y, int z)
        {
            return x * probeSizeY * probeSizeZ + y * probeSizeZ + z;
        }
    }
}
#endif
