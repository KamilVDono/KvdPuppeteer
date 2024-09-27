using System;
using KVD.Utils;
using KVD.Utils.DataStructures;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace KandraRenderer {
    public class KandraTrisCuller : MonoBehaviour {
        public CulledMesh[] culledMeshes;

        KandraTrisCullee _cullee;

        void OnDestroy() {
            if (_cullee) {
                _cullee.Uncull(this);
            }
        }

        public void Cull(KandraTrisCullee cullee) {
            cullee.Cull(this);
            _cullee = cullee;
        }

        public unsafe void DisableCulledTriangles(Guid culee, ref UnsafeBitmask visibleTris) {
            var culledMeshe = Array.Find(culledMeshes, mesh => mesh.culleeId == culee);
            if (culledMeshe.culleeId == Guid.Empty) {
                Debug.LogWarning("Trying to disable culled triangles for a cullee that was not culled");
                return;
            }
            var culledRanges = culledMeshe.culledRanges;
            fixed(CulledRange* culledRangesPtr = culledRanges) {
                new DisableCulledTrianglesJob
                {
                    visibleTris = visibleTris,
                    culledRanges = culledRangesPtr,
                    culledRangesLength = culledRanges.Length
                }.Run();
            }
        }

        [Serializable]
        public struct CulledMesh {
            public SerializableGuid culleeId;
            public CulledRange[] culledRanges;
        }

        [Serializable]
        public struct CulledRange {
            public ushort start;
            public ushort length;
        }

        [BurstCompile]
        unsafe struct DisableCulledTrianglesJob : IJob {
            public UnsafeBitmask visibleTris;
            [NativeDisableUnsafePtrRestriction] public CulledRange* culledRanges;
            public int culledRangesLength;

            public void Execute() {
                for (var i = 0; i < culledRangesLength; ++i) {
                    var range = culledRanges[i];
                    visibleTris.Down(range.start, range.length);
                }
            }
        }
    }
}