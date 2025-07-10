using UnityEngine;
using UnityEditor;
using System;

[Serializable]
[CreateAssetMenu(fileName = "ProbeVolumeData", menuName = "ProbeVolumeData")]
public class ProbeVolumeData : ScriptableObject
{
    [SerializeField] public Vector3 volumePosition;

    [SerializeField] public float[] surfelStorageBuffer;

    // pack all probe's data to 1D array
    public void StorageSurfelData(ProbeVolume volume)
    {
        int probeNum = volume.probeSizeX * volume.probeSizeY * volume.probeSizeZ;
        int surfelPerProbe = 512;
        int floatPerSurfel = 10;
        Array.Resize(ref surfelStorageBuffer, probeNum * surfelPerProbe * floatPerSurfel);
        int j = 0;
        for (int i = 0; i < volume.Probes.Length; i++)
        {
            Probe probe = volume.Probes[i].GetComponent<Probe>();
            foreach (var surfel in probe.readBackBuffer)
            {
                surfelStorageBuffer[j++] = surfel.position.x;
                surfelStorageBuffer[j++] = surfel.position.y;
                surfelStorageBuffer[j++] = surfel.position.z;
                surfelStorageBuffer[j++] = surfel.normal.x;
                surfelStorageBuffer[j++] = surfel.normal.y;
                surfelStorageBuffer[j++] = surfel.normal.z;
                surfelStorageBuffer[j++] = surfel.albedo.x;
                surfelStorageBuffer[j++] = surfel.albedo.y;
                surfelStorageBuffer[j++] = surfel.albedo.z;
                surfelStorageBuffer[j++] = surfel.skyMask;
            }
        }

        volumePosition = volume.gameObject.transform.position;
        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssets();
    }

    public void Clear()
    {
        surfelStorageBuffer = Array.Empty<float>();
        volumePosition = Vector3.zero;
    }
}