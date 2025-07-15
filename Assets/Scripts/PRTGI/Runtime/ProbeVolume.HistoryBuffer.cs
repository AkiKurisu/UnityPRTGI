using UnityEngine;
using UnityEngine.Rendering;

namespace PRTGI
{
    public partial class ProbeVolume
    {
        /// <summary>
        /// Utility class for managing buffering of RenderTextures
        /// </summary>
        private class RenderTextureHistoryBuffer
        {
            private RenderTexture[] _buffers;

            private int _writeIndex;

            private int _currentIndex = 1;

            /// <summary>
            /// RenderTexture for write operations
            /// </summary>
            public RenderTexture WriteFrame => _buffers[_writeIndex];

            /// <summary>
            /// RenderTexture for current frame sampling
            /// </summary>
            public RenderTexture CurrentFrame => _buffers[_currentIndex];

            /// <summary>
            /// Check if the buffer is initialized
            /// </summary>
            public bool IsInitialized =>
                _buffers != null && _buffers[0] != null && _buffers[1] != null;

            /// <summary>
            /// Initialize the buffer with given RenderTexture settings
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

                _buffers = new RenderTexture[2];

                for (int i = 0; i < 2; i++)
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
            /// Swap the RenderTextures
            /// Current frame RT and write frame RT swap
            /// </summary>
            public void SwapBuffers()
            {
                if (!IsInitialized)
                {
                    Debug.LogWarning("Buffer not initialized, cannot swap buffers");
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
                return $"Write: {_writeIndex}, Current: {_currentIndex}";
            }
        }
    }
}