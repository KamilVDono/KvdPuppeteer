using System;
using System.Collections.Generic;
using KVD.Utils;
using KVD.Utils.DataStructures;
using UnityEditor;
using UnityEngine;

namespace KandraRenderer.Editor
{
    public class CreateCullingData : ScriptableWizard
    {
        private const float CullerOffset = 0.2f;
        private const float CullerDistance = CullerOffset * 2f;

        public KandraRenderer cullee;

        public KandraRenderer[] cullers;

        [MenuItem("Kandra/Cull")]
        public static void CreateWizard()
        {
            DisplayWizard<CreateCullingData>("Kandra cull", "Cull");
        }

        public void OnWizardCreate()
        {
            if(cullee.rendererData.mesh.submeshes.Length > 1)
            {
                Debug.LogError("Culling is not supported for meshes with multiple submeshes");
                return;
            }
            foreach (var culler in cullers)
            {
                CreateCuller(culler);
            }
        }

        private void CreateCuller(KandraRenderer culler)
        {
            var cullerTmpGo = new GameObject("Culler", typeof(MeshCollider));
            var cullerCollider = cullerTmpGo.GetComponent<MeshCollider>();
            cullerCollider.sharedMesh = culler.rendererData.sourceMesh;

            var vertices = cullee.rendererData.mesh.vertices;
            var indices = cullee.rendererData.sourceMesh.triangles;
            var trisCount = indices.Length / 3;

            var ranges = new List<KandraTrisCuller.CulledRange>();
            var rangeStart = -1;
            var rangeLength = 0;
            for (var i = 0u; i < trisCount; ++i)
            {
                bool shouldBeCulled = false;

                for (var j = 0u; !shouldBeCulled & j < 3; ++j)
                {
                    var index = (uint)indices[i * 3 + j];

                    var vertexPosition = vertices[index].position;
                    var vertexNormal = vertices[index].normal;

                    var ray = new Ray(vertexPosition + vertexNormal * CullerOffset, vertexNormal * -1);

                    if (cullerCollider.Raycast(ray, out _, CullerDistance))
                    {
                        shouldBeCulled = true;
                    }
                }

                if (shouldBeCulled)
                {
                    if (rangeStart == -1)
                    {
                        rangeStart = (int)i;
                    }
                    ++rangeLength;
                }
                else if (rangeStart != -1)
                {
                    ranges.Add(new KandraTrisCuller.CulledRange
                    {
                        start = (ushort)rangeStart,
                        length = (ushort)rangeLength
                    });
                    rangeStart = -1;
                    rangeLength = 0;
                }
            }

            if (rangeStart != -1)
            {
                ranges.Add(new KandraTrisCuller.CulledRange
                {
                    start = (ushort)rangeStart,
                    length = (ushort)rangeLength
                });
            }

            KandraTrisCullee culleeComponent = cullee.GetComponent<KandraTrisCullee>();
            if (!culleeComponent)
            {
                culleeComponent = cullee.gameObject.AddComponent<KandraTrisCullee>();
                culleeComponent.id = SerializableGuid.NewGuid();
                culleeComponent.kandraRenderer = cullee;
            }

            KandraTrisCuller cullerComponent = culler.GetComponent<KandraTrisCuller>();
            if(!cullerComponent)
            {
                cullerComponent = culler.gameObject.AddComponent<KandraTrisCuller>();
                cullerComponent.culledMeshes = Array.Empty<KandraTrisCuller.CulledMesh>();
            }

            var cullerId = Array.FindIndex(cullerComponent.culledMeshes, mesh => mesh.culleeId == culleeComponent.id);
            if (cullerId == -1)
            {
                cullerId = cullerComponent.culledMeshes.Length;
                Array.Resize(ref cullerComponent.culledMeshes, cullerId+1);
            }
            cullerComponent.culledMeshes[cullerId] = new KandraTrisCuller.CulledMesh
            {
                culleeId = culleeComponent.id,
                culledRanges = ranges.ToArray()
            };
            EditorUtility.SetDirty(cullerComponent);

            DestroyImmediate(cullerTmpGo);
        }
    }
}