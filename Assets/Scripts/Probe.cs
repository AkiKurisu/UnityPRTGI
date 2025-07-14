using UnityEngine;
using UnityEngine.Rendering;

namespace PRTGI
{
    public struct Surfel
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector3 albedo;
        public float skyMask;
    }

    public enum ProbeDebugMode
    {
        IrradianceSphere = 0,
        SphereDistribution = 1,
        SampleDirection = 2,
        Surfel = 3,
        SurfelRadiance = 4
    }

    [ExecuteAlways]
    public class Probe : MonoBehaviour
    {
        private const int ThreadX = 32;
        
        private const int ThreadY = 16;
        
        private const int RayNum = ThreadX * ThreadY; // 512 per probe
        
        private const int SurfelByteSize = 3 * 12 + 4; // sizeof(Surfel)

        // Debug visualization settings
        [Header("Debug Settings")]
        [Range(0.01f, 0.1f)] 
        public float sphereSize = 0.025f;
        
        private const float SurfelSize = 0.05f;
        
        private const float NormalLength = 0.25f;
        
        private const float SkyRayLength = 25.0f;

        private const float SkyMaskThreshold = 0.995f;

        // Debug colors
        public Color defaultColor = Color.yellow;
        
        public Color skyColor = Color.blue;
        
        public Color normalColor = Color.green;

        private MaterialPropertyBlock _matPropBlock;
        
        public Surfel[] readBackBuffer; // CPU side surfel array, for debug
        
        public ComputeBuffer surfels; // GPU side surfel array
        
        private Vector3[] _radianceDebugBuffer;
        
        private ComputeBuffer _surfelRadiance;
        
        private int[] coefficientClearValue;
        
        private ComputeBuffer _coefficientSH9; // GPU side SH9 coefficient, size: 9x3=27

        public ComputeShader surfelSampleCS;
        
        public ComputeShader surfelReLightCS;

        internal int indexInProbeVolume = -1; // set by parent

        private ComputeBuffer _tempBuffer;

        // Shader property IDs
        private static readonly int ProbePos = Shader.PropertyToID("_probePos");
        
        private static readonly int RandSeed = Shader.PropertyToID("_randSeed");
        
        private static readonly int WorldPosCubemap = Shader.PropertyToID("_worldPosCubemap");
        
        private static readonly int NormalCubemap = Shader.PropertyToID("_normalCubemap");
        
        private static readonly int AlbedoCubemap = Shader.PropertyToID("_albedoCubemap");
        
        private static readonly int Surfels = Shader.PropertyToID("_surfels");
        
        private static readonly int CoefficientSH9 = Shader.PropertyToID("_coefficientSH9");

        public MeshRenderer Renderer { get; private set; }
        
        public ProbeVolume Volume { get; private set; }

        private void Start()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            TryInit();
        }

#if UNITY_EDITOR
        private void Update()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            // Update MeshRenderer visibility based on debug mode
            UpdateMeshRendererVisibility();
        }
#endif

        // for debug
        public void TryInit()
        {
            surfels ??= new ComputeBuffer(RayNum, SurfelByteSize);

            if (_coefficientSH9 == null)
            {
                _coefficientSH9 = new ComputeBuffer(27, sizeof(int));
                coefficientClearValue = new int[27];
                for (int i = 0; i < 27; i++)
                {
                    coefficientClearValue[i] = 0;
                }
            }

            readBackBuffer ??= new Surfel[RayNum];
            _surfelRadiance ??= new ComputeBuffer(RayNum, sizeof(float) * 3);
            _radianceDebugBuffer ??= new Vector3[RayNum];
            _matPropBlock ??= new MaterialPropertyBlock();
            _tempBuffer ??= new ComputeBuffer(1, 4);
            Renderer = GetComponent<MeshRenderer>();
            Volume = GetComponentInParent<ProbeVolume>();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            surfels?.Release();
            _coefficientSH9?.Release();
            _surfelRadiance?.Release();
            _tempBuffer?.Release();
        }

        /// <summary>
        /// Update MeshRenderer visibility based on debug mode
        /// </summary>
        private void UpdateMeshRendererVisibility()
        {
            if (!Renderer)
                return;

            // Show irradiance sphere only when IrradianceSphere debug mode is enabled
            bool shouldShowIrradianceSphere = Volume.debugMode == ProbeVolumeDebugMode.ProbeRadiance;
            bool isSelected = Volume.selectedProbeIndex == indexInProbeVolume && indexInProbeVolume != -1;
            // Hide when is selected and using other debug modes
            shouldShowIrradianceSphere &= !isSelected || Volume.selectedProbeDebugMode == ProbeDebugMode.IrradianceSphere;
            Renderer.enabled = shouldShowIrradianceSphere;

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
            if (!Renderer || !Renderer.sharedMaterial)
                return;

            Renderer.sharedMaterial.shader = Shader.Find("CasualPRT/SHDebug");

            if (_coefficientSH9 != null)
            {
                _matPropBlock.SetBuffer(CoefficientSH9, _coefficientSH9);
                Renderer.SetPropertyBlock(_matPropBlock);
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

        /// <summary>
        /// Bake surfels data using PRTBaker
        /// </summary>
        /// <param name="prtBaker">PRTBaker instance to use for capture</param>
        public void BakeData(IPRTBaker prtBaker)
        {
            if (prtBaker == null || !prtBaker.IsInitialized())
            {
                Debug.LogError("PRTBaker is null or not initialized");
                return;
            }

            TryInit();

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
            surfelSampleCS.SetVector(ProbePos, new Vector4(p.x, p.y, p.z, 1.0f));
            surfelSampleCS.SetFloat(RandSeed, Random.Range(0.0f, 1.0f));
            surfelSampleCS.SetTexture(kid, WorldPosCubemap, worldPosCubemap);
            surfelSampleCS.SetTexture(kid, NormalCubemap, normalCubemap);
            surfelSampleCS.SetTexture(kid, AlbedoCubemap, albedoCubemap);
            surfelSampleCS.SetBuffer(kid, Surfels, surfels);

            // start CS
            surfelSampleCS.Dispatch(kid, 1, 1, 1);

            // readback
            surfels.GetData(readBackBuffer);
        }

        // relight compute pass in runtime 
        public void ReLight(CommandBuffer cmd)
        {
            var kernel = surfelReLightCS.FindKernel("CSMain");

            // set necessary data and start sample
            Vector3 p = transform.position;
            cmd.SetComputeVectorParam(surfelReLightCS, "_probePos", new Vector4(p.x, p.y, p.z, 1.0f));
            cmd.SetComputeBufferParam(surfelReLightCS, kernel, "_surfels", surfels);
            cmd.SetComputeBufferParam(surfelReLightCS, kernel, "_coefficientSH9", _coefficientSH9);
            cmd.SetComputeBufferParam(surfelReLightCS, kernel, "_surfelRadiance", _surfelRadiance);

            // if probe has parent volume, "indexInProbeVolume" will >= 0
            // then SH will output to volume storage
            ProbeVolume probeVolume = transform.parent == null ? null : transform.parent.GetComponent<ProbeVolume>();

            if (probeVolume != null)
            {
                cmd.SetComputeTextureParam(surfelReLightCS, kernel, "_coefficientVoxel3D", probeVolume.CoefficientVoxel3D);
                cmd.SetComputeTextureParam(surfelReLightCS, kernel, "_lastFrameCoefficientVoxel3D",
                    probeVolume.LastFrameCoefficientVoxel3D);
            }
            else
            {
                // No parent volume, use temp buffer
                cmd.SetComputeBufferParam(surfelReLightCS, kernel, "_coefficientVoxel", _tempBuffer);
            }

            cmd.SetComputeIntParam(surfelReLightCS, "_indexInProbeVolume", indexInProbeVolume);

            // start CS
            cmd.SetBufferData(_coefficientSH9, coefficientClearValue);
            cmd.DispatchCompute(surfelReLightCS, kernel, 1, 1, 1);
        }
    }
}