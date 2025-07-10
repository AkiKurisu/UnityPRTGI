using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ProbeVolume))]
public class ProbeVolumeEditor : Editor
{
    private ProbeVolume ProbeVolume => (ProbeVolume)target;
    
    public override void OnInspectorGUI() 
    {
        DrawDefaultInspector();

        if (GUILayout.Button("Clear Data"))
        {
            ProbeVolume.ClearData();
        }

        if (GUILayout.Button("Bake Data"))
        {
            ProbeVolume.BakeData();
        }
    }
}
