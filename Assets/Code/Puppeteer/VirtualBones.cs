using System;
using KVD.Puppeteer.Data;
using KVD.Puppeteer.Data.Authoring;
using KVD.Puppeteer.Managers;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace KVD.Puppeteer
{
	[ExecuteInEditMode, BurstCompile]
	public class VirtualBones : MonoBehaviour
	{
		public SkeletonAsset skeletonAsset;

		[field: NonSerialized] public uint SkeletonIndex{ get; set; } = uint.MaxValue;

		public ref readonly Skeleton Skeleton => ref SkeletonsManager.Instance.Skeletons[SkeletonIndex];
		public ref readonly SharedSkeleton SharedSkeleton => ref SkeletonsManager.Instance.SharedSkeletons[SharedSkeletonIndex];
		public uint SharedSkeletonIndex => SkeletonsManager.Instance.SharedSkeletonIndices[SkeletonIndex];
		public bool IsValid => SkeletonIndex != uint.MaxValue && Skeleton.IsCreated;

		public void EnsureInitialized()
		{
			if (SkeletonIndex == uint.MaxValue)
			{
				Awake();
			}
		}

		void Awake()
		{
			if (!skeletonAsset)
			{
				enabled = false;
				return;
			}
			if (SkeletonIndex != uint.MaxValue)
			{
				return;
			}
			CreateSkeleton();
		}

		void CreateSkeleton()
		{
			SkeletonIndex = SkeletonsManager.Instance.RegisterSkeleton(skeletonAsset);
			WorldOffsetsManager.Instance.RegisterRoot(SkeletonIndex, transform);
		}

#if UNITY_EDITOR
		void OnEnable()
		{
			EnsureInitialized();
		}
#endif

		void OnDestroy()
		{
			if (SkeletonIndex == uint.MaxValue)
			{
				return;
			}

			WorldOffsetsManager.Instance.UnregisterBones(SkeletonIndex);
			SkeletonsManager.Instance.UnregisterSkeleton(SkeletonIndex);

			SkeletonIndex = uint.MaxValue;
		}

		void OnDrawGizmosSelected()
		{
			if (!IsValid)
			{
				return;
			}

			var identityPosition = new float4(0f, 0f, 0f, 1f);

			for (var i = 1; i < Skeleton.localBones.Length; i++)
			{
				var bone = Skeleton.localToWorlds[i];
				var bonePosition = math.mul(bone, identityPosition).xyz;

				var parentIndex = SharedSkeleton.parentIndices[i];
				var parentBone = Skeleton.localToWorlds[parentIndex];
				var parentPosition = math.mul(parentBone, identityPosition).xyz;

				Gizmos.color = Color.red;
				Gizmos.DrawLine(parentPosition, bonePosition);
			}
		}
	}
}
