using System;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace KandraRenderer
{
    [CreateAssetMenu(menuName = "Kandra/Mesh", order = 0)]
    public class KandraMesh : ScriptableObject {
        // -- OneTime data
        public UnsafeArray<CompressedVertex>.Span vertices;
        public UnsafeArray<AdditionalVertexData>.Span additionalData;
        public UnsafeArray<BoneWeights>.Span boneWeights;

        public UnsafeArray<float3x4>.Span bindposes;

        public UnsafeArray<Blendshape> blendshapesData;

        // -- Allways data
        public Bounds meshLocalBounds;
        public float4 localBoundingSphere;
        public SubmeshData[] submeshes;
        public string[] blendshapesNames;

        public ushort vertexCount;
        public ushort indicesCount;
        public ushort bindposesCount;

        public unsafe void AssignMeshData(UnsafeArray<byte> serializedData)
        {
            var ptr = serializedData.Ptr;

            vertices = UnsafeArray<CompressedVertex>.FromExistingData((CompressedVertex*)ptr, vertexCount);
            ptr += vertexCount * sizeof(CompressedVertex);

            additionalData = UnsafeArray<AdditionalVertexData>.FromExistingData((AdditionalVertexData*)ptr, vertexCount);
            ptr += vertexCount * sizeof(AdditionalVertexData);

            boneWeights = UnsafeArray<BoneWeights>.FromExistingData((BoneWeights*)ptr, vertexCount);
            ptr += vertexCount * sizeof(BoneWeights);

            bindposes = UnsafeArray<float3x4>.FromExistingData((float3x4*)ptr, bindposesCount);
            ptr += bindposesCount * sizeof(float3x4);

            blendshapesData = new UnsafeArray<Blendshape>((uint)blendshapesNames.Length, Allocator.Temp);
            for (var i = 0u; i < blendshapesNames.Length; i++)
            {
                blendshapesData[i].deltas = UnsafeArray<BlendshapeDeltas>.FromExistingData((BlendshapeDeltas*)ptr, vertexCount);
                ptr += vertexCount * sizeof(BlendshapeDeltas);
            }
        }

        public void DisposeMeshData()
        {
            if (blendshapesData.IsCreated)
            {
                blendshapesData.Dispose();
            }
        }

        [Serializable]
        public struct Blendshape
        {
            public UnsafeArray<BlendshapeDeltas>.Span deltas;

            public readonly uint Length => deltas.Length;

            public override int GetHashCode()
            {
                return deltas.GetHashCode();
            }
        }

        [Serializable]
        public struct BlendshapeDeltas
        {
            public uint2 positionDelta;
            public uint2 normalAndTangentDelta;

            public BlendshapeDeltas(float3 positionDelta, float3 normalDelta, float3 tangentDelta, float3 originalNormal, float4 originalTangent)
            {
                var halfPosition = math.f32tof16(positionDelta);
                this.positionDelta = default;
                this.positionDelta.x = halfPosition.x | (halfPosition.y << 16);
                this.positionDelta.y = halfPosition.z;

                var outputNormal = math.normalizesafe(normalDelta + originalNormal);
                var outputTangent = math.normalizesafe(tangentDelta + originalTangent.xyz);

                normalAndTangentDelta = MathUtils.EncodeNormalAndTangent(outputNormal, outputTangent);
            }
        }

        [Serializable]
        public struct AdditionalVertexData
        {
            public uint uv;
        }

        [Serializable]
        public struct BoneWeights
        {
            const uint LowMask = 0x0000_FFFF;
            const uint HighMask = 0xFFFF_0000;

            public uint2 indices;
            public uint2 weights;

            public ushort Index0
            {
                get => LoadLow(indices.x);
                set => indices.x = (indices.x & HighMask) | value;
            }

            public ushort Index1
            {
                get => LoadHigh(indices.x);
                set => indices.x = (indices.x & LowMask) | (uint)(value << 16);
            }

            public ushort Index2
            {
                get => LoadLow(indices.y);
                set => indices.y = (indices.y & HighMask) | value;
            }

            public ushort Index3
            {
                get => LoadHigh(indices.y);
                set => indices.y = (indices.y & LowMask) | (uint)(value << 16);
            }

            public float Weight0
            {
                get => LoadLow(weights.x) / (float)ushort.MaxValue;
                set => weights.x = (weights.x & HighMask) | math.f32tof16(value);
            }

            public float Weight1
            {
                get => LoadHigh(weights.x) / (float)ushort.MaxValue;
                set => weights.x = (weights.x & LowMask) | (math.f32tof16(value) << 16);
            }

            public float Weight2
            {
                get => LoadLow(weights.y) / (float)ushort.MaxValue;
                set => weights.y = (weights.y & HighMask) | math.f32tof16(value);
            }

            public float Weight3
            {
                get => LoadHigh(weights.y) / (float)ushort.MaxValue;
                set => weights.y = (weights.y & LowMask) | (math.f32tof16(value) << 16);
            }

            public BoneWeights(BoneWeight unityBoneWeight)
            {
                indices = default;
                weights = default;

                var weight3 = (ushort)(ushort.MaxValue * unityBoneWeight.weight3);
                var weight2 = (ushort)(ushort.MaxValue * unityBoneWeight.weight2);
                var weight1 = (ushort)(ushort.MaxValue * unityBoneWeight.weight1);
                var weight0 = ushort.MaxValue - (weight1 + weight2 + weight3);

                indices.x = (uint)(unityBoneWeight.boneIndex0 | (unityBoneWeight.boneIndex1 << 16));
                indices.y = (uint)(unityBoneWeight.boneIndex2 | (unityBoneWeight.boneIndex3 << 16));

                weights.x = (uint)(weight0 | (weight1 << 16));
                weights.y = (uint)(weight2 | (weight3 << 16));
            }

            public override string ToString()
            {
                return $"({Index0}[{Weight0:P1}], {Index1}[{Weight1:P1}], {Index2}[{Weight2:P1}], {Index3}[{Weight3:P1}])";
            }

            static ushort LoadLow(uint value)
            {
                return (ushort)(value & LowMask);
            }

            static ushort LoadHigh(uint value)
            {
                return (ushort)(value >> 16);
            }
        }

        [Serializable]
        public struct SubmeshData
        {
            public ushort indexStart;
            public ushort indexCount;
        }
    }
}