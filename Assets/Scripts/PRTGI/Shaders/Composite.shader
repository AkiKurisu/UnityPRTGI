Shader "CasualPRT/Composite"
{
    Properties
    {
       
    }
    SubShader
    {
        // No culling or depth
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "SH.hlsl"

            TEXTURE2D_X_HALF(_GBuffer0);
            TEXTURE2D_X_HALF(_GBuffer1);
            TEXTURE2D_X_HALF(_GBuffer2);
            float4x4 _ScreenToWorld[2];

            float _coefficientVoxelGridSize;
            float4 _coefficientVoxelCorner;
            float4 _coefficientVoxelSize;
            
            // Legacy ComputeBuffer support
            StructuredBuffer<int> _coefficientVoxel;

            Texture3D<float3> _coefficientVoxel3D;
            
            SamplerState sampler_coefficientVoxel3D;

            float _indirectIntensity;

            float4 frag (Varyings i) : SV_Target
            {
                float4 color = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.texcoord);

                // decode from gbuffer
                float sceneRawDepth = SampleSceneDepth(i.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(i.texcoord, sceneRawDepth, UNITY_MATRIX_I_VP);
                float3 albedo = SAMPLE_TEXTURE2D(_GBuffer0, sampler_LinearClamp, i.texcoord).xyz;
                float3 normal = SAMPLE_TEXTURE2D(_GBuffer2, sampler_LinearClamp, i.texcoord).xyz;

                float3 gi = SampleSHVoxel3D(
                        worldPos, 
                        albedo, 
                        normal,
                        _coefficientVoxel3D,
                        _coefficientVoxelGridSize,
                        _coefficientVoxelCorner,
                        _coefficientVoxelSize
                    );
                
                color.rgb += gi * _indirectIntensity;
                
                return color;
            }
            ENDHLSL
        }
    }
}
