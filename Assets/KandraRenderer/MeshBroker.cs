using System.Collections.Generic;
using KVD.Utils.DataStructures;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace KandraRenderer {
    public class MeshBroker {
        Dictionary<int, MeshData> _originalMeshes = new Dictionary<int, MeshData>(KandraRendererManager.RenderersCapacity);

        public Mesh GetOriginalMesh(KandraMesh kandraMesh) {
            var hash = kandraMesh.GetHashCode();
            if (_originalMeshes.TryGetValue(hash, out var meshData)) {
                meshData.referenceCount++;
                _originalMeshes[hash] = meshData;
                return meshData.mesh;
            }

            var mesh = CreateOriginalMesh(kandraMesh);
            _originalMeshes.Add(hash, new MeshData {mesh = mesh, referenceCount = 1});
            return mesh;
        }

        public void ReleaseOriginalMesh(KandraMesh kandraMesh) {
            var hash = kandraMesh.GetHashCode();
            if (_originalMeshes.TryGetValue(hash, out var meshData)) {
                meshData.referenceCount--;
                if (meshData.referenceCount == 0) {
#if UNITY_EDITOR
                    if (Application.isPlaying) {
                        Object.Destroy(meshData.mesh);
                    } else {
                        Object.DestroyImmediate(meshData.mesh);
                    }
#else
                    Object.Destroy(meshData.mesh);
#endif
                    _originalMeshes.Remove(hash);
                } else {
                    _originalMeshes[hash] = meshData;
                }
            }
        }

        public Mesh CreateCullableMesh(KandraMesh kandraMesh, UnsafeArray<ushort> indices) {
            return CreateCulledMesh(kandraMesh, indices);
        }

        public void ReleaseCullableMesh(KandraMesh kandraMesh, Mesh mesh) {
#if UNITY_EDITOR
            if (Application.isPlaying) {
                Object.Destroy(mesh);
            } else {
                Object.DestroyImmediate(mesh);
            }
#else
            Object.Destroy(mesh);
#endif
        }

        unsafe Mesh CreateOriginalMesh(KandraMesh kandraMesh) {
            var indicesCount = kandraMesh.indicesCount;

            var indices = KandraRendererManager.Instance.StreamingManager.LoadIndicesData(kandraMesh);
            var dataArray = Mesh.AllocateWritableMeshData(1);
            var data = dataArray[0];
            data.SetVertexBufferParams(kandraMesh.vertexCount, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 1));
            data.SetIndexBufferParams(indicesCount, IndexFormat.UInt16);
            var meshIndices = data.GetIndexData<ushort>();
            UnsafeUtility.MemCpy(meshIndices.GetUnsafePtr(), indices.Ptr, indicesCount * sizeof(ushort));

            data.subMeshCount = kandraMesh.submeshes.Length;
            for (int i = 0; i < kandraMesh.submeshes.Length; i++) {
                var submesh = kandraMesh.submeshes[i];
                data.SetSubMesh(i, new SubMeshDescriptor(submesh.indexStart, submesh.indexCount), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            }

            var meshName = "Original";
#if UNITY_EDITOR
            meshName = kandraMesh.name + "_Original";
#endif
            var mesh = new Mesh
            {
                name = meshName,
            };
            Mesh.ApplyAndDisposeWritableMeshData(dataArray, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            mesh.UploadMeshData(true);

            return mesh;
        }

        unsafe Mesh CreateCulledMesh(KandraMesh kandraMesh, UnsafeArray<ushort> indices) {
            var indicesCount = (int)indices.Length;

            var dataArray = Mesh.AllocateWritableMeshData(1);
            var data = dataArray[0];
            data.SetVertexBufferParams(kandraMesh.vertexCount, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 1));
            data.SetIndexBufferParams(indicesCount, IndexFormat.UInt16);
            var meshIndices = data.GetIndexData<ushort>();
            UnsafeUtility.MemCpy(meshIndices.GetUnsafePtr(), indices.Ptr, indicesCount * sizeof(ushort));

            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, indicesCount), MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            var meshName = "Culled";
#if UNITY_EDITOR
            meshName = kandraMesh.name + "_Culled";
#endif
            var mesh = new Mesh
            {
                name = meshName,
            };
            Mesh.ApplyAndDisposeWritableMeshData(dataArray, mesh, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
            mesh.UploadMeshData(true);

            return mesh;
        }

        struct MeshData {
            public Mesh mesh;
            public int referenceCount;
        }
    }
}