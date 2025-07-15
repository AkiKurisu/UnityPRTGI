using UnityEngine;
using UnityEngine.Rendering;

namespace PRTGI
{
    public partial class ProbeVolume
    {
        /// <summary>
        /// Utility class for managing triple buffering of RenderTextures
        /// Manages three RenderTextures for history frame sampling, write operations, and current frame sampling
        /// </summary>
        private class RenderTextureTripleBuffer
        {
            private RenderTexture[] _buffers;

            private int _historyIndex;

            private int _writeIndex = 1;

            private int _currentIndex = 2;

            /// <summary>
            /// RenderTexture for history frame sampling
            /// </summary>
            public RenderTexture HistoryFrame => _buffers[_historyIndex];

            /// <summary>
            /// RenderTexture for write operations
            /// </summary>
            public RenderTexture WriteFrame => _buffers[_writeIndex];

            /// <summary>
            /// RenderTexture for current frame sampling
            /// </summary>
            public RenderTexture CurrentFrame => _buffers[_currentIndex];

            /// <summary>
            /// Check if the triple buffer is initialized
            /// </summary>
            public bool IsInitialized =>
                _buffers != null && _buffers[0] != null && _buffers[1] != null && _buffers[2] != null;

            /// <summary>
            /// Initialize the triple buffer with given RenderTexture settings
            /// </summary>
            /// <param name="width">Width of the RenderTexture</param>
            /// <param name="height">Height of the RenderTexture</param>
            /// <param name="depth">Depth buffer bits</param>
            /// <param name="format">Format of the RenderTexture</param>
            /// <param name="dimension">Texture dimension</param>
            /// <param name="volumeDepth">Volume depth for 3D textures</param>
            public void Initialize(int width, int height, int depth, RenderTextureFormat format,
                TextureDimension dimension = TextureDimension.Tex2D, int volumeDepth = 0)
            {
                Release();

                _buffers = new RenderTexture[3];

                for (int i = 0; i < 3; i++)
                {
                    _buffers[i] = new RenderTexture(width, height, depth, format)
                    {
                        dimension = dimension,
                        enableRandomWrite = true,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                        name = $"RenderTextureBuffer_{i}"
                    };

                    // Set volume depth for 3D textures
                    if (dimension == TextureDimension.Tex3D)
                    {
                        _buffers[i].volumeDepth = volumeDepth;
                    }

                    _buffers[i].Create();
                }
            }

            /// <summary>
            /// Swap the RenderTextures according to the triple buffer pattern
            /// First: Current frame RT and history frame RT swap
            /// Second: History frame RT and write frame RT swap
            /// </summary>
            public void SwapTripleBuffers()
            {
                if (!IsInitialized)
                {
                    Debug.LogWarning("Triple buffer not initialized, cannot swap buffers");
                    return;
                }

                // Perform the two swaps as described:
                // 1. Current frame RT and history frame RT swap
                // 2. History frame RT and write frame RT swap

                // This effectively rotates the indices: current -> history -> write -> current
                int tempIndex = _currentIndex;
                _currentIndex = _historyIndex;
                _historyIndex = _writeIndex;
                _writeIndex = tempIndex;
            }

            /// <summary>
            /// Swap only write and current buffers, ignore history frame RT
            /// </summary>
            public void SwapDoubleBuffers()
            {
                if (!IsInitialized)
                {
                    Debug.LogWarning("Triple buffer not initialized, cannot swap buffers");
                    return;
                }

                (_currentIndex, _writeIndex) = (_writeIndex, _currentIndex);
            }

            /// <summary>
            /// Clear only the write buffer
            /// </summary>
            /// <param name="cmd">Command buffer to use for clearing</param>
            /// <param name="clearColor">Color to clear to</param>
            public void ClearWriteBuffer(CommandBuffer cmd, Color clearColor)
            {
                if (!IsInitialized)
                {
                    Debug.LogWarning("Triple buffer not initialized, cannot clear write buffer");
                    return;
                }

                cmd.SetRenderTarget(WriteFrame, 0, CubemapFace.Unknown, -1);
                cmd.ClearRenderTarget(false, true, clearColor);
            }

            /// <summary>
            /// Release all RenderTextures
            /// </summary>
            public void Release()
            {
                if (_buffers != null)
                {
                    for (int i = 0; i < _buffers.Length; i++)
                    {
                        if (_buffers[i] != null)
                        {
                            _buffers[i].Release();
                            _buffers[i] = null;
                        }
                    }

                    _buffers = null;
                }
            }

            /// <summary>
            /// Get buffer index for debugging purposes
            /// </summary>
            /// <returns>String representation of current buffer indices</returns>
            public string GetBufferInfo()
            {
                return $"History: {_historyIndex}, Write: {_writeIndex}, Current: {_currentIndex}";
            }
        }
    }
}