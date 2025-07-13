using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;

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

    [Range(0.0f, 50.0f)]
    public float skyLightIntensity = 7.0f;

    [Range(0.0f, 50.0f)]
    public float indirectIntensity = 12.0f;

    public ProbeVolumeDebugMode debugMode;

    private const RenderTextureFormat Texture3DFormat = RenderTextureFormat.RInt;

    public Probe[] Probes { get; private set; }

    private bool _isDataInitialized;

    private void Start()
    {
        GenerateProbes();
        TryLoadSurfelData(data);
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (Application.isPlaying) return;
        if (!IsGPUValid())
        {
            GenerateProbes();
            TryLoadSurfelData(data);
        }
    }
#endif

    private void OnDestroy()
    {
        if (CoefficientVoxel3D != null)
        {
            CoefficientVoxel3D.Release();
            CoefficientVoxel3D = null;
        }

        if (LastFrameCoefficientVoxel3D != null)
        {
            LastFrameCoefficientVoxel3D.Release();
            LastFrameCoefficientVoxel3D = null;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawCube(GetVoxelMinCorner(), new Vector3(1, 1, 1));

        if (Probes != null)
        {
            foreach (var probe in Probes)
            {
                if (debugMode == ProbeVolumeDebugMode.ProbeGrid)
                {
                    Vector3 cubeSize = new Vector3(probeGridSize / 2, probeGridSize / 2, probeGridSize / 2);
                    Gizmos.DrawWireCube(probe.transform.position + cubeSize, cubeSize * 2.0f);
                }

                MeshRenderer meshRenderer = probe.GetComponent<MeshRenderer>();
                if (Application.isPlaying) meshRenderer.enabled = false;
                if (debugMode == ProbeVolumeDebugMode.None) meshRenderer.enabled = false;
            }
        }
    }

    public bool IsGPUValid()
    {
        return CoefficientVoxel3D != null && LastFrameCoefficientVoxel3D != null;
    }

    public bool IsActivate()
    {
        return CoefficientVoxel3D != null && LastFrameCoefficientVoxel3D != null && _isDataInitialized;
    }

    /// <summary>
    /// load surfel data from storage
    /// </summary>
    /// <param name="surfelData"></param>
    public void TryLoadSurfelData(ProbeVolumeData surfelData)
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
            Debug.LogWarning("Volume data is out of date, please regenerate prt data.");
            return;
        }

        int j = 0;
        foreach (var probe in Probes)
        {
            for (int i = 0; i < probe.readBackBuffer.Length; i++)
            {
                probe.readBackBuffer[i].position.x = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].position.y = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].position.z = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].normal.x = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].normal.y = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].normal.z = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].albedo.x = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].albedo.y = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].albedo.z = surfelStorageBuffer[j++];
                probe.readBackBuffer[i].skyMask = surfelStorageBuffer[j++];
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
                if (probe != null)
                    DestroyImmediate(probe.gameObject);
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
        int depth = probeSizeY * 9 * 3; // 9 SH coefficients per probe, 3 components (RGB) per coefficient

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
    /// Precompute surfel and bake into <see cref="ProbeVolumeData"/>
    /// </summary>
    public void BakeData()
    {
        if (!Probes.Any())
        {
            GenerateProbes();
        }

        // Hide debug spheres
        foreach (var probe in Probes)
        {
            probe.GetComponent<MeshRenderer>().enabled = false;
        }

        // Capture surfel using brute force cubemap method.
        foreach (var probe in Probes)
        {
            probe.CaptureGbufferCubemaps();
        }

        data.StorageSurfelData(this);
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
