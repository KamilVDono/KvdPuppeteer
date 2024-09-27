//#define UNITY_DOTS_INSTANCING_ENABLED
#ifndef SAMPLE_SKIN_BUFFER_INCLUDED
#define SAMPLE_SKIN_BUFFER_INCLUDED

#include "Matrices.hlsl"

#if defined(UNITY_DOTS_INSTANCING_ENABLED)
uniform ByteAddressBuffer _VerticesBuffer;
uniform ByteAddressBuffer _AdditionalVerticesData;
#endif

void sampleDeform(uint vertexId, uint2 instanceData, inout float3 position, inout float3 normal, inout float3 tangent, inout float2 uv)
{
    #if defined(UNITY_DOTS_INSTANCING_ENABLED)
    uint bytesStart = (vertexId + instanceData.x) * (12 + 8);
    position = asfloat(_VerticesBuffer.Load3(bytesStart));
    const uint2 normalAndTangent = _VerticesBuffer.Load2(bytesStart + 12);

    DecodeNormalAndTangent(normalAndTangent, normal, tangent);

    bytesStart = (vertexId + instanceData.y) * (4);
    const uint uvCompressed = _AdditionalVerticesData.Load(bytesStart);
    uv.x = f16tof32(uvCompressed & 0x0000FFFF);
    uv.y = f16tof32(uvCompressed >> 16);
    #endif
}

#endif