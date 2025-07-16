#if UNITY_EDITOR
using System.Linq;
using UnityEngine;

namespace PRTGI
{
    public partial class ProbeVolume
    {
        /// <summary>
        /// Precompute surfel and bake into <see cref="ProbeVolumeData"/> using PRTBaker
        /// </summary>
        /// <param name="prtBaker">PRTBaker instance to use for baking</param>
        internal void BakeData(IPRTBaker prtBaker)
        {
            if (prtBaker == null || !prtBaker.IsInitialized())
            {
                Debug.LogError("PRTBaker is null or not initialized");
                return;
            }

            if (!Probes.Any())
            {
                GenerateProbes();
            }

            // Hide debug spheres
            foreach (var probe in Probes)
            {
                probe.Renderer.enabled = false;
            }

            prtBaker.UpdateProgress($"Baking {Probes.Length} probes in volume", 0.0f);

            // Capture surfel using PRTBaker for each probe
            for (int i = 0; i < Probes.Length; i++)
            {
                var probe = Probes[i];
                float progress = (float)i / Probes.Length;
                prtBaker.UpdateProgress($"Baking probe {i + 1}/{Probes.Length} at {probe.transform.position}", progress);

                probe.BakeData(prtBaker);
            }

            prtBaker.UpdateProgress("Storing surfel data...", 1.0f);
        }
    }
}
#endif