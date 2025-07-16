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
    public partial class Probe : MonoBehaviour
    {
        private const int ThreadX = 32;

        private const int ThreadY = 16;

        private const int RayNum = ThreadX * ThreadY; // 512 per probe

        private const int SurfelByteSize = 3 * 12 + 4; // sizeof(Surfel)

        private const float SurfelSize = 0.05f;

        private const float NormalLength = 0.25f;

        private const float SkyRayLength = 25.0f;

        private const float SkyMaskThreshold = 0.995f;

#if UNITY_EDITOR
        // Debug visualization settings
        [Header("Debug Settings")]
        [Range(0.01f, 0.1f)] 
        public float sphereSize = 0.025f;
        
        // Debug colors
        public Color defaultColor = Color.yellow;
        
        public Color skyColor = Color.blue;
        
        public Color normalColor = Color.green;
#endif

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

        // Shader property IDs
        private static readonly int ProbePos = Shader.PropertyToID("_probePos");

        private static readonly int RandSeed = Shader.PropertyToID("_randSeed");

        private static readonly int WorldPosCubemap = Shader.PropertyToID("_worldPosCubemap");

        private static readonly int NormalCubemap = Shader.PropertyToID("_normalCubemap");

        private static readonly int AlbedoCubemap = Shader.PropertyToID("_albedoCubemap");

        private static readonly int Surfels = Shader.PropertyToID("_surfels");

        private static readonly int CoefficientSH9 = Shader.PropertyToID("_coefficientSH9");

        private MeshRenderer _renderer;

        private ProbeVolume _volume;

        private int _relightKernel;

        private void Start()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            ReAllocateIfNeeded();
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (!gameObject.scene.IsValid()) return;
            // Update MeshRenderer visibility based on debug mode
            UpdateMeshRendererVisibility();
        }
#endif
        
        public void ReAllocateIfNeeded()
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
            if(!_renderer) _renderer = GetComponent<MeshRenderer>();
            if(!_volume) _volume = GetComponentInParent<ProbeVolume>();

            _relightKernel = surfelReLightCS.FindKernel("CSMain");
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            surfels?.Release();
            _coefficientSH9?.Release();
            _surfelRadiance?.Release();
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

        /// <summary>
        /// Relight probe
        /// </summary>
        /// <param name="cmd"></param>
        public void ReLight(CommandBuffer cmd)
        {
            ReAllocateIfNeeded();
            if (!_volume) return;
            
            // set necessary data and start sample
            Vector3 p = transform.position;
            cmd.SetComputeVectorParam(surfelReLightCS, "_probePos", new Vector4(p.x, p.y, p.z, 1.0f));
            cmd.SetComputeBufferParam(surfelReLightCS, _relightKernel, "_surfels", surfels);
            cmd.SetComputeIntParam(surfelReLightCS, "_indexInProbeVolume", indexInProbeVolume);
            
            // Debug data
            if (_volume.debugMode == ProbeVolumeDebugMode.ProbeRadiance)
            {
                cmd.SetBufferData(_coefficientSH9, coefficientClearValue);
                cmd.SetComputeBufferParam(surfelReLightCS, _relightKernel, "_coefficientSH9", _coefficientSH9);
                cmd.SetComputeBufferParam(surfelReLightCS, _relightKernel, "_surfelRadiance", _surfelRadiance);
            }

            // SH output to volume storage
            cmd.SetComputeTextureParam(surfelReLightCS, _relightKernel, "_coefficientVoxel3D",
                _volume.WriteCoefficientVoxel3D);
            cmd.SetComputeTextureParam(surfelReLightCS, _relightKernel, "_lastFrameCoefficientVoxel3D",
                _volume.LastFrameCoefficientVoxel3D);
            
            // start CS
            cmd.DispatchCompute(surfelReLightCS, _relightKernel, 1, 1, 1);
        }
    }
}