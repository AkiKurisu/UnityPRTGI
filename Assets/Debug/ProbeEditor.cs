using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Probe))]
public class ProbeEditor : Editor
{
    private Probe Probe => (Probe)target;
    
    public override void OnInspectorGUI() 
    {
        DrawDefaultInspector();

        if (GUILayout.Button("Probe Capture"))
        {
            Probe.CaptureGbufferCubemaps();
        }
    }
}
