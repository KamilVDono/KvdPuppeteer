using AclUnity;
using KVD.Puppeteer;
using KVD.Puppeteer.Managers;
using KVD.Utils.DataStructures;
using KVD.Utils.Maths;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace KandraRenderer {
    public unsafe class VisibilityCullingManager {
        const int MaxRenderers = KandraRendererManager.RenderersCapacity;

        UnsafeArray<float4> _localBoundingSpheres;
        UnsafeArray<float3x4> _bindposes;

        public UnsafeArray<RootTransform> rootTransforms;
        public UnsafeArray<float3x4> rootBones;
        public UnsafeArray<float> xs;
        public UnsafeArray<float> ys;
        public UnsafeArray<float> zs;
        public UnsafeArray<float> radii;

        public JobHandle collectCullingDataJobHandle;

        public VisibilityCullingManager() {
            _localBoundingSpheres = new UnsafeArray<float4>(MaxRenderers, Allocator.Persistent);
            _bindposes = new UnsafeArray<float3x4>(MaxRenderers, Allocator.Persistent);

            rootTransforms = new UnsafeArray<RootTransform>(MaxRenderers, Allocator.Persistent);
            rootBones = new UnsafeArray<float3x4>(MaxRenderers, Allocator.Persistent);
            xs = new UnsafeArray<float>(MaxRenderers, Allocator.Persistent);
            ys = new UnsafeArray<float>(MaxRenderers, Allocator.Persistent);
            zs = new UnsafeArray<float>(MaxRenderers, Allocator.Persistent);
            radii = new UnsafeArray<float>(MaxRenderers, Allocator.Persistent);
        }

        public void Dispose() {
            collectCullingDataJobHandle.Complete();
            _localBoundingSpheres.Dispose();
            _bindposes.Dispose();
            rootTransforms.Dispose();
            rootBones.Dispose();
            xs.Dispose();
            ys.Dispose();
            zs.Dispose();
            radii.Dispose();
        }

        public void Register(uint slot, in float3x4 bindPose, ref float3x4 rootBonePtr, float4 localBoundingSphere) {
            _localBoundingSpheres[slot] = localBoundingSphere;
            _bindposes[slot] = bindPose;

            rootTransforms[slot] = new RootTransform((float3x4*)UnsafeUtility.AddressOf(ref rootBonePtr));

            var rootBoneMatrix = mathUtil.mul(rootTransforms[slot].LocalToWorldMatrix, _bindposes[slot]);
            rootBones[slot] = rootBoneMatrix;
            var center = localBoundingSphere.xyz;
            var radius = localBoundingSphere.w;
            var worldCenter = math.mul(rootBoneMatrix, new float4(center, 1)).xyz;
            var worldRadius = math.cmax(math.mul(rootBoneMatrix, new float4(radius, radius, radius, 0)).xyz);
            xs[slot] = worldCenter.x;
            ys[slot] = worldCenter.y;
            zs[slot] = worldCenter.z;
            radii[slot] = worldRadius;
        }

        public void Unregister(uint slot) {
            rootTransforms[(int)slot] = default;
        }

        public void CollectCullingData(UnsafeBitmask takenSlots) {
            collectCullingDataJobHandle.Complete();
            var dependency = PuppeteerManager.Instance.AnimationsJobHandle;
            collectCullingDataJobHandle = new CullingDataJob
            {
                rootTransforms = rootTransforms,
                localBoundingSpheres = _localBoundingSpheres,
                bindposes = _bindposes,
                takenSlots = takenSlots,

                boneMatrices = rootBones,
                xs = xs,
                ys = ys,
                zs = zs,
                radii = radii
            }.Schedule(takenSlots.LastOne()+1, 32, dependency);
        }

        public readonly unsafe struct RootTransform
        {
            public readonly float3x4* transfrom;

            public float3x4 LocalToWorldMatrix => *transfrom;

            public RootTransform(float3x4* transfrom)
            {
                this.transfrom = transfrom;
            }
        }

        [BurstCompile]
        struct CullingDataJob : IJobParallelFor {
            [ReadOnly] public UnsafeArray<RootTransform> rootTransforms;
            [ReadOnly] public UnsafeArray<float4> localBoundingSpheres;
            [ReadOnly] public UnsafeArray<float3x4> bindposes;
            [ReadOnly] public UnsafeBitmask takenSlots;

            [WriteOnly] public UnsafeArray<float3x4> boneMatrices;
            [WriteOnly] public UnsafeArray<float> xs;
            [WriteOnly] public UnsafeArray<float> ys;
            [WriteOnly] public UnsafeArray<float> zs;
            [WriteOnly] public UnsafeArray<float> radii;

            public void Execute(int index) {
                var uIndex = (uint)index;
                if (!takenSlots[uIndex]) {
                    return;
                }

                var rootBoneMatrix = rootTransforms[uIndex].LocalToWorldMatrix;
                var boneMatrix = mathUtil.mul(rootBoneMatrix, bindposes[uIndex]);
                var center = localBoundingSpheres[uIndex].xyz;
                var radius = localBoundingSpheres[uIndex].w;

                var worldCenter = math.mul(boneMatrix, new float4(center, 1)).xyz;
                var worldRadius = math.length(math.mul(boneMatrix, new float4(radius, 0, 0, 0)).xyz);

                boneMatrices[uIndex] = boneMatrix;
                xs[uIndex] = worldCenter.x;
                ys[uIndex] = worldCenter.y;
                zs[uIndex] = worldCenter.z;
                radii[uIndex] = worldRadius;
            }
        }
    }
}