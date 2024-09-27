using System.Collections.Generic;
using KVD.Puppeteer;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;

namespace KandraRenderer {
    public class KandraRig : MonoBehaviour {
        static readonly ProfilerMarker MargeMarker = new ProfilerMarker("KandraRig.Merge");
        static readonly ProfilerMarker RemoveMergeMarker = new ProfilerMarker("KandraRig.RemoveMerge");
        static readonly ProfilerMarker CopyBoneMarker = new ProfilerMarker("KandraRig.CopyBone");

        public VirtualBones virtualBones;

        readonly List<KandraRenderer> _renderers = new List<KandraRenderer>();
        UnsafeList<int> _addedBonesRefCount;

        void Awake() {
            _addedBonesRefCount = new UnsafeList<int>(8, Allocator.Persistent);
        }

        void OnDestroy() {
            _addedBonesRefCount.Dispose();
        }

        public void AddRegisteredRenderer(KandraRenderer renderer) {
            _renderers.Add(renderer);
        }

        public void RemoveRegisteredRenderer(KandraRenderer renderer) {
            _renderers.Remove(renderer);
        }

        public unsafe void Merge(KandraRig otherRig, ushort[] otherRendererBones, ref ushort otherRendererRootbone) {
            // MargeMarker.Begin();
            //
            // var allNewBonesCount = (int)(boneNames.Length * 1.2f);
            // var bonesCatalog = new UnsafeHashMap<FixedString32Bytes, ushort>((int)(allNewBonesCount*1.2f), Allocator.Temp);
            // for (ushort i = 0; i < bones.Length; i++) {
            //     bonesCatalog.TryAdd(boneNames[i], i);
            // }
            //
            // var allBones = new List<Transform>(allNewBonesCount);
            // allBones.AddRange(bones);
            // var allBoneParents = new UnsafeList<ushort>(allNewBonesCount, Allocator.Temp);
            // fixed (ushort* oldBoneParentsPtr = &boneParents[0]) {
            //     allBoneParents.AddRange(oldBoneParentsPtr, boneParents.Length);
            // }
            // var allBoneNames = new UnsafeList<FixedString32Bytes>(allNewBonesCount, Allocator.Temp);
            // fixed (FixedString32Bytes* oldBoneNamesPtr = &boneNames[0]) {
            //     allBoneNames.AddRange(oldBoneNamesPtr, boneNames.Length);
            // }
            //
            // var changed = false;
            // for (ushort i = 0; i < otherRig.bones.Length; i++) {
            //     var boneName = otherRig.boneNames[i];
            //     if (!bonesCatalog.TryGetValue(boneName, out var boneIndex)) {
            //         var (bone, parentIndex) = CopyBone(otherRig, i, allBones, bonesCatalog);
            //         allBones.Add(bone);
            //         allBoneParents.Add(parentIndex);
            //         allBoneNames.Add(boneName);
            //         changed = true;
            //         boneIndex = (ushort)(allBones.Count - 1);
            //         bonesCatalog.TryAdd(boneName, boneIndex);
            //     }
            //
            //     if(baseBoneCount <= boneIndex) {
            //         var index = boneIndex - baseBoneCount;
            //         if(_addedBonesRefCount.Length <= index) {
            //             _addedBonesRefCount.Add(1);
            //         } else {
            //             var refCount = _addedBonesRefCount[index];
            //             _addedBonesRefCount[index] = refCount + 1;
            //         }
            //     }
            // }
            //
            // bones = allBones.ToArray();
            //
            // boneNames = new FixedString32Bytes[allBoneNames.Length];
            // CopyNativeToArray(boneNames, allBoneNames);
            //
            // boneParents = new ushort[allBoneParents.Length];
            // CopyNativeToArray(boneParents, allBoneParents);
            //
            // allBoneParents.Dispose();
            // allBoneNames.Dispose();
            //
            // for (var i = 0; i < otherRendererBones.Length; i++) {
            //     var otherBoneIndex = otherRendererBones[i];
            //     var oldBoneName = otherRig.boneNames[otherBoneIndex];
            //     otherRendererBones[i] = bonesCatalog[oldBoneName];
            // }
            // { // Rootbone
            //     var boneName = otherRig.boneNames[otherRendererRootbone];
            //     otherRendererRootbone = bonesCatalog[boneName];
            // }
            //
            // if (changed) {
            //     KandraRendererManager.Instance.RigChanged(this, _renderers);
            // }
            //
            // bonesCatalog.Dispose();
            //
            // MargeMarker.End();
        }

        public void RemoveMerged(ushort[] otherBones) {
            // RemoveMergeMarker.Begin();
            // if (!_addedBonesRefCount.IsCreated) {
            //     return;
            // }
            //
            // var toDelete = new UnsafeList<ushort>(4, Allocator.Temp);
            // for (var i = 0; i < otherBones.Length; ++i) {
            //     if(otherBones[i] >= baseBoneCount) {
            //         var index = otherBones[i] - baseBoneCount;
            //         var refCount = _addedBonesRefCount[index];
            //         if (refCount == 1) {
            //             toDelete.Add((ushort)(index+baseBoneCount));
            //         }
            //         _addedBonesRefCount[index] = refCount - 1;
            //     }
            // }
            //
            // if(toDelete.Length == 0) {
            //     toDelete.Dispose();
            //     return;
            // }
            //
            // toDelete.Sort();
            //
            // var oldBones = bones;
            // var newBones = new Transform[bones.Length - toDelete.Length];
            // var newBoneParents = new ushort[bones.Length - toDelete.Length];
            // var newBoneNames = new FixedString32Bytes[boneNames.Length - toDelete.Length];
            //
            // var startSourceIndex = 0;
            // var startTargetIndex = 0;
            // for (var i = 0; i < toDelete.Length; ++i) {
            //     var indexToSkip = toDelete[i];
            //     var count = indexToSkip - startSourceIndex;
            //     Array.Copy(bones, startSourceIndex, newBones, startTargetIndex, count);
            //     Array.Copy(boneParents, startSourceIndex, newBoneParents, startTargetIndex, count);
            //     Array.Copy(boneNames, startSourceIndex, newBoneNames, startTargetIndex, count);
            //     startSourceIndex = indexToSkip + 1;
            //     startTargetIndex += count;
            // }
            //
            // {
            //     var count = oldBones.Length - startSourceIndex;
            //     Array.Copy(bones, startSourceIndex, newBones, startTargetIndex, count);
            //     Array.Copy(boneParents, startSourceIndex, newBoneParents, startTargetIndex, count);
            //     Array.Copy(boneNames, startSourceIndex, newBoneNames, startTargetIndex, count);
            // }
            //
            // bones = newBones;
            // boneParents = newBoneParents;
            // boneNames = newBoneNames;
            //
            // for (int i = toDelete.Length - 1; i >= 0; i--) {
            //     var bone = oldBones[toDelete[i]];
            //     Destroy(bone.gameObject);
            //     _addedBonesRefCount.RemoveAt(toDelete[i] - baseBoneCount);
            // }
            //
            // for (int i = 0; i < _renderers.Count; i++) {
            //     var renderer = _renderers[i];
            //     var rendererBones = renderer.rendererData.bones;
            //     for (int j = 0; j < rendererBones.Length; j++) {
            //         if (rendererBones[i] < baseBoneCount) {
            //             continue;
            //         }
            //         var indexModifier = CalculateIndexModifier(toDelete, rendererBones[j]);
            //         rendererBones[j] -= indexModifier;
            //     }
            // }
            //
            // toDelete.Dispose();
            //
            // RemoveMergeMarker.End();
        }

//         (Transform, ushort) CopyBone(KandraRig otherRig, ushort otherRigBoneIndex, List<Transform> allBones, in UnsafeHashMap<FixedString32Bytes, ushort> bonesCatalog)
//         {
//             CopyBoneMarker.Begin();
//
//             var parentIndex = otherRig.boneParents[otherRigBoneIndex];
//
//             Transform parent = null;
//             ushort targetParentIndex = ushort.MaxValue;
//             if (parentIndex == ushort.MaxValue) {
//                 parent = transform;
//                 targetParentIndex = ushort.MaxValue;
//             } else {
//                 var parentName = otherRig.boneNames[parentIndex];
//                 targetParentIndex = bonesCatalog[parentName];
//                 parent = allBones[targetParentIndex];
//             }
//
//             string boneName = "CopiedBone";
// #if UNITY_EDITOR
//             boneName = otherRig.boneNames[otherRigBoneIndex].ToString();
// #endif
//             var copiedBoneGo = new GameObject(boneName);
//             var copiedBone = copiedBoneGo.transform;
//             var sourceBone = otherRig.bones[otherRigBoneIndex];
//
//             copiedBoneGo.transform.SetParent(parent);
//             sourceBone.GetLocalPositionAndRotation(out var localPosition, out var localRotation);
//             copiedBone.SetLocalPositionAndRotation(localPosition, localRotation);
//             copiedBoneGo.transform.localScale = otherRig.bones[otherRigBoneIndex].localScale;
//
//             CopyBoneMarker.End();
//
//             return (copiedBone, targetParentIndex);
//         }
//
//         ushort CalculateIndexModifier(UnsafeList<ushort> toDelete, ushort rendererBone) {
//             ushort i = 0;
//             while (i < toDelete.Length && toDelete[i] < rendererBone) {
//                 ++i;
//             }
//
//             return i;
//         }
//
//         static unsafe void CopyNativeToArray<T>(T[] dest, UnsafeList<T> source) where T : unmanaged {
//             var gcHandle = GCHandle.Alloc(dest, GCHandleType.Pinned);
//             UnsafeUtility.MemCpy(gcHandle.AddrOfPinnedObject().ToPointer(), source.Ptr, source.Length * UnsafeUtility.SizeOf<T>());
//             gcHandle.Free();
//         }
    }
}