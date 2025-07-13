#ifndef PRT_SH_INCLUDED
#define PRT_SH_INCLUDED

// ref: https://www.shadertoy.com/view/lsfXWH
// Y_l_m(s), where l is the band and m the range in [-l..l] 
// return SH basis value of Y_l_m in direction s 
float SH(in int l, in int m, in float3 s) 
{ 
    #define k01 0.2820947918    // sqrt(  1/PI)/2
    #define k02 0.4886025119    // sqrt(  3/PI)/2
    #define k03 1.0925484306    // sqrt( 15/PI)/2
    #define k04 0.3153915652    // sqrt(  5/PI)/4
    #define k05 0.5462742153    // sqrt( 15/PI)/4

	//float3 n = s.zxy;
    float x = s.x;
    float y = s.z;
    float z = s.y;
	
    //----------------------------------------------------------
    if( l==0 )          return  k01;
    //----------------------------------------------------------
	if( l==1 && m==-1 ) return  k02*y;
    if( l==1 && m== 0 ) return  k02*z;
    if( l==1 && m== 1 ) return  k02*x;
    //----------------------------------------------------------
	if( l==2 && m==-2 ) return  k03*x*y;
    if( l==2 && m==-1 ) return  k03*y*z;
    if( l==2 && m== 0 ) return  k04*(2.0*z*z-x*x-y*y);
    if( l==2 && m== 1 ) return  k03*x*z;
    if( l==2 && m== 2 ) return  k05*(x*x-y*y);

	return 0.0;
}
 
// decode irradiance
float3 IrradianceSH9(in float3 c[9], in float3 dir)
{
    #define A0 3.1415
    #define A1 2.0943
    #define A2 0.7853

    float3 irradiance = float3(0, 0, 0);
    irradiance += SH(0,  0, dir) * c[0] * A0;
    irradiance += SH(1, -1, dir) * c[1] * A1;
    irradiance += SH(1,  0, dir) * c[2] * A1;
    irradiance += SH(1,  1, dir) * c[3] * A1;
    irradiance += SH(2, -2, dir) * c[4] * A2;
    irradiance += SH(2, -1, dir) * c[5] * A2;
    irradiance += SH(2,  0, dir) * c[6] * A2;
    irradiance += SH(2,  1, dir) * c[7] * A2;
    irradiance += SH(2,  2, dir) * c[8] * A2;
    irradiance = max(float3(0, 0, 0), irradiance);

    return irradiance;
}

// 使用定点数存储小数, 保留小数点后 5 位
// 因为 compute shader 的 InterlockedAdd 不支持 float
#define FIXED_SCALE 100000.0

int EncodeFloatToInt(float x)
{
    return int(x * FIXED_SCALE);
}
float DecodeFloatFromInt(int x)
{
    return float(x) / FIXED_SCALE;
}

// Convert world position to 3D texture coordinates
float3 WorldPosToTexture3DCoord(float3 worldPos, float4 _coefficientVoxelSize, float _coefficientVoxelGridSize, float4 _coefficientVoxelCorner)
{
    // Convert world position to probe grid coordinates
    float3 probeIndexF = (worldPos.xyz - _coefficientVoxelCorner.xyz) / _coefficientVoxelGridSize;
    
    // Convert to normalized texture coordinates [0, 1]
    float3 texCoord;
    texCoord.x = probeIndexF.x / _coefficientVoxelSize.x;  // X coordinate
    texCoord.y = probeIndexF.z / _coefficientVoxelSize.z;  // Z coordinate (mapped to Y in texture)
    texCoord.z = probeIndexF.y / (_coefficientVoxelSize.y * 9.0 * 3.0); // Y coordinate scaled by 9 SH coefficients * 3 components
    
    return texCoord;
}

// Convert 3D probe index to 3D texture coordinates for a specific SH coefficient
float3 ProbeIndex3DToTexture3DCoord(int3 probeIndex3, int shIndex, float4 _coefficientVoxelSize)
{
    float3 texCoord;
    texCoord.x = (probeIndex3.x + 0.5) / _coefficientVoxelSize.x;
    texCoord.y = (probeIndex3.z + 0.5) / _coefficientVoxelSize.z;
    texCoord.z = (probeIndex3.y * 9.0 * 3.0 + shIndex * 3.0 + 0.5) / (_coefficientVoxelSize.y * 9.0 * 3.0);
    
    return texCoord;
}

// Helper function to decode SH coefficients from 3D texture for a specific probe
void DecodeSHCoefficientFromVoxel3D(inout float3 c[9], in Texture3D<int> coefficientVoxel3D, int3 probeIndex3)
{
    // Sample RGB components separately for each SH coefficient
    for (int i = 0; i < 9; i++)
    {
        // Calculate 3D texture coordinates for each RGB component
        int3 texCoordR = int3(probeIndex3.x, probeIndex3.z, (probeIndex3.y * 9 + i) * 3 + 0);
        int3 texCoordG = int3(probeIndex3.x, probeIndex3.z, (probeIndex3.y * 9 + i) * 3 + 1);
        int3 texCoordB = int3(probeIndex3.x, probeIndex3.z, (probeIndex3.y * 9 + i) * 3 + 2);
        
        // Load and decode the fixed-point values
        c[i].r = DecodeFloatFromInt(coefficientVoxel3D.Load(int4(texCoordR, 0)));
        c[i].g = DecodeFloatFromInt(coefficientVoxel3D.Load(int4(texCoordG, 0)));
        c[i].b = DecodeFloatFromInt(coefficientVoxel3D.Load(int4(texCoordB, 0)));
    }
}

// Legacy functions for ComputeBuffer support
int3 GetProbeIndex3DFromWorldPos(float3 worldPos, float4 _coefficientVoxelSize, float _coefficientVoxelGridSize, float4 _coefficientVoxelCorner)
{
    float3 probeIndexF = floor((worldPos.xyz - _coefficientVoxelCorner.xyz) / _coefficientVoxelGridSize);
    int3 probeIndex3 = int3(probeIndexF.x, probeIndexF.y, probeIndexF.z);
    return probeIndex3;
}

int GetProbeIndex1DFromIndex3D(int3 probeIndex3, float4 _coefficientVoxelSize)
{
    int probeIndex = probeIndex3.x * _coefficientVoxelSize.y * _coefficientVoxelSize.z
                    + probeIndex3.y * _coefficientVoxelSize.z 
                    + probeIndex3.z;
    return probeIndex;
}

bool IsIndex3DInsideVoxel(int3 probeIndex3, float4 _coefficientVoxelSize)
{
    bool isInsideVoxelX = 0 <= probeIndex3.x && probeIndex3.x < _coefficientVoxelSize.x;
    bool isInsideVoxelY = 0 <= probeIndex3.y && probeIndex3.y < _coefficientVoxelSize.y;
    bool isInsideVoxelZ = 0 <= probeIndex3.z && probeIndex3.z < _coefficientVoxelSize.z;
    bool isInsideVoxel = isInsideVoxelX && isInsideVoxelY && isInsideVoxelZ;
    return isInsideVoxel;
}

void DecodeSHCoefficientFromVoxel(inout float3 c[9], in StructuredBuffer<int> _coefficientVoxel, int probeIndex)
{
    const int coefficientByteSize = 27; // 3x9 for SH9 RGB
    int offset = probeIndex * coefficientByteSize;
    for (int i = 0; i < 9; i++)
    {
        c[i].x = DecodeFloatFromInt(_coefficientVoxel[offset + i * 3 + 0]);
        c[i].y = DecodeFloatFromInt(_coefficientVoxel[offset + i * 3 + 1]);
        c[i].z = DecodeFloatFromInt(_coefficientVoxel[offset + i * 3 + 2]);
    }
}

float3 GetProbePositionFromIndex3D(int3 probeIndex3, float _coefficientVoxelGridSize, float4 _coefficientVoxelCorner)
{
    float3 res = float3(probeIndex3.x, probeIndex3.y, probeIndex3.z) * _coefficientVoxelGridSize + _coefficientVoxelCorner.xyz;
    return res;
}

float3 TrilinearInterpolationFloat3(in float3 value[8], float3 rate)
{
    float3 a = lerp(value[0], value[4], rate.x);    // 000, 100
    float3 b = lerp(value[2], value[6], rate.x);    // 010, 110
    float3 c = lerp(value[1], value[5], rate.x);    // 001, 101
    float3 d = lerp(value[3], value[7], rate.x);    // 011, 111
    float3 e = lerp(a, b, rate.y);
    float3 f = lerp(c, d, rate.y);
    float3 g = lerp(e, f, rate.z); 
    return g;
}

// Legacy ComputeBuffer sampling function
float3 SampleSHVoxel1D(
    in float3 worldPos, 
    in float3 albedo, 
    in float3 normal,
    in StructuredBuffer<int> coefficientVoxel,
    in float coefficientVoxelGridSize,
    in float4 coefficientVoxelCorner,
    in float4 coefficientVoxelSize
    )
{
    // probe grid index for current fragment
    int3 probeIndex3 = GetProbeIndex3DFromWorldPos(worldPos, coefficientVoxelSize, coefficientVoxelGridSize, coefficientVoxelCorner);
    int3 offset[8] = {
        int3(0, 0, 0), int3(0, 0, 1), int3(0, 1, 0), int3(0, 1, 1), 
        int3(1, 0, 0), int3(1, 0, 1), int3(1, 1, 0), int3(1, 1, 1), 
    };

    float3 c[9];
    float3 Lo[8] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), };
    float3 BRDF = albedo / PI;
    float weight = 0.0005;

    // near 8 probes
    for (int i = 0; i < 8; i++)
    {
        int3 idx3 = probeIndex3 + offset[i];
        bool isInsideVoxel = IsIndex3DInsideVoxel(idx3, coefficientVoxelSize);
        if (!isInsideVoxel)
        {
            Lo[i] = float3(0, 0, 0);
            continue;
        }

        // normal weight blend
        float3 probePos = GetProbePositionFromIndex3D(idx3, coefficientVoxelGridSize, coefficientVoxelCorner);
        float3 dir = normalize(probePos - worldPos);
        float normalWeight = saturate(dot(dir, normal));
        weight += normalWeight;

        // decode SH9
        int probeIndex = GetProbeIndex1DFromIndex3D(idx3, coefficientVoxelSize);
        DecodeSHCoefficientFromVoxel(c, coefficientVoxel, probeIndex);
        Lo[i] = IrradianceSH9(c, normal) * BRDF * normalWeight;
    }

    // trilinear interpolation
    float3 minCorner = GetProbePositionFromIndex3D(probeIndex3, coefficientVoxelGridSize, coefficientVoxelCorner);
    float3 maxCorner = minCorner + float3(1, 1, 1) * coefficientVoxelGridSize;
    float3 rate = (worldPos - minCorner) / coefficientVoxelGridSize;
    float3 color = TrilinearInterpolationFloat3(Lo, rate) / weight;
    
    return color;
}

// Sample SH coefficients from 3D texture with trilinear interpolation
float3 SampleSHVoxel3D(
    in float3 worldPos, 
    in float3 albedo, 
    in float3 normal,
    in Texture3D<int> coefficientVoxel3D,
    in float _coefficientVoxelGridSize,
    in float4 _coefficientVoxelCorner,
    in float4 _coefficientVoxelSize
)
{
    // probe grid index for current fragment
    int3 probeIndex3 = GetProbeIndex3DFromWorldPos(worldPos, _coefficientVoxelSize, _coefficientVoxelGridSize, _coefficientVoxelCorner);
    int3 offset[8] = {
        int3(0, 0, 0), int3(0, 0, 1), int3(0, 1, 0), int3(0, 1, 1), 
        int3(1, 0, 0), int3(1, 0, 1), int3(1, 1, 0), int3(1, 1, 1), 
    };

    float3 c[9];
    float3 Lo[8] = { float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), float3(0, 0, 0), };
    float3 BRDF = albedo / PI;
    float weight = 0.0005;

    // near 8 probes
    for (int i = 0; i < 8; i++)
    {
        int3 idx3 = probeIndex3 + offset[i];
        bool isInsideVoxel = IsIndex3DInsideVoxel(idx3, _coefficientVoxelSize);
        if (!isInsideVoxel)
        {
            Lo[i] = float3(0, 0, 0);
            continue;
        }

        // normal weight blend
        float3 probePos = GetProbePositionFromIndex3D(idx3, _coefficientVoxelGridSize, _coefficientVoxelCorner);
        float3 dir = normalize(probePos - worldPos);
        float normalWeight = saturate(dot(dir, normal));
        weight += normalWeight;

        // decode SH9 from 3D texture
        DecodeSHCoefficientFromVoxel3D(c, coefficientVoxel3D, idx3);
        Lo[i] = IrradianceSH9(c, normal) * BRDF * normalWeight;
    }

    // trilinear interpolation
    float3 minCorner = GetProbePositionFromIndex3D(probeIndex3, _coefficientVoxelGridSize, _coefficientVoxelCorner);
    float3 maxCorner = minCorner + float3(1, 1, 1) * _coefficientVoxelGridSize;
    float3 rate = (worldPos - minCorner) / _coefficientVoxelGridSize;
    float3 color = TrilinearInterpolationFloat3(Lo, rate) / weight;
    
    return color;
}
#endif // defined(PRT_SH_INCLUDED)