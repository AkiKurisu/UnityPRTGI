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
    /// Array for each probe's SH coefficient
    /// </summary>
    public ComputeBuffer CoefficientVoxel { get; private set; }
    
    /// <summary>
    /// Last frame voxel for inf bounce
    /// </summary>
    public ComputeBuffer LastFrameCoefficientVoxel { get; private set; }
    
    private int[] _cofficientVoxelClearValue;

    [Range(0.0f, 50.0f)]
    public float skyLightIntensity = 7.0f;

    [Range(0.0f, 50.0f)]
    public float indirectIntensity = 12.0f;

    public ProbeVolumeDebugMode debugMode;

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
        CoefficientVoxel?.Release();
        LastFrameCoefficientVoxel?.Release();
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.DrawCube(GetVoxelMinCorner(), new Vector3(1,1,1));

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
        if (!Probes.Any()) return false;
        // Is GPU data prepared.
        if (!(CoefficientVoxel?.IsValid() ?? false) || !(LastFrameCoefficientVoxel?.IsValid() ?? false)) return false;
        
        // Ignore when component is disabled.
        return enabled;
    }

    public bool IsActivate()
    {
        if (!_isDataInitialized) return false;
        return IsGPUValid();
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
        for (int i = transform.childCount -1; i >=0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        Probes = null;
    }

    /// <summary>
    /// Create probes based on volume current location.
    /// </summary>
    private void GenerateProbes()
    {
        ReleaseProbes();

        CoefficientVoxel?.Release();
        LastFrameCoefficientVoxel?.Release();

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

        // generate 1D "Voxel" buffer to storage SH coefficients
        CoefficientVoxel = new ComputeBuffer(probeNum * 27, sizeof(int));
        LastFrameCoefficientVoxel = new ComputeBuffer(probeNum * 27, sizeof(int));
        _cofficientVoxelClearValue = new int[probeNum * 27];
        for (int i = 0; i < _cofficientVoxelClearValue.Length; i++)
        {
            _cofficientVoxelClearValue[i] = 0;
        }  
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
        if (CoefficientVoxel == null || _cofficientVoxelClearValue == null) return;
        cmd.SetBufferData(CoefficientVoxel, _cofficientVoxelClearValue);
    }

    /// <summary>
    /// Swap last frame voxel with current voxel
    /// </summary>
    public void SwapLastFrameCoefficientVoxel()
    {
        if (CoefficientVoxel == null || LastFrameCoefficientVoxel == null) return;
        (CoefficientVoxel, LastFrameCoefficientVoxel) = (LastFrameCoefficientVoxel, CoefficientVoxel);
    }

    
    public Vector3 GetVoxelMinCorner()
    {
        return transform.position;
    }
}
