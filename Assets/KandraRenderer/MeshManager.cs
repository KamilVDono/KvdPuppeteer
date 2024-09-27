using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using static KandraRenderer.KandraRendererManager;

namespace KandraRenderer {
    public unsafe class MeshManager {
        const int VerticesCapacity = KandraRendererManager.UniqueVerticesCapacity;
        const int BindposesCapacity = KandraRendererManager.UniqueBindposesCapacity;
        const int UniqueMeshesCapacity = KandraRendererManager.UniqueMeshesCapacity;

        static readonly int BoneWeightsId = Shader.PropertyToID("_BoneWeights");
        static readonly int OriginalVerticesId = Shader.PropertyToID("_OriginalVertices");
        static readonly int AdditionalVerticesDataId = Shader.PropertyToID("_AdditionalVerticesData");
        static readonly int BindPosesId = Shader.PropertyToID("_Bindposes");

        readonly ComputeShader _prepareBonesShader;
        readonly int _prepareBonesKernel;
        readonly ComputeShader _skinningShader;
        readonly int _skinningKernel;

        GraphicsBuffer _bindPosesBuffer;
        GraphicsBuffer _originalVerticesBuffer;
        GraphicsBuffer _additionalVerticesDataBuffer;
        GraphicsBuffer _boneWeightsBuffer;

        MemoryBookkeeper _bindPosesMemory;
        MemoryBookkeeper _verticesMemory;
        UnsafeHashMap<int, MeshData> _meshes;

        public void OnGUI(StringBuilder sb, ref double used, ref double total) {
            sb.AppendLine(nameof(MeshManager));
            LogBuffer(sb, _bindPosesBuffer, "BindPosesBuffer", _bindPosesMemory.LastBinStart, ref used, ref total);
            LogBuffer(sb, _originalVerticesBuffer, "OriginalVerticesBuffer", _verticesMemory.LastBinStart, ref used, ref total);
            LogBuffer(sb, _boneWeightsBuffer, "BoneWeightsBuffer", _verticesMemory.LastBinStart, ref used, ref total);
        }

        public MeshManager(ComputeShader skinningShader, ComputeShader prepareBonesShader) {
            _prepareBonesShader = prepareBonesShader;
            _prepareBonesKernel = _prepareBonesShader.FindKernel("CSPrepareBones");
            _skinningShader = skinningShader;
            _skinningKernel = _skinningShader.FindKernel("CSSkinning");

            _bindPosesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, BindposesCapacity, sizeof(float3x4));
            _originalVerticesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VerticesCapacity, sizeof(CompressedVertex));
            _additionalVerticesDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, VerticesCapacity, sizeof(KandraMesh.AdditionalVertexData));
            _boneWeightsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, VerticesCapacity, sizeof(KandraMesh.BoneWeights));

            _bindPosesMemory = new MemoryBookkeeper("Bindposes mesh", BindposesCapacity, UniqueMeshesCapacity/3, Allocator.Persistent);
            _verticesMemory = new MemoryBookkeeper("Verts mesh", VerticesCapacity, UniqueMeshesCapacity/3, Allocator.Persistent);
            _meshes = new UnsafeHashMap<int, MeshData>(UniqueMeshesCapacity, Allocator.Persistent);

            EnsureBuffers();
        }

        public void Dispose() {
            _originalVerticesBuffer?.Dispose();
            _additionalVerticesDataBuffer?.Dispose();
            _bindPosesBuffer?.Dispose();
            _boneWeightsBuffer?.Dispose();
            _bindPosesMemory.Dispose();
            _verticesMemory.Dispose();
            _meshes.Dispose();
        }

        public MeshMemory RegisterMesh(KandraMesh mesh) {
            var hash = mesh.GetHashCode();

            if (_meshes.TryGetValue(hash, out var data)) {
                data.refCount++;
                _meshes[hash] = data;
                return data.memory;
            }

            mesh.AssignMeshData(KandraRendererManager.Instance.StreamingManager.LoadMeshData(mesh));

            _verticesMemory.Take(mesh.vertexCount, out var verticesRegion);
            _bindPosesMemory.Take(mesh.bindposesCount, out var bindPosesRegion);
            var meshMemory = new MeshMemory(bindPosesRegion, verticesRegion);

            data = new MeshData(meshMemory);

            // Copy mesh data to GPU
            _originalVerticesBuffer.SetData(mesh.vertices.AsUnsafeArray().AsNativeArray(), 0, (int)verticesRegion.start, (int)verticesRegion.length);
            _additionalVerticesDataBuffer.SetData(mesh.additionalData.AsUnsafeArray().AsNativeArray(), 0, (int)verticesRegion.start, (int)verticesRegion.length);
            _boneWeightsBuffer.SetData(mesh.boneWeights.AsUnsafeArray().AsNativeArray(), 0, (int)verticesRegion.start, (int)verticesRegion.length);
            _bindPosesBuffer.SetData(mesh.bindposes.AsUnsafeArray().AsNativeArray(), 0, (int)bindPosesRegion.start, (int)bindPosesRegion.length);

            _meshes[hash] = data;
            return meshMemory;
        }

        public void UnregisterMesh(KandraMesh mesh) {
            var hash = mesh.GetHashCode();
            if (_meshes.TryGetValue(hash, out var data)) {
                data.refCount--;
                if (data.refCount == 0) {
                    _bindPosesMemory.Return(data.memory.bindPosesMemory);
                    _verticesMemory.Return(data.memory.verticesMemory);
                    _meshes.Remove(hash);
                } else {
                    _meshes[hash] = data;
                }
            }
        }

        public void EnsureBuffers() {
            _prepareBonesShader.SetBuffer(_prepareBonesKernel, BindPosesId, _bindPosesBuffer);
            _skinningShader.SetBuffer(_skinningKernel, BoneWeightsId, _boneWeightsBuffer);
            _skinningShader.SetBuffer(_skinningKernel, OriginalVerticesId, _originalVerticesBuffer);

            Shader.SetGlobalBuffer(AdditionalVerticesDataId, _additionalVerticesDataBuffer);
        }

        public MeshMemory GetMeshMemory(KandraMesh mesh)
        {
            var hash = mesh.GetHashCode();
            return _meshes[hash].memory;

        }

        struct MeshData
        {
            public readonly MeshMemory memory;
            public int refCount;

            public MeshData(MeshMemory memory)
            {
                this.memory = memory;
                refCount = 1;
            }
        }

        public readonly struct MeshMemory
        {
            public readonly MemoryBookkeeper.MemoryRegion bindPosesMemory;
            public readonly MemoryBookkeeper.MemoryRegion verticesMemory;

            public MeshMemory(MemoryBookkeeper.MemoryRegion bindPosesMemory, MemoryBookkeeper.MemoryRegion verticesMemory)
            {
                this.bindPosesMemory = bindPosesMemory;
                this.verticesMemory = verticesMemory;
            }
        }
    }
}