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

        private MaterialPropertyBlock _matPropBlock;

        public Surfel[] ReadBackBuffer { get; set; } // CPU side surfel array, for debug

        private ComputeBuffer _surfels; // GPU side surfel array

        private Vector3[] _radianceDebugBuffer;

        private ComputeBuffer _surfelRadiance;

        private int[] coefficientClearValue;

        private ComputeBuffer _coefficientSH9; // GPU side SH9 coefficient, size: 9x3=27

        [SerializeField]
        private ComputeShader surfelSampleCS;

        [SerializeField]
        private ComputeShader surfelReLightCS;

        internal int indexInProbeVolume = -1; // set by parent

        /// <summary>
        /// Debug renderer
        /// </summary>
        internal MeshRenderer Renderer { get; private set; }

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
            _surfels ??= new ComputeBuffer(RayNum, SurfelByteSize);

            if (_coefficientSH9 == null)
            {
                _coefficientSH9 = new ComputeBuffer(27, sizeof(int));
                coefficientClearValue = new int[27];
                for (int i = 0; i < 27; i++)
                {
                    coefficientClearValue[i] = 0;
                }
            }

            ReadBackBuffer ??= new Surfel[RayNum];
            _surfelRadiance ??= new ComputeBuffer(RayNum, sizeof(float) * 3);
            _radianceDebugBuffer ??= new Vector3[RayNum];
            _matPropBlock ??= new MaterialPropertyBlock();
            if(!Renderer) Renderer = GetComponent<MeshRenderer>();
            if(!_volume) _volume = GetComponentInParent<ProbeVolume>();

            _relightKernel = surfelReLightCS.FindKernel("CSMain");
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            if (!gameObject.scene.IsValid()) return;
#endif
            _surfels?.Release();
            _coefficientSH9?.Release();
            _surfelRadiance?.Release();
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
            cmd.SetComputeVectorParam(surfelReLightCS, ShaderProperties.ProbePos, new Vector4(p.x, p.y, p.z, 1.0f));
            cmd.SetComputeBufferParam(surfelReLightCS, _relightKernel, ShaderProperties.Surfels, _surfels);
            cmd.SetComputeIntParam(surfelReLightCS, ShaderProperties.IndexInProbeVolume, indexInProbeVolume);
            
            // Debug data
            if (_volume.debugMode == ProbeVolumeDebugMode.ProbeRadiance)
            {
                cmd.SetBufferData(_coefficientSH9, coefficientClearValue);
                cmd.SetComputeBufferParam(surfelReLightCS, _relightKernel, ShaderProperties.CoefficientSH9, _coefficientSH9);
                cmd.SetComputeBufferParam(surfelReLightCS, _relightKernel, ShaderProperties.SurfelRadiance, _surfelRadiance);
            }

            // SH output to volume storage
            cmd.SetComputeTextureParam(surfelReLightCS, _relightKernel, PRTShaderProperties.CoefficientVoxel3D,
                _volume.WriteCoefficientVoxel3D);
            cmd.SetComputeTextureParam(surfelReLightCS, _relightKernel, PRTShaderProperties.LastFrameCoefficientVoxel3D,
                _volume.LastFrameCoefficientVoxel3D);
            
            // start CS
            cmd.DispatchCompute(surfelReLightCS, _relightKernel, 1, 1, 1);
        }

        internal void SetData(Surfel[] probeReadBackBuffer)
        {
            _surfels.SetData(probeReadBackBuffer);
        }
        
        // Shader property IDs
        private static class ShaderProperties
        {
            public static readonly int ProbePos = Shader.PropertyToID("_probePos");

            public static readonly int RandSeed = Shader.PropertyToID("_randSeed");

            public static readonly int WorldPosCubemap = Shader.PropertyToID("_worldPosCubemap");

            public static readonly int NormalCubemap = Shader.PropertyToID("_normalCubemap");

            public static readonly int AlbedoCubemap = Shader.PropertyToID("_albedoCubemap");

            public static readonly int Surfels = Shader.PropertyToID("_surfels");

            public static readonly int CoefficientSH9 = Shader.PropertyToID("_coefficientSH9");
        
            public static readonly int SurfelRadiance = Shader.PropertyToID("_surfelRadiance");
        
            public static readonly int IndexInProbeVolume = Shader.PropertyToID("_indexInProbeVolume");
        }
    }
}