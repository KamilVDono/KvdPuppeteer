#ifndef __MATRIX_INCLUDED__
#define __MATRIX_INCLUDED__

#include "Compression.hlsl"

struct Vertex
{
    float3 position;
    float3 normal;
    float3 tangent;
};

struct CompressedVertex
{
    float3 position;
    uint2 normalAndTangent;
};

struct BlendshapeDeltas
{
    uint2 positionDelta;
    uint2 normalAndTangentDelta;
};

uint2 EncodeNormalAndTangent(const float3 normal, const float3 tangent)
{
    const uint encodedNormal = EncodeNormalVectorOctahedron(normal);
    const uint encodedTangent = EncodeNormalVectorOctahedron(tangent);
    return uint2(encodedNormal, encodedTangent);
}

void DecodeNormalAndTangent(const uint2 encoded, out float3 normal, out float3 tangent)
{
    normal = DecodeNormalVectorOctahedron(encoded.x);
    tangent = DecodeNormalVectorOctahedron(encoded.y);
}

#endif