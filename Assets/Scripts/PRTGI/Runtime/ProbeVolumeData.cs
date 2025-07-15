using UnityEngine;
using System;

namespace PRTGI
{
    [Serializable]
    [CreateAssetMenu(fileName = "ProbeVolumeData", menuName = "ProbeVolumeData")]
    public class ProbeVolumeData : ScriptableObject
    {
        [HideInInspector]
        public Vector3 volumePosition;

        [HideInInspector]
        public float[] surfelStorageBuffer;

        public void Clear()
        {
            surfelStorageBuffer = Array.Empty<float>();
            volumePosition = Vector3.zero;
        }
    }
}