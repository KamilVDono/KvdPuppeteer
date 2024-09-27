using System.Collections.Generic;
using KVD.Utils;
using KVD.Utils.DataStructures;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace KandraRenderer
{
    [RequireComponent(typeof(KandraRenderer)), BurstCompile]
    public class KandraTrisCullee : MonoBehaviour
    {
        public SerializableGuid id;
        public KandraRenderer kandraRenderer;

        UnsafeBitmask _visibleTris;
        List<KandraTrisCuller> _cullers;

        void Awake()
        {
            _cullers = new List<KandraTrisCuller>(8);
        }

        void OnDestroy()
        {
            if (_visibleTris.IsCreated)
            {
                _visibleTris.Dispose();
            }
            _cullers.Clear();
            kandraRenderer.ReleaseCullableMesh();
            _cullers = null;
        }

        public void Cull(KandraTrisCuller culler)
        {
            if (_cullers == null)
            {
                return;
            }
            if (_cullers.Contains(culler))
            {
                Debug.LogWarning("Trying to cull a culler that was already culled");
                return;
            }
            _cullers.Add(culler);
            UpdateCulledMesh();
        }

        public void Uncull(KandraTrisCuller culler)
        {
            if (_cullers == null)
            {
                return;
            }
            var removed = _cullers.Remove(culler);
            if (!removed)
            {
                Debug.LogWarning("Trying to uncull a culler that was not culled");
                return;
            }

            if (_cullers.Count == 0)
            {
                kandraRenderer.ReleaseCullableMesh();
            }
            else
            {
                UpdateCulledMesh();
            }
        }

        unsafe void UpdateCulledMesh()
        {
            if (!_visibleTris.IsCreated)
            {
                CreateVisibleTris();
            }

            _visibleTris.All();
            foreach (var culler in _cullers)
            {
                culler.DisableCulledTriangles(id, ref _visibleTris);
            }

            var trisCount = _visibleTris.CountOnes();
            var indicesCount = trisCount*3;

            if (indicesCount == kandraRenderer.rendererData.mesh.indicesCount)
            {
                kandraRenderer.ReleaseCullableMesh();
            }
            else
            {
                var indices = KandraRendererManager.Instance.StreamingManager.LoadIndicesData(kandraRenderer.rendererData.mesh);
                var culledIndices = new UnsafeArray<ushort>(indicesCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                FillNewIndices(indices.Ptr, culledIndices, _visibleTris);

                kandraRenderer.UpdateCulledMesh(culledIndices);
                culledIndices.Dispose();
            }
        }

        void CreateVisibleTris()
        {
            var indicesCount = kandraRenderer.rendererData.mesh.indicesCount/3;
            _visibleTris = new UnsafeBitmask((uint)indicesCount, Allocator.Persistent);
        }

        [BurstCompile]
        static unsafe void FillNewIndices(ushort* originalIndices, in UnsafeArray<ushort> culledIndices, in UnsafeBitmask visibleTris)
        {
            var originalTrianglesPtr = (Triangle*)originalIndices;
            var culledTrianglesPtr = (Triangle*)culledIndices.Ptr;
            var i = 0u;
            foreach (var triangleIndex in visibleTris.EnumerateOnes())
            {
                culledTrianglesPtr[i++] = originalTrianglesPtr[triangleIndex];
            }
        }

        struct Triangle
        {
            ushort i1;
            ushort i2;
            ushort i3;
        }
    }
}