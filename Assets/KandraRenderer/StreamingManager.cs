using System.IO;
using KVD.Utils.DataStructures;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;

namespace KandraRenderer {
    public class StreamingManager {
        static string BasePath => Path.Combine(Application.streamingAssetsPath, "Kandra");

        UnsafeHashMap<int, UnsafeArray<byte>> _meshesCache;
        UnsafeHashMap<int, UnsafeArray<ushort>> _indicesCache;

        public StreamingManager() {
            _meshesCache = new UnsafeHashMap<int, UnsafeArray<byte>>(16, Allocator.Persistent);
            _indicesCache = new UnsafeHashMap<int, UnsafeArray<ushort>>(16, Allocator.Persistent);
        }

        public void Dispose() {
            OnFrameEnd();
            _meshesCache.Dispose();
            _indicesCache.Dispose();
        }

        public static string MeshDataPath(Mesh mesh) {
#if UNITY_EDITOR
            if (!Directory.Exists(BasePath)) {
                Directory.CreateDirectory(BasePath);
            }
            var meshFullName = $"{KandraMeshName(mesh)}.mdkandra";
            return Path.Combine(BasePath, meshFullName);
#else
            Debug.LogError("Mesh data path is not available in build");
            return null;
#endif
        }

        public static string IndicesDataPath(Mesh mesh) {
#if UNITY_EDITOR
            if (!Directory.Exists(BasePath)) {
                Directory.CreateDirectory(BasePath);
            }
            var meshFullName = $"{KandraMeshName(mesh)}.ixkandra";
            return Path.Combine(BasePath, meshFullName);
#else
            Debug.LogError("Mesh data path is not available in build");
            return null;
#endif
        }

        public static string KandraMeshName(Mesh mesh) {
#if UNITY_EDITOR
            var meshPath = UnityEditor.AssetDatabase.GetAssetPath(mesh);
            var fbx = UnityEditor.AssetDatabase.LoadMainAssetAtPath(meshPath);
            return $"{fbx.name}_{mesh.name}";
#else
            Debug.LogError("Mesh data path is not available in build");
            return null;
#endif
        }

        public static string MeshDataPath(KandraMesh mesh) {
            return Path.Combine(BasePath, mesh.name + ".mdkandra");
        }

        public static string IndicesDataPath(KandraMesh mesh) {
            return Path.Combine(BasePath, mesh.name + ".ixkandra");
        }

        public UnsafeArray<byte> LoadMeshData(KandraMesh kandraMesh) {
            var hash = kandraMesh.GetHashCode();
            if (!_meshesCache.TryGetValue(hash, out var meshData)) {
                meshData = Buffer<byte>(MeshDataPath(kandraMesh), Allocator.Temp);
                _meshesCache.TryAdd(hash, meshData);
            }
            return meshData;
        }

        public UnsafeArray<ushort> LoadIndicesData(KandraMesh kandraMesh) {
            var hash = kandraMesh.GetHashCode();
            if (!_indicesCache.TryGetValue(hash, out var indices)) {
                indices = Buffer<ushort>(IndicesDataPath(kandraMesh), 0, kandraMesh.indicesCount, Allocator.Temp);
                _indicesCache.TryAdd(hash, indices);
            }
            return indices;
        }

        public void OnFrameEnd() {
            foreach (var meshes in _meshesCache) {
                meshes.Value.Dispose();
            }
            _meshesCache.Clear();
            foreach (var indices in _indicesCache) {
                indices.Value.Dispose();
            }
            _indicesCache.Clear();
        }

        static unsafe UnsafeArray<T> Buffer<T>(string filepath, Allocator allocator) where T : unmanaged {
            var fileInfo = default(FileInfoResult);
            AsyncReadManager.GetFileInfo(filepath, &fileInfo).JobHandle.Complete();

            var cellCount = (uint)(fileInfo.FileSize / UnsafeUtility.SizeOf<T>());
            return Buffer<T>(filepath, 0, cellCount, allocator);
        }

        static unsafe UnsafeArray<T> Buffer<T>(string filepath, long offset, uint count, Allocator allocator) where T : unmanaged {
            var buffer = new UnsafeArray<T>(count, allocator, NativeArrayOptions.UninitializedMemory);
            var readCommand = new ReadCommand
            {
                Offset = offset,
                Size = UnsafeUtility.SizeOf<T>() * count,
                Buffer = buffer.Ptr,
            };
            var readHandle = AsyncReadManager.Read(filepath, &readCommand, 1);
            AsyncReadManager.CloseCachedFileAsync(filepath, readHandle.JobHandle).Complete();
            return buffer;
        }
    }
}