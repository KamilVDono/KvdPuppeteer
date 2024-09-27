using AclUnity;
using KVD.Puppeteer.Data;
using KVD.Puppeteer.Data.Authoring;
using KVD.Utils.DataStructures;
using KVD.Utils.Maths;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace KVD.Puppeteer.Managers
{
	public class SkeletonsManager
	{
		UnsafeHashMap<SerializableGuid, SkeletonData> _skeletonsData;
		OccupiedArray<SharedSkeleton> _sharedSkeletons;
		UnsafeArray<ushort> _relaxPoseIndices;
		UnsafeArray<SerializableGuid> _guids;

		OccupiedArray<Skeleton> _skeletons;
		UnsafeArray<uint> _sharedSkeletonIndices;

		public static SkeletonsManager Instance { get; private set; }

		public ref readonly UnsafeArray<Skeleton> Skeletons => ref _skeletons.array;
		public ref readonly UnsafeArray<uint> SharedSkeletonIndices => ref _sharedSkeletonIndices;

		public ref readonly UnsafeArray<SharedSkeleton> SharedSkeletons => ref _sharedSkeletons.array;
		public ref readonly UnsafeArray<ushort> RelaxClipIndices => ref _relaxPoseIndices;

		public SkeletonsManager(int preAllocCount)
		{
			Instance = this;

			_skeletonsData = new UnsafeHashMap<SerializableGuid, SkeletonData>(preAllocCount, Allocator.Domain);
			_sharedSkeletons = new OccupiedArray<SharedSkeleton>((uint)preAllocCount, Allocator.Domain);
			_relaxPoseIndices = new UnsafeArray<ushort>((uint)preAllocCount, Allocator.Domain);
			_guids = new UnsafeArray<SerializableGuid>((uint)preAllocCount, Allocator.Domain);

			_skeletons = new OccupiedArray<Skeleton>((uint)preAllocCount, Allocator.Domain);
			_sharedSkeletonIndices = new UnsafeArray<uint>((uint)preAllocCount, Allocator.Domain);
		}

		public uint RegisterSkeleton(PuppeteerAsset<Skeleton> skeletonAsset)
		{
			if (!_skeletonsData.TryGetValue(skeletonAsset, out var data))
			{
				// TODO: Check for fail
				StreamingManager.Instance.LoadSkeleton(skeletonAsset, out var sharedSkeleton);
				var relaxPoseIndex = ClipsManager.Instance.RegisterClip(skeletonAsset);
				_sharedSkeletons.TryInsert(sharedSkeleton, out var sharedSkeletonSlot);
				_relaxPoseIndices[sharedSkeletonSlot] = relaxPoseIndex;
				_guids[sharedSkeletonSlot] = skeletonAsset;
				data = new SkeletonData
				{
					refCount = 0,
					sharedSkeletonIndex = sharedSkeletonSlot,
				};
			}
			data.refCount++;
			_skeletonsData[skeletonAsset] = data;

			// TODO: Check for fail
			CreateSkeleton(data.sharedSkeletonIndex, out var skeletonIndex);

			ClipsManager.Instance.ClipData[_relaxPoseIndices[data.sharedSkeletonIndex]].SampleFirst(_skeletons[skeletonIndex], 0f, 1f);

			return skeletonIndex;
		}

		public void UnregisterSkeleton(uint skeletonIndex)
		{
			var sharedSkeletonIndex = _sharedSkeletonIndices[skeletonIndex];
			_skeletons.Release(skeletonIndex);

			var skeletonGuid = _guids[sharedSkeletonIndex];
			var data = _skeletonsData[skeletonGuid];
			if (data.refCount == 1)
			{
				_sharedSkeletons.Release(sharedSkeletonIndex);
				_guids[sharedSkeletonIndex] = default;
				ClipsManager.Instance.UnregisterClip(_relaxPoseIndices[sharedSkeletonIndex]);
				_relaxPoseIndices[sharedSkeletonIndex] = default;
				_skeletonsData.Remove(skeletonGuid);
			}
			else
			{
				data.refCount--;
				_skeletonsData[skeletonGuid] = data;
			}
		}

		bool CreateSkeleton(uint sharedSkeletonIndex, out uint skeletonIndex)
		{
			ref readonly var sharedSkeleton = ref _sharedSkeletons[sharedSkeletonIndex];
			var skeleton = new Skeleton()
			{
				localBones = new UnsafeArray<Qvvs>(sharedSkeleton.BonesCount, Allocator.Persistent),
				localToWorlds = new UnsafeArray<float3x4>(sharedSkeleton.BonesCount, Allocator.Persistent),
			};
			if (!_skeletons.TryInsert(skeleton, out skeletonIndex))
			{
				return false;
			}

			_sharedSkeletonIndices[skeletonIndex] = sharedSkeletonIndex;
			return true;
		}

		public JobHandle RunSyncTransformsJob(JobHandle dependencies)
		{
			return new SyncTransformsJob
			{
				sharedSkeletons = _sharedSkeletons,
				skeletons = _skeletons,
				sharedSkeletonIndices = _sharedSkeletonIndices,
				worldOffsets = WorldOffsetsManager.Instance.WorldOffsets,
			}.ScheduleParallel((int)_skeletons.LastTakenCount, 16, dependencies);
		}

		[BurstCompile]
		struct SyncTransformsJob : IJobFor
		{
			[ReadOnly] public OccupiedArray<SharedSkeleton> sharedSkeletons;

			[ReadOnly] public OccupiedArray<Skeleton> skeletons;
			[ReadOnly] public UnsafeArray<uint> sharedSkeletonIndices;

			[ReadOnly] public UnsafeArray<float3x4> worldOffsets;

			public void Execute(int index)
			{
				var uSlot = (uint)index;
				if (!skeletons.IsOccupied(uSlot))
				{
					return;
				}

				ref readonly var skeleton = ref skeletons[uSlot];
				ref readonly var sharedSkeleton = ref sharedSkeletons[sharedSkeletonIndices[uSlot]];

				var subLocalBones = skeleton.localBones;
				var subParentIndices = sharedSkeleton.parentIndices;
				var outSubLocalToWorlds = skeleton.localToWorlds;

				ToOrthonormal(subLocalBones[0], out var rootMatrix);
				outSubLocalToWorlds[0] = mathUtil.mul(worldOffsets[index], rootMatrix);
				for (var i = 1; i < subLocalBones.Length; i++)
				{
					var parentIndex = subParentIndices[i];
					var parentLocalToWorld = outSubLocalToWorlds[parentIndex];
					ToOrthonormal(subLocalBones[i], out var localMatrix);
					outSubLocalToWorlds[i] = mathUtil.mul(parentLocalToWorld, localMatrix);
				}
			}

			static void ToOrthonormal(in Qvvs qvvs, out float3x4 orthonormal)
			{
				var r = math.float3x3(qvvs.rotation);
				orthonormal = new float3x4(
					r.c0 * qvvs.stretch.x,
					r.c1 * qvvs.stretch.y,
					r.c2 * qvvs.stretch.z,
					qvvs.position);
			}
		}

		struct SkeletonData
		{
			public int refCount;
			public uint sharedSkeletonIndex;
		}
	}
}
