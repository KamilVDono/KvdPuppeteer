using System.Text;
using KVD.Utils.DataStructures;
using Unity.Collections;
using UnityEngine;

using static KandraRenderer.KandraRendererManager;

namespace KandraRenderer {
    public unsafe class SkinningManager {
        const int SkinnedVerticesCapacity = KandraRendererManager.SkinnedVerticesCapacity;
        const int MaxRenderers = KandraRendererManager.RenderersCapacity;

        static readonly int VerticesBufferId = Shader.PropertyToID("_VerticesBuffer");
        static readonly int VertexOffsetId = Shader.PropertyToID("_VertexOffset");
        static readonly int SkinningVerticesDataId = Shader.PropertyToID("_SkinningVerticesData");
        static readonly int RenderersDataId = Shader.PropertyToID("_RenderersData");

        static readonly int OutputVerticesId = Shader.PropertyToID("_OutputVertices");
        static readonly int VertexCountId = Shader.PropertyToID("_VertexCount");

        readonly ComputeShader _skinningComputeShader;
        readonly int _skinningKernel;
        readonly uint _xGroupSize;

        GraphicsBuffer _skinningVerticesDataBuffer;
        GraphicsBuffer _renderersDataBuffer;
        GraphicsBuffer _outputVerticesBuffer;

        UnsafeArray<RegisteredRenderer> _registeredRenderers;
        MemoryBookkeeper _memoryRegions;

        uint VerticesCount => _memoryRegions.LastBinStart;

        public void OnGUI(StringBuilder sb, uint slotsTaken, ref double used, ref double total) {
            sb.AppendLine(nameof(SkinningManager));

            LogBuffer(sb, _skinningVerticesDataBuffer, "SkinningVerticesDataBuffer", VerticesCount, ref used, ref total);
            LogBuffer(sb, _renderersDataBuffer, "RenderersDataDataBuffer", slotsTaken, ref used, ref total);
            LogBuffer(sb, _outputVerticesBuffer, "OutputVerticesBuffer", VerticesCount, ref used, ref total);
        }

        public SkinningManager(ComputeShader skinningShader) {
            _skinningComputeShader = skinningShader;
            _skinningKernel = _skinningComputeShader.FindKernel("CSSkinning");
            _skinningComputeShader.GetKernelThreadGroupSizes(_skinningKernel, out _xGroupSize, out _, out _);

            _skinningVerticesDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SkinnedVerticesCapacity, sizeof(SkinningVerticesDatum));
            _renderersDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxRenderers, sizeof(RendererDatum));
            _outputVerticesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, SkinnedVerticesCapacity, sizeof(CompressedVertex));

            _skinningComputeShader.SetInt(VertexCountId, 0);

            _registeredRenderers = new UnsafeArray<RegisteredRenderer>(MaxRenderers, Allocator.Persistent);
            _memoryRegions = new MemoryBookkeeper("Verts skinned", SkinnedVerticesCapacity, MaxRenderers/3, Allocator.Persistent);

            EnsureBuffers();
        }

        public void Dispose() {
            _skinningVerticesDataBuffer?.Dispose();
            _renderersDataBuffer?.Dispose();
            _outputVerticesBuffer?.Dispose();

            if (_registeredRenderers.IsCreated) {
                _registeredRenderers.Dispose();
                _memoryRegions.Dispose();
            }
        }

        public void Register(uint slot, MemoryBookkeeper.MemoryRegion meshMemory, uint bonesOffset, out uint startVertex) {
            _memoryRegions.Take(meshMemory.length, out var rendererRegion);

            _registeredRenderers[slot] = new RegisteredRenderer
            {
                memory = rendererRegion
            };

            startVertex = rendererRegion.start;
            var verticesStart = (int)rendererRegion.start;

            var verticesData = new UnsafeArray<SkinningVerticesDatum>(rendererRegion.length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (var i = 0u; i < meshMemory.length; ++i) {
                verticesData[i] = new SkinningVerticesDatum
                {
                    vertexIndexAndRendererIndex = i | (slot << 16)
                };
            }
            _skinningVerticesDataBuffer.SetData(verticesData.AsNativeArray(), 0, verticesStart, (int)verticesData.Length);
            verticesData.Dispose();

            var rendererDataArray = new UnsafeArray<RendererDatum>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            rendererDataArray[0] = new RendererDatum
            {
                meshStart = meshMemory.start,
                bonesStart = bonesOffset
            };
            _renderersDataBuffer.SetData(rendererDataArray.AsNativeArray(), 0, (int)slot, 1);
            rendererDataArray.Dispose();
        }

        public void Unregister(uint slot) {
            var renderer = _registeredRenderers[slot];
            _memoryRegions.Return(renderer.memory);
        }

        public void EnsureBuffers() {
            _skinningComputeShader.SetBuffer(_skinningKernel, SkinningVerticesDataId, _skinningVerticesDataBuffer);
            _skinningComputeShader.SetBuffer(_skinningKernel, RenderersDataId, _renderersDataBuffer);
            _skinningComputeShader.SetBuffer(_skinningKernel, OutputVerticesId, _outputVerticesBuffer);

            Shader.SetGlobalBuffer(VerticesBufferId, _outputVerticesBuffer);

            _skinningComputeShader.SetInt(VertexCountId, (int)VerticesCount);
        }

        public void RunSkinning() {
            const int MaxDispatches = 60_000;

            if (VerticesCount > 0) {
                var dispatchCount = Mathf.CeilToInt((float)VerticesCount / _xGroupSize);
                var vertexOffset = 0;
                while(dispatchCount > 0) {
                    var dispatches = Mathf.Min(dispatchCount, MaxDispatches);
                    _skinningComputeShader.SetInt(VertexOffsetId, vertexOffset);
                    _skinningComputeShader.Dispatch(_skinningKernel, dispatches, 1, 1);
                    dispatchCount -= dispatches;
                    vertexOffset += dispatches * (int)_xGroupSize;
                }
            }
        }

        struct RegisteredRenderer {
            public MemoryBookkeeper.MemoryRegion memory;
        }

        struct SkinningVerticesDatum {
            public uint vertexIndexAndRendererIndex;
        }

        struct RendererDatum
        {
            public uint meshStart;
            public uint bonesStart;
        }
    }
}