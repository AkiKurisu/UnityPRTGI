using UnityEngine;
using System;

namespace PRTGI
{
    [Serializable]
    [CreateAssetMenu(fileName = "ProbeVolumeData", menuName = "ProbeVolumeData")]
    public class ProbeVolumeData : ScriptableObject
    {
        public Vector3 volumePosition;

        public float[] surfelStorageBuffer;

        public void Clear()
        {
            surfelStorageBuffer = Array.Empty<float>();
            volumePosition = Vector3.zero;
        }
    }
}