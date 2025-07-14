using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace PRTGI
{
    public enum ProbeVolumeDebugMode
    {
        None = 0,
        ProbeGrid = 1,
        ProbeRadiance = 2
    }

    [ExecuteAlways]
    public class ProbeVolume : MonoBehaviour
    {
        public Probe probePrefab;

        public int probeSizeX = 8;

        public int probeSizeY = 4;

        public int probeSizeZ = 8;

        public float probeGridSize = 2.0f;

        public ProbeVolumeData data;

        /// <summary>
        /// 3D Texture to store SH coefficients (replaces ComputeBuffer)
        /// Layout: [probeSizeX, probeSizeZ, probeSizeY * 9]
        /// </summary>
        public RenderTexture CoefficientVoxel3D { get; private set; }

        /// <summary>
        /// Last frame 3D texture for infinite bounce
        /// </summary>
        public RenderTexture LastFrameCoefficientVoxel3D { get; private set; }

        [Range(0.0f, 10.0f)] 
        public float skyLightIntensity = 1.0f;

        [Range(0.0f, 10.0f)] 
        public float indirectIntensity = 1.0f;

#if UNITY_EDITOR
        [SerializeField, HideInInspector] 
        internal ProbeVolumeDebugMode debugMode;

        [SerializeField, HideInInspector] 
        internal int selectedProbeIndex = -1;
        
        [SerializeField, HideInInspector] 
        internal ProbeDebugMode selectedProbeDebugMode = ProbeDebugMode.IrradianceSphere;
        
        [Range(0.5f, 3.0f), SerializeField, HideInInspector] 
        internal float probeHandleSize = 1.0f;
        
        [SerializeField, HideInInspector] 
        internal PRTBakeResolution bakeResolution = PRTBakeResolution._256;
#endif

        private const RenderTextureFormat Texture3DFormat = RenderTextureFormat.ARGBInt;

        public Probe[] Probes { get; private set; }

        private bool _isDataInitialized;

        private void Start()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            GenerateProbes();
            TryLoadSurfelData(data);
        }

#if UNITY_EDITOR
        private void Update()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            if (Application.isPlaying) return;
            if (!IsProbeValid())
            {
                GenerateProbes();
                TryLoadSurfelData(data);
            }
        }
#endif
        private void OnDestroy()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            ReleaseProbes();
            CoefficientVoxel3D?.Release();
            LastFrameCoefficientVoxel3D?.Release();
        }

#if UNITY_EDITOR
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

                // Draw probe handles for selection
                DrawProbeHandle(i, probePos);

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
        /// Draw selectable handle for probe in scene view
        /// </summary>
        /// <param name="probeIndex">Index of the probe</param>
        /// <param name="probePos">Position of the probe</param>
        private void DrawProbeHandle(int probeIndex, Vector3 probePos)
        {
            // Different colors based on selection state
            if (probeIndex == selectedProbeIndex)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(probePos, probeHandleSize * 0.15f);
                Gizmos.color = Color.yellow;
            }
            else
            {
                Gizmos.color = Color.white;
            }

            // Draw clickable handle
            Gizmos.DrawWireCube(probePos, Vector3.one * probeHandleSize * 0.1f);

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
#endif

        /// <summary>
        /// Check if the probe volume is valid
        /// </summary>
        /// <returns>True if the probe volume is valid</returns>
        private bool IsProbeValid()
        {
            if (Probes == null || !Probes.Any()) return false;
            return CoefficientVoxel3D != null && LastFrameCoefficientVoxel3D != null;
        }

        /// <summary>
        /// Check if the probe volume is valid
        /// </summary>
        /// <returns>True if the probe volume is valid</returns>
        public bool IsActivate()
        {
#if UNITY_EDITOR
            if (debugMode == ProbeVolumeDebugMode.ProbeRadiance) return false;
#endif
            return enabled && IsProbeValid() && _isDataInitialized;
        }

        /// <summary>
        /// Pack all probe's data to 1D array using efficient memory reinterpretation
        /// </summary>
        /// <param name="volumeData">ProbeVolumeData to store the data</param>
        public void StorageSurfelData(ProbeVolumeData volumeData)
        {
            int probeNum = probeSizeX * probeSizeY * probeSizeZ;
            int surfelPerProbe = 512;
            int floatPerSurfel = 10;
            Array.Resize(ref volumeData.surfelStorageBuffer, probeNum * surfelPerProbe * floatPerSurfel);

            int destinationIndex = 0;
            for (int i = 0; i < Probes.Length; i++)
            {
                Probe probe = Probes[i];
                if (probe.readBackBuffer != null && probe.readBackBuffer.Length > 0)
                {
                    // Create NativeArray from the surfel array
                    using var surfelNativeArray = new NativeArray<Surfel>(probe.readBackBuffer, Allocator.Temp);

                    // Reinterpret as float array
                    var floatNativeArray = surfelNativeArray.Reinterpret<float>(UnsafeUtility.SizeOf<Surfel>());

                    // Copy to destination buffer
                    NativeArray<float>.Copy(floatNativeArray, 0, volumeData.surfelStorageBuffer, destinationIndex, floatNativeArray.Length);

                    destinationIndex += floatNativeArray.Length;
                }
            }

            volumeData.volumePosition = transform.position;
        }

        /// <summary>
        /// load surfel data from storage using efficient memory reinterpretation
        /// </summary>
        /// <param name="surfelData"></param>
        private void TryLoadSurfelData(ProbeVolumeData surfelData)
        {
            var surfelStorageBuffer = surfelData.surfelStorageBuffer;
            int probeNum = probeSizeX * probeSizeY * probeSizeZ;
            int surfelPerProbe = 512;
            int floatPerSurfel = 10;
            bool dataDirty = surfelStorageBuffer.Length != probeNum * surfelPerProbe * floatPerSurfel;
            bool posDirty = transform.position != surfelData.volumePosition;
            if (posDirty || dataDirty)
            {
                _isDataInitialized = false;
                Debug.LogWarning("Volume data is out of date, please regenerate it.");
                return;
            }

            int sourceIndex = 0;
            foreach (var probe in Probes)
            {
                if (probe.readBackBuffer != null && probe.readBackBuffer.Length > 0)
                {
                    int surfelDataLength = probe.readBackBuffer.Length * floatPerSurfel;

                    // Create NativeArray from the surfel array
                    using var surfelNativeArray = new NativeArray<Surfel>(probe.readBackBuffer.Length, Allocator.Temp);

                    // Reinterpret as float array
                    var floatNativeArray = surfelNativeArray.Reinterpret<float>(UnsafeUtility.SizeOf<Surfel>());

                    // Copy from source buffer to surfel data
                    NativeArray<float>.Copy(surfelStorageBuffer, sourceIndex, floatNativeArray, 0, surfelDataLength);

                    probe.readBackBuffer = surfelNativeArray.ToArray();
                    sourceIndex += surfelDataLength;
                }

                probe.surfels.SetData(probe.readBackBuffer);
            }

            _isDataInitialized = true;
        }

        private void ReleaseProbes()
        {
            if (Probes != null)
            {
                foreach (var probe in Probes)
                {
                    if (probe)
                    {
                        DestroyImmediate(probe.gameObject);
                    }
                }
            }

            Probes = null;
        }

        /// <summary>
        /// Create probes based on volume current location.
        /// </summary>
        private void GenerateProbes()
        {
            ReleaseProbes();

            CoefficientVoxel3D?.Release();
            LastFrameCoefficientVoxel3D?.Release();

            int probeNum = probeSizeX * probeSizeY * probeSizeZ;

            // generate probe actors
            Probes = new Probe[probeNum];
            for (int x = 0; x < probeSizeX; x++)
            {
                for (int y = 0; y < probeSizeY; y++)
                {
                    for (int z = 0; z < probeSizeZ; z++)
                    {
                        Vector3 relativePos = new Vector3(x, y, z) * probeGridSize;
                        Vector3 parentPos = transform.position;

                        // setup probe
                        int index = x * probeSizeY * probeSizeZ + y * probeSizeZ + z;
                        Probes[index] = Instantiate(probePrefab, gameObject.transform);
                        Probes[index].gameObject.hideFlags = HideFlags.HideAndDontSave;
                        Probes[index].transform.position = relativePos + parentPos;
                        Probes[index].indexInProbeVolume = index;
                        Probes[index].TryInit();
                    }
                }
            }

            // Create 3D textures for SH coefficients
            // Layout: [probeSizeX, probeSizeZ, probeSizeY * 9 * 3]
            // Each depth slice corresponds to one RGB component of SH coefficient
            int depth = probeSizeY * 9; // 9 SH coefficients per probe

            CoefficientVoxel3D = new RenderTexture(probeSizeX, probeSizeZ, 0, Texture3DFormat)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = depth,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            CoefficientVoxel3D.Create();

            LastFrameCoefficientVoxel3D = new RenderTexture(probeSizeX, probeSizeZ, 0, Texture3DFormat)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = depth,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            LastFrameCoefficientVoxel3D.Create();
        }

        /// <summary>
        /// Precompute surfel and bake into <see cref="ProbeVolumeData"/> using PRTBaker
        /// </summary>
        /// <param name="prtBaker">PRTBaker instance to use for baking</param>
        public void BakeData(IPRTBaker prtBaker)
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
                probe.GetComponent<MeshRenderer>().enabled = false;
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

        /// <summary>
        /// Clear surfel and <see cref="ProbeVolumeData"/>
        /// </summary>
        public void ClearData()
        {
            data.Clear();
            GenerateProbes();
        }

        public void ClearCoefficientVoxel(CommandBuffer cmd)
        {
            // Clear 3D texture
            cmd.SetRenderTarget(CoefficientVoxel3D, 0, CubemapFace.Unknown, -1);
            cmd.ClearRenderTarget(false, true, Color.black);
        }

        /// <summary>
        /// Swap last frame voxel with current voxel
        /// </summary>
        public void SwapLastFrameCoefficientVoxel()
        {
            // Swap 3D textures
            (LastFrameCoefficientVoxel3D, CoefficientVoxel3D) = (CoefficientVoxel3D, LastFrameCoefficientVoxel3D);
        }

        public Vector3 GetVoxelMinCorner()
        {
            return transform.position;
        }
    }
}