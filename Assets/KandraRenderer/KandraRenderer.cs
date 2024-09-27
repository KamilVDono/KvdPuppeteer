using System;
using KVD.Utils.DataStructures;
using KVD.Utils.Maths;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace KandraRenderer
{
    [ExecuteInEditMode]
    public class KandraRenderer : MonoBehaviour
    {
        public RendererData rendererData;

#if UNITY_EDITOR
        [field: NonSerialized] public bool EDITOR_Force_Uninitialized { get; set; }
#endif

        [field: NonSerialized] public int RenderingId { get; set; } = -1;
        public ushort BlendshapesCount => (ushort)rendererData.blendshapeWeights.Length;

        void OnEnable()
        {
            rendererData.blendshapeWeights = new UnsafeArray<float>((uint)rendererData.mesh.blendshapesNames.Length, Allocator.Persistent);
            rendererData.originalMesh = KandraRendererManager.Instance.MeshBroker.GetOriginalMesh(rendererData.mesh);
            RenderingId = KandraRendererManager.Instance.Register(this);
            rendererData.rig.AddRegisteredRenderer(this);
        }

        void OnDisable()
        {
            rendererData.rig.RemoveRegisteredRenderer(this);
#if UNITY_EDITOR
            // In editor lifetime is so fucked up that it is possible
            if (!EDITOR_Force_Uninitialized && KandraRendererManager.Instance != null)
#endif
            {
                KandraRendererManager.Instance.Unregister(RenderingId);
            }
        }

        void OnDestroy()
        {
            rendererData.rig.RemoveMerged(rendererData.bones);
        }

        public void SetBlendshapeWeight(uint blendshapeIndex, float weight)
        {
            rendererData.blendshapeWeights[blendshapeIndex] = weight;
        }

        public void UpdateCulledMesh(UnsafeArray<ushort> indices)
        {
            var oldMesh = rendererData.cullableMesh;

            rendererData.cullableMesh = KandraRendererManager.Instance.MeshBroker.CreateCullableMesh(rendererData.mesh, indices);
            UpdateRenderingMesh();

            if (oldMesh)
            {
                KandraRendererManager.Instance.MeshBroker.ReleaseCullableMesh(rendererData.mesh, oldMesh);
            }
        }

        public void ReleaseCullableMesh()
        {
            if (rendererData.cullableMesh)
            {
                var oldMesh = rendererData.cullableMesh;
                rendererData.cullableMesh = null;
                UpdateRenderingMesh();

                KandraRendererManager.Instance.MeshBroker.ReleaseCullableMesh(rendererData.mesh, oldMesh);
            }
        }

        public static void RedirectToRig(KandraRenderer source, KandraRenderer copy, KandraRig rig)
        {
            copy.rendererData = source.rendererData.Copy();
            rig.Merge(source.rendererData.rig, copy.rendererData.bones, ref copy.rendererData.rootbone);
            copy.rendererData.rig = rig;
        }

        void UpdateRenderingMesh()
        {
            if (RenderingId != -1)
            {
                KandraRendererManager.Instance.UpdateRenderingMesh(RenderingId, rendererData.RenderingMesh);
            }
        }

        void OnDrawGizmosSelected() {
            if (RenderingId == -1) {
                return;
            }
            KandraRendererManager.Instance.BoundsAndRootBone(RenderingId, out var worldBoundingSphere, out var rootBoneMatrix);
            var bounds = rendererData.mesh.meshLocalBounds;
            var oldMatrix = Gizmos.matrix;
            Gizmos.color = KandraRendererManager.Instance.IsVisible(RenderingId) ? Color.green : Color.red;
            Gizmos.DrawWireSphere(worldBoundingSphere.xyz, worldBoundingSphere.w);
            Gizmos.matrix = rootBoneMatrix.toFloat4x4();
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.matrix = oldMatrix;
        }

        [Serializable]
        public struct RendererData
        {
            public KandraRig rig;

#if UNITY_EDITOR
            public Mesh sourceMesh;
#endif
            public KandraMesh mesh;
            [NonSerialized] public Mesh originalMesh;
            [NonSerialized] public Mesh cullableMesh;
            public Material[] materials;

            public ushort[] bones;
            public ushort rootbone;
            public float3x4 rootBoneMatrix;

            public UnsafeArray<float> blendshapeWeights;

            public Mesh RenderingMesh => cullableMesh ? cullableMesh : originalMesh;

            public RendererData Copy()
            {
                var bonesCopy = new ushort[bones.Length];
                Array.Copy(bones, bonesCopy, bones.Length);
                return new RendererData
                {
                    rig = rig,
                    mesh = mesh,
                    materials = materials,
                    bones = bonesCopy,
                    rootbone = rootbone,
                    rootBoneMatrix = rootBoneMatrix
                };
            }
        }
    }
}
