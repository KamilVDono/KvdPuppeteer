using System.Text;
using KVD.Utils.DataStructures;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;

using static KandraRenderer.KandraRendererManager;

namespace KandraRenderer {
    [BurstCompile]
    public unsafe class BlendshapesManager {
        const int RenderersCapacity = KandraRendererManager.RenderersCapacity;
        const int BlendshapesCapacity = KandraRendererManager.BlendshapesCapacity;
        const int BlendshapesDeltasCapacity = KandraRendererManager.BlendshapesDeltasCapacity;

        static readonly ProfilerMarker UpdateBlendshapesMarker = new ProfilerMarker("KandraRenderer.UpdateBlendshapes");
        static readonly ProfilerMarker UpdateBuffersMarker = new ProfilerMarker("KandraRenderer.UpdateBlendshapesBuffers");

        static readonly int BlendshapeDataId = Shader.PropertyToID("_BlendshapeData");
        static readonly int BlendshapesDeltasId = Shader.PropertyToID("_BlendshapesDeltas");
        static readonly int BlendshapeIndicesAndWeightsId = Shader.PropertyToID("_BlendshapeIndicesAndWeights");

        readonly ComputeShader _skinningShader;
        readonly int _skinningKernel;

        GraphicsBuffer _blendshapeDataBuffer;
        GraphicsBuffer _blendshapesDeltasBuffer;
        GraphicsBuffer _blendshapeIndicesAndWeightsBuffer;

        UnsafeArray<UnsafeArray<float>> _weights;
        UnsafeArray<UnsafeArray<uint>> _indices;

        UnsafeHashMap<int, BlendshapesData> _blendshapes;
        MemoryBookkeeper _blendshapesMemory;


        public void OnGUI(StringBuilder sb, uint takenSlots, ref double used, ref double total) {
            sb.AppendLine(nameof(BlendshapesManager));

            LogBuffer(sb, _blendshapeDataBuffer, "BlendshapeDataBuffer", takenSlots, ref used, ref total);
            LogBuffer(sb, _blendshapesDeltasBuffer, "BlendshapesDeltasBuffer", _blendshapesMemory.LastBinStart, ref used, ref total);
            LogBuffer(sb, _blendshapeIndicesAndWeightsBuffer, "IndicesAndWeightsBuffer", takenSlots, ref used, ref total);
        }

        public BlendshapesManager(ComputeShader skinningShader) {
            _skinningShader = skinningShader;
            _skinningKernel = _skinningShader.FindKernel("CSSkinning");

            _blendshapeDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RenderersCapacity, sizeof(BlendshapesInstanceDatum));

            _blendshapesDeltasBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, BlendshapesDeltasCapacity, sizeof(KandraMesh.BlendshapeDeltas));
            _blendshapeIndicesAndWeightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, BlendshapesCapacity, sizeof(BlendshapeIndexAndWeight));

            _blendshapes = new UnsafeHashMap<int, BlendshapesData>(BlendshapesCapacity, Allocator.Persistent);
            _blendshapesMemory = new MemoryBookkeeper("Blendshape deltas", BlendshapesDeltasCapacity, RenderersCapacity/3, Allocator.Persistent);

            _weights = new UnsafeArray<UnsafeArray<float>>(RenderersCapacity, Allocator.Persistent);
            _indices = new UnsafeArray<UnsafeArray<uint>>(RenderersCapacity, Allocator.Persistent);

            EnsureBuffers();
        }

        public void Dispose() {
            _blendshapeDataBuffer?.Dispose();
            _blendshapesDeltasBuffer?.Dispose();
            _blendshapeIndicesAndWeightsBuffer?.Dispose();

            var blendshapesValues = _blendshapes.GetValueArray(Allocator.Temp);
            for (var i = 0; i < blendshapesValues.Length; i++) {
                blendshapesValues[i].Dispose();
            }
            blendshapesValues.Dispose();
            _blendshapes.Dispose();
            _blendshapesMemory.Dispose();
            _weights.Dispose();
            for (var i = 0u; i < _indices.Length; i++) {
                _indices[i].Dispose();
            }
            _indices.Dispose();
        }

        public void Register(uint slot, UnsafeArray<float> rendererWeights, KandraMesh mesh) {
            var hash = mesh.GetHashCode();
            var indices = new UnsafeArray<uint>((uint)mesh.blendshapesNames.Length, Allocator.Persistent);

            if(_blendshapes.TryGetValue(hash, out var dataArray)) {
                for (var i = 0u; i < dataArray.Length; ++i) {
                    indices[i] = dataArray.blendshapesMemory[i].start;
                }
                dataArray.refCount++;
                _blendshapes[hash] = dataArray;
            } else {
                var blendshapes = mesh.blendshapesData;
                var blendshapesData = new UnsafeArray<MemoryBookkeeper.MemoryRegion>(blendshapes.Length, Allocator.Persistent);
                for (var i = 0u; i < blendshapes.Length; ++i) {
                    var blendshape = blendshapes[i];

                    _blendshapesMemory.Take(blendshape.Length, out var blendshapesMemory);
                    _blendshapesDeltasBuffer.SetData(blendshape.deltas.AsUnsafeArray().AsNativeArray(), 0, (int)blendshapesMemory.start, (int)blendshapesMemory.length);

                    blendshapesData[i] = blendshapesMemory;
                    indices[i] = blendshapesMemory.start;
                }
                dataArray = new BlendshapesData(blendshapesData);
                _blendshapes.TryAdd(hash, dataArray);
            }

            _indices[slot] = indices;
            _weights[slot] = rendererWeights;
        }

        public void Unregister(uint slot, KandraMesh mesh) {
            var hash = mesh.GetHashCode();
            if (_blendshapes.TryGetValue(hash, out var dataArray)) {
                dataArray.refCount--;
                if (dataArray.refCount == 0) {
                    for (var i = 0u; i < dataArray.Length; i++) {
                        _blendshapesMemory.Return(dataArray.blendshapesMemory[i]);
                    }
                    dataArray.Dispose();
                    _blendshapes.Remove(hash);
                } else {
                    _blendshapes[hash] = dataArray;
                }
            }

            _indices[slot].Dispose();
        }

        public void EnsureBuffers() {
            _skinningShader.SetBuffer(_skinningKernel, BlendshapeDataId, _blendshapeDataBuffer);
            _skinningShader.SetBuffer(_skinningKernel, BlendshapesDeltasId, _blendshapesDeltasBuffer);
            _skinningShader.SetBuffer(_skinningKernel, BlendshapeIndicesAndWeightsId, _blendshapeIndicesAndWeightsBuffer);
        }

        public void UpdateBlendshapes(UnsafeBitmask takenSlots) {
            UpdateBlendshapesMarker.Begin();
            var nonZeroIndicesAndWeights = new NativeList<BlendshapeIndexAndWeight>(_weights.LengthInt, Allocator.Temp);
            var instancesData = new UnsafeArray<BlendshapesInstanceDatum>(_weights.Length, Allocator.Temp);

            CollectActiveBlendshapesData(_weights, _indices, takenSlots, ref nonZeroIndicesAndWeights, ref instancesData);

            UpdateBuffersMarker.Begin();
            _blendshapeIndicesAndWeightsBuffer.SetData(nonZeroIndicesAndWeights.AsArray());
            _blendshapeDataBuffer.SetData(instancesData.AsNativeArray());
            UpdateBuffersMarker.End();

            nonZeroIndicesAndWeights.Dispose();
            instancesData.Dispose();
            UpdateBlendshapesMarker.End();
        }

        [BurstCompile]
        static void CollectActiveBlendshapesData(in UnsafeArray<UnsafeArray<float>> weights, in UnsafeArray<UnsafeArray<uint>> indices,
            in UnsafeBitmask takenSlots, ref NativeList<BlendshapeIndexAndWeight> nonZeroIndicesAndWeights, ref UnsafeArray<BlendshapesInstanceDatum> instancesData) {
            foreach (var i in takenSlots.EnumerateOnes()) {
                var subWeights = weights[i];
                var start = (uint)nonZeroIndicesAndWeights.Length;

                for (var j = 0u; j < subWeights.Length; j++) {
                    if (subWeights[j] > 0.001f) {
                        var blendshapeIndexAndWeight = new BlendshapeIndexAndWeight
                        {
                            index = indices[i][j],
                            weight = subWeights[j]
                        };
                        nonZeroIndicesAndWeights.Add(blendshapeIndexAndWeight);
                    }
                }

                var instanceData = new BlendshapesInstanceDatum
                {
                    startAndLengthOfWeights = start | (uint)((nonZeroIndicesAndWeights.Length - start) << 16),
                };
                instancesData[i] = instanceData;
            }
        }

        struct BlendshapesInstanceDatum {
            public uint startAndLengthOfWeights;
        }

        struct BlendshapeIndexAndWeight {
            public uint index;
            public float weight;
        }

        struct BlendshapesData {
            public readonly UnsafeArray<MemoryBookkeeper.MemoryRegion> blendshapesMemory;
            public int refCount;

            public readonly uint Length => blendshapesMemory.Length;

            public BlendshapesData(UnsafeArray<MemoryBookkeeper.MemoryRegion> memory) {
                blendshapesMemory = memory;
                refCount = 1;
            }

            public void Dispose() {
                blendshapesMemory.Dispose();
            }
        }
    }
}