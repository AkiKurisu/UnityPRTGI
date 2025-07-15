using System;
using System.Collections.Generic;
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
    public partial class ProbeVolume : MonoBehaviour
    {
        private readonly struct Grid
        {
            public readonly int X;

            public readonly int Y;

            public readonly int Z;

            public readonly float Size;

            public Grid(int x, int y, int z, float size)
            {
                X = x;
                Y = y;
                Z = z;
                Size = size;
            }

            public bool Equals(Grid other)
            {
                return X == other.X && Y == other.Y && Z == other.Z && Size.Equals(other.Size);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(X, Y, Z, Size);
            }
        }

        private readonly RenderTextureHistoryBuffer _historyBuffer = new();

        public Probe probePrefab;

        public int probeSizeX = 8;

        public int probeSizeY = 4;

        public int probeSizeZ = 8;

        public float probeGridSize = 2.0f;

        private Grid _grid;

        public ProbeVolumeData data;

        /// <summary>
        /// 3D Texture to write SH coefficients
        /// Layout: [probeSizeX, probeSizeZ, probeSizeY * 9]
        /// </summary>
        public RenderTexture WriteCoefficientVoxel3D => _historyBuffer.WriteFrame;

        /// <summary>
        /// 3D Texture to store SH coefficients
        /// Layout: [probeSizeX, probeSizeZ, probeSizeY * 9]
        /// </summary>
        public RenderTexture CurrentFrameCoefficientVoxel3D => _historyBuffer.WriteFrame;

        /// <summary>
        /// Last frame 3D texture for infinite bounce
        /// </summary>
        public RenderTexture LastFrameCoefficientVoxel3D => _historyBuffer.CurrentFrame;

        [Range(0.0f, 10.0f)]
        public float skyLightIntensity = 1.0f;

        [Range(0.0f, 10.0f)]
        public float indirectIntensity = 1.0f;

        // Probe update rotation for performance optimization
        private int _currentProbeUpdateIndex;

        public bool multiFrameRelight;

        [Range(1, 100)]
        public int probesPerFrameUpdate = 2;

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

        private uint _frameCount;

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
            if (!gameObject.scene.IsValid()) return;
            if (Application.isPlaying) return;
            if (!IsProbeValid())
            {
                GenerateProbes();
                TryLoadSurfelData(data);
            }
            
            var currentGrid = new Grid(probeSizeX, probeSizeY, probeSizeZ, probeGridSize);
            if (!currentGrid.Equals(_grid))
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
            _historyBuffer.Release();
        }

        /// <summary>
        /// Check if the probe volume is valid
        /// </summary>
        /// <returns>True if the probe volume is valid</returns>
        private bool IsProbeValid()
        {
            if (Probes == null || !Probes.Any()) return false;
            return _historyBuffer?.IsInitialized ?? false;
        }

        /// <summary>
        /// Check if the probe volume is valid
        /// </summary>
        /// <returns>True if the probe volume is valid</returns>
        public bool IsActivate()
        {
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
            using var surfelStorageBuffer = new NativeArray<Surfel>(probeNum * surfelPerProbe, Allocator.Temp);

            int destinationIndex = 0;
            foreach (var probe in Probes)
            {
                // Copy to destination buffer
                NativeArray<Surfel>.Copy(probe.readBackBuffer, 0, surfelStorageBuffer, destinationIndex, probe.readBackBuffer.Length);
                destinationIndex += probe.readBackBuffer.Length;
            }

            volumeData.surfelStorageBuffer = surfelStorageBuffer.Reinterpret<float>(UnsafeUtility.SizeOf<Surfel>()).ToArray();
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

            // Create NativeArray from the surfel buffer
            using var surfelFloatStorageArray = new NativeArray<float>(surfelData.surfelStorageBuffer, Allocator.Temp);
            var surfelStorageArray = surfelFloatStorageArray.Reinterpret<Surfel>(UnsafeUtility.SizeOf<float>());

            int sourceIndex = 0;
            foreach (var probe in Probes)
            {
                // Copy from source buffer to surfel data
                NativeArray<Surfel>.Copy(surfelStorageArray, sourceIndex, probe.readBackBuffer, 0, probe.readBackBuffer.Length);
                probe.surfels.SetData(probe.readBackBuffer);
                sourceIndex += probe.readBackBuffer.Length;
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

            _grid = new Grid(probeSizeX, probeSizeY, probeSizeZ, probeGridSize);

            _historyBuffer.Release();

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
                        Probes[index].ReAllocateIfNeeded();
                    }
                }
            }

            // Create 3D textures for SH coefficients
            // Layout: [probeSizeX, probeSizeZ, probeSizeY * 9 * 3]
            // Each depth slice corresponds to one RGB component of SH coefficient
            int volumeDepth = probeSizeY * 9; // 9 SH coefficients per probe

            _historyBuffer.Initialize(probeSizeX, probeSizeZ, 0, Texture3DFormat, TextureDimension.Tex3D, volumeDepth);

            // Reset frame count when historyBuffer are re-allocated
            _frameCount = 0;

            // Reset probe update rotation when new probes are generated
            ResetProbeUpdateRotation();
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
            if (multiFrameRelight && _frameCount >= 2) return;

            // Clear 3D texture
            _historyBuffer.ClearWriteBuffer(cmd, Color.black);
        }

        /// <summary>
        /// Swap voxels
        /// </summary>
        public void SwapCoefficientVoxels()
        {
            _historyBuffer.SwapBuffers();
        }

        public Vector3 GetVoxelMinCorner()
        {
            return transform.position;
        }

        /// <summary>
        /// Get probes that need to be updated for performance optimization
        /// </summary>
        /// <returns>Array of probes to update this frame</returns>
        public void GetProbesToUpdate(List<Probe> probes)
        {
            if (Probes == null || Probes.Length == 0)
                return;

            if (!multiFrameRelight || _frameCount < 2)
            {
                probes.AddRange(Probes);
                return;
            }

            // Multi frame relight processing
            // Ensure probesToUpdateCount is a divisor of Probes.Length to guarantee proper cycling
            int probesToUpdateCount = GetValidProbesPerFrameUpdate();

            for (int i = 0; i < probesToUpdateCount; i++)
            {
                int probeIndex = (_currentProbeUpdateIndex + i) % Probes.Length;
                probes.Add(Probes[probeIndex]);
            }
        }

        public void AdvanceRenderFrame()
        {
            ++_frameCount;
            int probesToUpdateCount = GetValidProbesPerFrameUpdate();
            // Advance the update index for next frame
            _currentProbeUpdateIndex = (_currentProbeUpdateIndex + probesToUpdateCount) % Probes.Length;
        }

        /// <summary>
        /// Reset probe update rotation to start from beginning
        /// </summary>
        public void ResetProbeUpdateRotation()
        {
            _currentProbeUpdateIndex = 0;
        }

        /// <summary>
        /// Get the largest divisor of Probes.Length that doesn't exceed probesPerFrameUpdate
        /// This ensures proper cycling of probe updates
        /// </summary>
        /// <returns>Valid number of probes to update per frame</returns>
        private int GetValidProbesPerFrameUpdate()
        {
            if (Probes == null || Probes.Length == 0)
                return 1;

            int maxProbesPerFrame = Mathf.Min(probesPerFrameUpdate, Probes.Length);

            // Find the largest divisor of Probes.Length that doesn't exceed maxProbesPerFrame
            for (int i = maxProbesPerFrame; i >= 1; i--)
            {
                if (Probes.Length % i == 0)
                {
                    return i;
                }
            }

            // Fallback to 1 (always a divisor)
            return 1;
        }
    }
}