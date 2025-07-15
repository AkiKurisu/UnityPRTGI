using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UObject = UnityEngine.Object;

namespace PRTGI.Editor
{
    /// <summary>
    /// PRTBaker manages the baking process and resources for PRT (Precomputed Radiance Transfer).
    /// It handles shared resources like render textures and provides a unified interface for baking operations.
    /// </summary>
    public class PRTBaker : IPRTBaker, IDisposable
    {
        // Shared render textures for G-buffer capture
        private RenderTexture _worldPosRT;

        private RenderTexture _normalRT;

        private RenderTexture _albedoRT;

        // Baking settings
        private readonly int _cubemapSize;

        // Progress tracking
        public Action<string, float> OnProgressUpdate;

        private bool _isInitialized;

        private bool _disposed;

        private readonly ProbeVolume _volume;

        // Dictionary to store original shaders for restoration
        private readonly Dictionary<Material, Shader> _originalShaders = new();

        /// <summary>
        /// Initialize the PRTBaker with the specified cubemap size
        /// </summary>
        /// <param name="volume">Bake volume</param>
        /// <param name="cubemapSize">Size of the cubemap textures</param>
        public PRTBaker(ProbeVolume volume, int cubemapSize = 512)
        {
            _volume = volume;
            _cubemapSize = cubemapSize;
            InitializeRenderTextures();
        }

        /// <summary>
        /// Initialize render textures for G-buffer capture
        /// </summary>
        private void InitializeRenderTextures()
        {
            if (_isInitialized) return;

            // Create cubemap render textures
            _worldPosRT = new RenderTexture(_cubemapSize, _cubemapSize, 0, RenderTextureFormat.ARGBFloat)
            {
                dimension = TextureDimension.Cube,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _worldPosRT.Create();

            _normalRT = new RenderTexture(_cubemapSize, _cubemapSize, 0, RenderTextureFormat.ARGBFloat)
            {
                dimension = TextureDimension.Cube,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _normalRT.Create();

            _albedoRT = new RenderTexture(_cubemapSize, _cubemapSize, 0, RenderTextureFormat.ARGB32)
            {
                dimension = TextureDimension.Cube,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _albedoRT.Create();

            _isInitialized = true;
        }

        /// <summary>
        /// Update baking progress
        /// </summary>
        /// <param name="status">Status message</param>
        /// <param name="progress">Progress value</param>
        public void UpdateProgress(string status, float progress)
        {
            OnProgressUpdate?.Invoke(status, progress);
        }

        public (RenderTexture WorldPosRT, RenderTexture NormalRT, RenderTexture AlbedoRT) BakeAtPoint(Vector3 position)
        {
            CaptureGbufferCubemaps(position);
            return (_worldPosRT, _normalRT, _albedoRT);
        }

        /// <summary>
        /// Batch set shader for all game objects in the scene and record original shaders
        /// </summary>
        /// <param name="gameObjects">Array of game objects to modify</param>
        /// <param name="shader">Shader to apply</param>
        /// <param name="recordOriginal">Whether to record original shaders</param>
        private void BatchSetShader(GameObject[] gameObjects, Shader shader, bool recordOriginal = false)
        {
            foreach (var go in gameObjects)
            {
                MeshRenderer meshRenderer = go.GetComponent<MeshRenderer>();
                if (meshRenderer != null && meshRenderer.sharedMaterial != null)
                {
                    var material = meshRenderer.sharedMaterial;

                    // Record original shader if requested
                    if (recordOriginal && !_originalShaders.ContainsKey(material))
                    {
                        _originalShaders[material] = material.shader;
                    }

                    material.shader = shader;
                }
            }
        }

        /// <summary>
        /// Restore original shaders for all materials
        /// </summary>
        private void RestoreOriginalShaders()
        {
            foreach (var kvp in _originalShaders)
            {
                var material = kvp.Key;
                var originalShader = kvp.Value;

                if (material != null && originalShader != null)
                {
                    material.shader = originalShader;
                }
            }

            // Clear the dictionary after restoration
            _originalShaders.Clear();
        }

        /// <summary>
        /// Create a temporary camera for cubemap capture
        /// </summary>
        /// <param name="position">Camera position</param>
        /// <returns>Temporary camera GameObject</returns>
        private Camera CreateCubemapCamera(Vector3 position)
        {
            GameObject cameraGO = new GameObject("PRTBaker_CubemapCamera")
            {
                transform =
                {
                    position = position,
                    rotation = Quaternion.identity
                }
            };

            Camera camera = cameraGO.AddComponent<Camera>();
            camera.cameraType = CameraType.Reflection;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);

            return camera;
        }

        /// <summary>
        /// Capture G-buffer cubemaps at the specified position
        /// </summary>
        /// <param name="position">Position to capture cubemaps from</param>
        private void CaptureGbufferCubemaps(Vector3 position)
        {
            // Create temporary camera
            Camera camera = CreateCubemapCamera(position);

            // Find all objects in the scene
            GameObject[] gameObjects = (GameObject[])UObject.FindObjectsOfType(typeof(GameObject));

            try
            {
                // Capture world position
                BatchSetShader(gameObjects, Shader.Find("CasualPRT/GbufferWorldPos"), true);
                camera.RenderToCubemap(_worldPosRT);

                // Capture normals
                BatchSetShader(gameObjects, Shader.Find("CasualPRT/GbufferNormal"));
                camera.RenderToCubemap(_normalRT);

                // Capture albedo
                BatchSetShader(gameObjects, Shader.Find("Universal Render Pipeline/Unlit"));
                camera.RenderToCubemap(_albedoRT);

                // Restore original shaders
                RestoreOriginalShaders();
            }
            finally
            {
                // Clean up temporary camera
                UObject.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>
        /// Check if the baker is properly initialized
        /// </summary>
        /// <returns>True if initialized</returns>
        public bool IsInitialized()
        {
            return _isInitialized && !_disposed;
        }

        public void Complete()
        {
            _volume.StorageSurfelData(_volume.data);
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _worldPosRT?.Release();
            _normalRT?.Release();
            _albedoRT?.Release();

            _worldPosRT = null;
            _normalRT = null;
            _albedoRT = null;

            // Clear original shaders dictionary
            _originalShaders.Clear();

            _disposed = true;
            _isInitialized = false;

            OnProgressUpdate = null;
        }
    }
}