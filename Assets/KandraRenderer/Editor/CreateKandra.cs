using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KVD.Puppeteer;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace KandraRenderer.Editor {
    [BurstCompile]
    public class CreateKandra : ScriptableWizard {
        public GameObject[] targets;

        [MenuItem("Kandra/Wizard")]
        public static void CreateWizard()
        {
            DisplayWizard<CreateKandra>("Kandra", "CREATE");
        }

        public void OnWizardCreate()
        {
            foreach (var target in targets)
            {
                ProcessSingleTarget(target);
            }
        }

        private void ProcessSingleTarget(GameObject target)
        {
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(target))
            {
                PrefabUtility.UnpackPrefabInstance(target, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                target.name = target.name.Replace("Unity", "Kandra");
            }

            var targetTransform = target.transform;
            var skinnedRenderers = target.GetComponentsInChildren<SkinnedMeshRenderer>();

            var rig = target.AddComponent<KandraRig>();

            var allUsedBones = new List<Transform>();
            foreach (var skinnedRenderer in skinnedRenderers)
            {
                var mesh = skinnedRenderer.sharedMesh;
                var usedBones = new UnsafeBitmask((uint)skinnedRenderer.bones.Length, Allocator.Temp);
                CollectUsedBones(mesh, ref usedBones);
                var meshBones = skinnedRenderer.bones;
                for (var index = 0u; index < meshBones.Length; index++)
                {
                    if (usedBones[index])
                    {
                        var bone = meshBones[index];
                        // Include all parent bones as we need them for stitching
                        var parentBone = bone.parent;
                        while (parentBone != targetTransform && !allUsedBones.Contains(parentBone))
                        {
                            allUsedBones.Add(parentBone);
                            parentBone = parentBone.parent;
                        }
                        allUsedBones.Add(meshBones[index]);
                    }
                }
                allUsedBones.Add(skinnedRenderer.rootBone);
            }
            var allBones = allUsedBones.Distinct().ToArray();

            var parentByBone = new Dictionary<Transform, Transform>();
            foreach (var bone in allBones)
            {
                var parent = bone.parent;
                if (parent != null)
                {
                    parentByBone[bone] = parent;
                }
            }

            rig.virtualBones = target.GetComponent<VirtualBones>();
            // rig.boneNames = allBones.Select(b => new FixedString32Bytes(b.name.Substring(0, math.min(FixedString32Bytes.UTF8MaxLengthInBytes, b.name.Length)))).ToArray();
            // rig.boneParents = allBones.Select(b =>
            //         parentByBone.TryGetValue(b, out var parent)
            //             ? (ushort)Array.IndexOf(allBones, parent)
            //             : (ushort)0xFFFF)
            //     .ToArray();

            foreach (var skinnedRenderer in skinnedRenderers)
            {
                ProcessSkinnedMeshRenderer(skinnedRenderer, rig);
            }
        }

        private void ProcessSkinnedMeshRenderer(SkinnedMeshRenderer skinnedRenderer, KandraRig rig)
        {
            var gameObject = skinnedRenderer.gameObject;
            var mesh = skinnedRenderer.sharedMesh;

            gameObject.SetActive(false);

            var skinnedMeshBones = skinnedRenderer.bones;

            var usedBones = new UnsafeBitmask((uint)skinnedMeshBones.Length, Allocator.Temp);
            usedBones.Up((uint)Array.IndexOf(skinnedRenderer.bones, skinnedRenderer.rootBone));
            CollectUsedBones(mesh, ref usedBones);
            var bonesMap = CreateBonesMap(in usedBones);

            var localRootBoneIndex = Array.IndexOf(skinnedRenderer.bones, skinnedRenderer.rootBone);
            var kandraMesh = CreateKandraMesh(mesh, usedBones, bonesMap, localRootBoneIndex, out var rootBoneBindpose);

            var bonesNames = rig.virtualBones.SharedSkeleton.boneNames;
            var bones = skinnedMeshBones.Where(FilterBones).Select(b => (ushort)bonesNames.FindIndexOf(b.name.ToFixedString32())).ToArray();

            var registerData = new KandraRenderer.RendererData
            {
                sourceMesh = mesh,

                rig = rig,
                mesh = kandraMesh,
                materials = skinnedRenderer.sharedMaterials,

                bones = bones,

                rootBoneMatrix = rootBoneBindpose,
                rootbone = (ushort)bonesNames.FindIndexOf(skinnedRenderer.rootBone.name.ToFixedString32()),
            };

            var kandraRenderer = gameObject.AddComponent<KandraRenderer>();
            kandraRenderer.rendererData = registerData;

            DestroyImmediate(skinnedRenderer);
            usedBones.Dispose();

            gameObject.SetActive(true);
            EditorUtility.SetDirty(gameObject);

            bool FilterBones(Transform _, int index)
            {
                return usedBones[(uint)index];
            }
        }

        KandraMesh CreateKandraMesh(Mesh mesh, UnsafeBitmask usedBones, UnsafeArray<int> bonesMap, int rootBone, out float3x4 rootBoneBindpose)
        {
            SaveMeshData(mesh, usedBones, bonesMap, rootBone, out var vertexCount, out var bindposesCount, out var blendshapesNames, out rootBoneBindpose);

            SaveIndicesData(mesh, out var indicesCount, out var submeshes);

            var bounds = mesh.bounds;
            var localBoundingSphere = new float4(bounds.center, bounds.extents.magnitude);

            var kandraMesh = ScriptableObject.CreateInstance<KandraMesh>();
            kandraMesh.meshLocalBounds = mesh.bounds;
            kandraMesh.localBoundingSphere = localBoundingSphere;
            kandraMesh.submeshes = submeshes;
            kandraMesh.blendshapesNames = blendshapesNames;

            kandraMesh.vertexCount = (ushort)vertexCount;
            kandraMesh.indicesCount = (ushort)indicesCount;
            kandraMesh.bindposesCount = (ushort)bindposesCount;

            SaveKandraAsset(mesh, kandraMesh);

            return kandraMesh;
        }

        private unsafe void SaveMeshData(Mesh mesh, UnsafeBitmask usedBones, UnsafeArray<int> bonesMap, int rootBone,
            out int vertexCount, out int bindposesCount, out string[] blendshapesNames, out float3x4 rootBoneBindpose)
        {
            vertexCount = mesh.vertexCount;

            var localVertices = mesh.vertices;
            var localNormals = mesh.normals;
            var localNormalsPtr = (float3*)UnsafeUtility.PinGCArrayAndGetDataAddress(localNormals, out var localNormalsHandle);
            var localTangents = mesh.tangents;
            var localTangentsPtr = (float4*)UnsafeUtility.PinGCArrayAndGetDataAddress(localTangents, out var localTangentsHandle);
            var localUvs = mesh.uv;
            var sourceBoneWeights = mesh.boneWeights;

            var vertices = new UnsafeArray<CompressedVertex>((uint)vertexCount, Allocator.Temp);
            var additionalVertexData = new UnsafeArray<KandraMesh.AdditionalVertexData>((uint)vertexCount, Allocator.Temp);
            var boneWeights = new UnsafeArray<KandraMesh.BoneWeights>((uint)vertexCount, Allocator.Temp);
            for (var i = 0u; i < mesh.vertexCount; i++)
            {
                var vertex = new Vertex(
                    localVertices[i],
                    localNormals[i],
                    new float3(localTangents[i].x, localTangents[i].y, localTangents[i].z)
                );

                var compressedVertex = new CompressedVertex(vertex);

                vertices[i] = compressedVertex;
                additionalVertexData[i] = EncodeUv(localUvs[i]);
                boneWeights[i] = new KandraMesh.BoneWeights(Remap(sourceBoneWeights[i]));
            }

            var originalBindposes = mesh.bindposes;
            var filteredBindposes = originalBindposes.Where(FilterBindposes).ToArray();
            bindposesCount = filteredBindposes.Length;
            var bindposes = new UnsafeArray<float3x4>((uint)filteredBindposes.Length, Allocator.Temp);
            for (var i = 0u; i < filteredBindposes.Length; i++)
            {
                bindposes[i] = PackOrthonormalMatrix(filteredBindposes[i]);
            }
            rootBoneBindpose = PackOrthonormalMatrix(originalBindposes[rootBone]);

            blendshapesNames = new string[mesh.blendShapeCount];

            var blendshapesDeltaVertices = new Vector3[vertexCount];
            var blendshapesDeltaNormals = new Vector3[vertexCount];
            var blendshapesDeltaTangents = new Vector3[vertexCount];

            var blendshapesDeltas = new UnsafeArray<UnsafeArray<KandraMesh.BlendshapeDeltas>>((uint)mesh.blendShapeCount, Allocator.Temp);
            var validBlendshapesCount = 0u;
            for (var i = 0; i < mesh.blendShapeCount; ++i)
            {
                var blendshapeName = mesh.GetBlendShapeName(i);
                blendshapesNames[i] = blendshapeName;

                mesh.GetBlendShapeFrameVertices(i, 0, blendshapesDeltaVertices, blendshapesDeltaNormals, blendshapesDeltaTangents);
                var verticesPtr = (float3*)UnsafeUtility.PinGCArrayAndGetDataAddress(blendshapesDeltaVertices, out var verticesHandle);
                var normalsPtr = (float3*)UnsafeUtility.PinGCArrayAndGetDataAddress(blendshapesDeltaNormals, out var normalsHandle);
                var tangentsPtr = (float3*)UnsafeUtility.PinGCArrayAndGetDataAddress(blendshapesDeltaTangents, out var tangentsHandle);

                bool isValidBlendshape = !IsBlendshapeEmpty(verticesPtr, normalsPtr, tangentsPtr, vertexCount);

                if (isValidBlendshape) {
                    var blendshapeDeltas = new UnsafeArray<KandraMesh.BlendshapeDeltas>((uint)vertexCount, Allocator.Temp);
                    FillBlendshapeDeltas(vertexCount, ref blendshapeDeltas, verticesPtr, normalsPtr, tangentsPtr, localNormalsPtr, localTangentsPtr);
                    blendshapesDeltas[validBlendshapesCount++] = blendshapeDeltas;
                } else {
                    blendshapesNames[i] = null;
                }

                UnsafeUtility.ReleaseGCObject(verticesHandle);
                UnsafeUtility.ReleaseGCObject(normalsHandle);
                UnsafeUtility.ReleaseGCObject(tangentsHandle);
            }

            var validBlendshapesNames = new string[validBlendshapesCount];
            var validNameIndex = 0;
            for (var i = 0u; i < blendshapesNames.Length; ++i) {
                if (blendshapesNames[i] != null) {
                    validBlendshapesNames[validNameIndex++] = blendshapesNames[i];
                }
            }
            blendshapesNames = validBlendshapesNames;

            UnsafeUtility.ReleaseGCObject(localNormalsHandle);
            UnsafeUtility.ReleaseGCObject(localTangentsHandle);

            // Save all data
            var dataPath = StreamingManager.MeshDataPath(mesh);
            var dataFile = new FileStream(dataPath, FileMode.Create);

            var dataToWrite = new ReadOnlySpan<byte>(vertices.Ptr, (int)(vertices.Length * sizeof(CompressedVertex)));
            dataFile.Write(dataToWrite);
            dataToWrite = new ReadOnlySpan<byte>(additionalVertexData.Ptr, (int)(additionalVertexData.Length * sizeof(KandraMesh.AdditionalVertexData)));
            dataFile.Write(dataToWrite);
            dataToWrite = new ReadOnlySpan<byte>(boneWeights.Ptr, (int)(boneWeights.Length * sizeof(KandraMesh.BoneWeights)));
            dataFile.Write(dataToWrite);
            dataToWrite = new ReadOnlySpan<byte>(bindposes.Ptr, (int)(bindposes.Length * sizeof(float3x4)));
            dataFile.Write(dataToWrite);
            for (var i = 0u; i < validBlendshapesCount; ++i)
            {
                dataToWrite = new ReadOnlySpan<byte>(blendshapesDeltas[i].Ptr, (int)(blendshapesDeltas[i].Length * sizeof(KandraMesh.BlendshapeDeltas)));
                dataFile.Write(dataToWrite);
                blendshapesDeltas[i].Dispose();
            }

            dataFile.Flush();
            dataFile.Dispose();

            vertices.Dispose();
            additionalVertexData.Dispose();
            boneWeights.Dispose();
            bindposes.Dispose();
            blendshapesDeltas.Dispose();

            BoneWeight Remap(BoneWeight boneWeight)
            {
                return new BoneWeight
                {
                    boneIndex0 = bonesMap[(uint)boneWeight.boneIndex0],
                    boneIndex1 = bonesMap[(uint)boneWeight.boneIndex1],
                    boneIndex2 = bonesMap[(uint)boneWeight.boneIndex2],
                    boneIndex3 = bonesMap[(uint)boneWeight.boneIndex3],
                    weight0 = boneWeight.weight0,
                    weight1 = boneWeight.weight1,
                    weight2 = boneWeight.weight2,
                    weight3 = boneWeight.weight3,
                };
            }

            bool FilterBindposes(Matrix4x4 _, int index)
            {
                return usedBones[(uint)index];
            }
        }

        private static unsafe void SaveIndicesData(Mesh mesh, out int indicesCount, out KandraMesh.SubmeshData[] submeshes)
        {
            var originalDataArray = MeshUtility.AcquireReadOnlyMeshData(mesh);
            var originalData = originalDataArray[0];
            var originalIndices = originalData.GetIndexData<ushort>();
            indicesCount = originalIndices.Length;
            var submeshCount = originalData.subMeshCount;
            submeshes = new KandraMesh.SubmeshData[submeshCount];
            for (var i = 0; i < submeshCount; ++i)
            {
                var submeshDesc = originalData.GetSubMesh(i);
                submeshes[i] = new KandraMesh.SubmeshData
                {
                    indexStart = (ushort)submeshDesc.indexStart,
                    indexCount = (ushort)submeshDesc.indexCount,
                };
            }

            var dataPath = StreamingManager.IndicesDataPath(mesh);
            var dataFile = new FileStream(dataPath, FileMode.Create);
            var dataToWrite = new ReadOnlySpan<byte>(originalIndices.GetUnsafeReadOnlyPtr(), indicesCount * sizeof(ushort));
            dataFile.Write(dataToWrite);
            dataFile.Flush();
            dataFile.Dispose();

            originalDataArray.Dispose();
        }

        private static void SaveKandraAsset(Mesh mesh, KandraMesh kandraMesh)
        {
            var originalMeshPath = AssetDatabase.GetAssetPath(mesh);
            var meshName = StreamingManager.KandraMeshName(mesh);
            var newKandraMeshPath = Path.Combine(Path.GetDirectoryName(originalMeshPath), meshName + ".asset");
            AssetDatabase.CreateAsset(kandraMesh, newKandraMeshPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(newKandraMeshPath);
        }

        [BurstCompile]
        private static unsafe bool IsBlendshapeEmpty(in float3* vertices, in float3* normals, in float3* tangents, int vertexCount) {
            for (var i = 0; i < vertexCount; ++i) {
                if (math.lengthsq(vertices[i]) > 0.001f) {
                    return false;
                }
            }

            return true;
        }

        [BurstCompile]
        private static unsafe void FillBlendshapeDeltas(int vertexCount, ref UnsafeArray<KandraMesh.BlendshapeDeltas> blendshapeDeltas,
            float3* verticesPtr, float3* normalsPtr, float3* tangentsPtr, float3* localNormalsPtr, float4* localTangentsPtr)
        {
            for (var j = 0u; j < vertexCount; ++j)
            {
                blendshapeDeltas[j] = new KandraMesh.BlendshapeDeltas(
                    verticesPtr[j],
                    normalsPtr[j],
                    tangentsPtr[j],
                    localNormalsPtr[j],
                    localTangentsPtr[j]
                );
            }
        }

        void CollectUsedBones(Mesh mesh, ref UnsafeBitmask usedBones)
        {
            var meshBoneWeights = mesh.boneWeights;
            for (var i = 0; i < meshBoneWeights.Length; i++)
            {
                usedBones.Up((uint)meshBoneWeights[i].boneIndex0);
                usedBones.Up((uint)meshBoneWeights[i].boneIndex1);
                usedBones.Up((uint)meshBoneWeights[i].boneIndex2);
                usedBones.Up((uint)meshBoneWeights[i].boneIndex3);
            }
        }

        UnsafeArray<int> CreateBonesMap(in UnsafeBitmask usedBones)
        {
            var bonesMap = new UnsafeArray<int>(usedBones.ElementsLength, Allocator.Temp);
            var bonesMapIndex = 0;
            for (var i = 0u; i < usedBones.ElementsLength; i++)
            {
                bonesMap[i] = bonesMapIndex;
                if (usedBones[i])
                {
                    bonesMapIndex++;
                }
            }

            return bonesMap;
        }

        KandraMesh.AdditionalVertexData EncodeUv(Vector2 uv)
        {
            var halfUv = math.f32tof16(uv);
            return new KandraMesh.AdditionalVertexData
            {
                uv = halfUv.x | (halfUv.y << 16),
            };
        }

        float3x4 PackOrthonormalMatrix(Matrix4x4 input)
        {
            var matrix = (float4x4)input;
            return new float3x4(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz, matrix.c3.xyz);
        }
    }
}