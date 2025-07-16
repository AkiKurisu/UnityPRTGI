using UnityEngine;
using UnityEditor;
using UEditor = UnityEditor.Editor;

namespace PRTGI.Editor
{
    [CustomEditor(typeof(ProbeVolume))]
    public class ProbeVolumeEditor : UEditor
    {
        private ProbeVolume _probeVolume;

        private bool _showProbeSelection;

        private bool _showBakeSettings;

        private void OnEnable()
        {
            _probeVolume = (ProbeVolume)target;
            _showProbeSelection = EditorPrefs.GetBool("PRTGI_ProbeVolumeEditor_ShowProbeSettings", false);
            _showBakeSettings = EditorPrefs.GetBool("PRTGI_ProbeVolumeEditor_ShowBakeSettings", false);
        }

        private void OnDisable()
        {
            EditorPrefs.SetBool("PRTGI_ProbeVolumeEditor_ShowProbeSettings", _showProbeSelection);
            EditorPrefs.SetBool("PRTGI_ProbeVolumeEditor_ShowBakeSettings", _showBakeSettings);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Basic ProbeVolume settings
            DrawDefaultInspector();

            EditorGUILayout.Space();

            // Probe Selection & Debug section
            DrawDebugSettingsSection();

            // Bake settings section
            DrawBakeSettingsSection();

            // Action buttons
            DrawActionButtons();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDebugSettingsSection()
        {
            EditorGUILayout.Space();
            _showProbeSelection = EditorGUILayout.Foldout(_showProbeSelection, "Debug Settings", true);

            if (_showProbeSelection)
            {
                EditorGUI.indentLevel++;

                // Probe selection
                if (_probeVolume.Probes != null && _probeVolume.Probes.Length > 0)
                {
                    // Volume debug mode
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"),
                        new GUIContent("Volume Debug Mode", "Debug mode of Probe Volume."));

                    // Debug mode for selected probe
                    var newDebugMode = (ProbeDebugMode)EditorGUILayout.EnumPopup("Probe Debug Mode", _probeVolume.selectedProbeDebugMode);
                    _probeVolume.selectedProbeDebugMode = newDebugMode;
                }
                else
                {
                    EditorGUILayout.HelpBox("No probes found. Click 'Generate Probes' to create probe grid.", MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawBakeSettingsSection()
        {
            EditorGUILayout.Space();
            _showBakeSettings = EditorGUILayout.Foldout(_showBakeSettings, "Bake Settings", true);

            if (_showBakeSettings)
            {
                EditorGUI.indentLevel++;

                // Bake resolution
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bakeResolution"),
                    new GUIContent("Bake Resolution", "Resolution for cubemap baking"));

                // Bake info
                if (_probeVolume.Probes != null)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Bake Info", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Total Probes: {_probeVolume.Probes.Length}");
                    EditorGUILayout.LabelField($"Grid Size: {_probeVolume.probeSizeX} x {_probeVolume.probeSizeY} x {_probeVolume.probeSizeZ}");
                    EditorGUILayout.LabelField($"Probe Spacing: {_probeVolume.probeGridSize}");
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Clear Data"))
            {
                ClearData();
            }

            if (GUILayout.Button("Bake Data"))
            {
                BakeData();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ClearData()
        {
            _probeVolume.ClearData();
            EditorUtility.SetDirty(_probeVolume.data);
            EditorUtility.ClearProgressBar();
        }

        private void BakeData()
        {
            using var prtBaker = new PRTBaker(_probeVolume, (int)_probeVolume.bakeResolution);

            // Setup progress callbacks
            prtBaker.OnProgressUpdate = (status, progress) =>
            {
                EditorUtility.DisplayProgressBar("PRTBaker", status, progress);
            };

            try
            {
                prtBaker.BakeVolume(_probeVolume);
                EditorUtility.SetDirty(_probeVolume.data);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void OnSceneGUI()
        {
            if (_probeVolume.Probes == null || _probeVolume.debugMode != ProbeVolumeDebugMode.ProbeRadiance)
                return;

            for (int i = 0; i < _probeVolume.Probes.Length; i++)
            {
                if (!_probeVolume.Probes[i])
                    continue;

                Vector3 probePos = _probeVolume.Probes[i].transform.position;

                // Draw selectable handles
                if (Handles.Button(probePos, Quaternion.identity, _probeVolume.probeHandleSize * 0.2f, _probeVolume.probeHandleSize * 0.2f, Handles.CubeHandleCap))
                {
                    _probeVolume.selectedProbeIndex = i;
                    Repaint();
                }

                // Draw probe index labels
                Handles.Label(probePos + Vector3.up * 0.5f, i.ToString());
            }
        }
    }
}