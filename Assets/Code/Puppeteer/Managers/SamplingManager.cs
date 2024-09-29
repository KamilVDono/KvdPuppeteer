using KVD.Puppeteer.Data;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace KVD.Puppeteer.Managers
{
	public class SamplingManager
	{
		UnsafeBitmask _takenSlots;
		UnsafeList<uint> _bonesIndices;
		UnsafeList<UnsafeList<SamplingDatum>> _blendingDatas;
		UnsafeList<byte> _capacities;

		public static SamplingManager Instance { get; private set; }

		public SamplingManager(int preAllocCount)
		{
			Instance = this;

			_takenSlots = new UnsafeBitmask((uint)preAllocCount, Allocator.Domain);

			_bonesIndices = new UnsafeList<uint>(preAllocCount, Allocator.Domain);
			_bonesIndices.Length = preAllocCount;

			_blendingDatas = new UnsafeList<UnsafeList<SamplingDatum>>(preAllocCount, Allocator.Domain, NativeArrayOptions.ClearMemory);
			_blendingDatas.Length = preAllocCount;

			_capacities = new UnsafeList<byte>(preAllocCount, Allocator.Domain);
			_capacities.Length = preAllocCount;
		}

		public uint RegisterPuppet(VirtualBones virtualBones)
		{
			var slot = _takenSlots.FirstZero();
			if (slot == -1)
			{
				// TODO: Resize
				Debug.Log("Max slots reached");
				return uint.MaxValue;
			}
			var uSlot = (uint)slot;
			_takenSlots.Up(uSlot);

			_bonesIndices[slot] = virtualBones.SkeletonIndex;
			var blendingData = new UnsafeList<SamplingDatum>(2, Allocator.Persistent);
			_capacities[slot] = 0;

			_blendingDatas[slot] = blendingData;

			return uSlot;
		}

		public void UnregisterPuppet(uint slot)
		{
			_takenSlots.Down(slot);
			ref var blendingData = ref _blendingDatas.ElementAt((int)slot);
			blendingData.Dispose();
			blendingData = default;
		}

		public UnsafeArray<SamplingDatum> GetClipsBlends(uint animator)
		{
			return _blendingDatas[(int)animator].AsUnsafeArray();
		}

		public ClipsWriter GetClipsWriter()
		{
			var data = _blendingDatas.AsUnsafeArray();
			var clearJob = new ClearSamplingDataJob
			{
				blendingDatas = data
			}.Schedule(_takenSlots.LastOne()+1, default);
			return new ClipsWriter(clearJob, data);
		}

		public JobHandle RunAnimationsSampling(JobHandle dependencies)
		{
			var maxIndex = _takenSlots.LastOne()+1;
			return new SamplingJob
			{
				clips = ClipsManager.Instance.ClipData,

				blendingDatas = _blendingDatas.AsUnsafeArray(),
				boneIndices = _bonesIndices.AsUnsafeArray(),
				skeletons = SkeletonsManager.Instance.Skeletons,
			}.ScheduleParallel(maxIndex, 16, dependencies);
		}

		public readonly struct ClipsWriter
		{
			public readonly JobHandle dependency;
			readonly UnsafeArray<UnsafeList<SamplingDatum>.ParallelWriter> _blendingDatas;

			internal ClipsWriter(JobHandle dependency, UnsafeArray<UnsafeList<SamplingDatum>> blendingDatas)
			{
				this.dependency = dependency;
				_blendingDatas = new UnsafeArray<UnsafeList<SamplingDatum>.ParallelWriter>(blendingDatas.Length, Allocator.TempJob);
				for (var i = 0u; i < blendingDatas.Length; i++)
				{
					_blendingDatas[i] = blendingDatas[i].AsParallelWriter();
				}
			}

			public unsafe void AddClip(uint puppet, ushort clipIndex, float blend, float time)
			{
				ref var blendingData = ref _blendingDatas.Ptr[puppet];
				blendingData.AddNoResize(new SamplingDatum
				{
					clipIndex = clipIndex,
					blend = blend,
					time = time
				});
			}

			public JobHandle Dispose(JobHandle dependencies)
			{
				return _blendingDatas.Dispose(dependencies);
			}
		}

		[BurstCompile]
		struct SamplingJob : IJobFor
		{
			public UnsafeArray<AnimationClipData> clips;

			public UnsafeArray<UnsafeList<SamplingDatum>> blendingDatas;

			public UnsafeArray<uint> boneIndices;
			public UnsafeArray<Skeleton> skeletons;

			public void Execute(int index)
			{
				var blendingData = blendingDatas[index];
				if (blendingData.Length == 0)
				{
					return;
				}

				var bonesIndex = boneIndices[index];
				ref readonly var skeleton = ref skeletons[bonesIndex];

				{
					var datum = blendingData[0];
					var clip = clips[datum.clipIndex];
					var clipTime = clip.LoopToClipTime(datum.time);
					clip.SampleFirst(skeleton, clipTime, datum.blend);
				}

				for (var i = 1; i < blendingData.Length; i++)
				{
					var datum = blendingData[i];
					var clip = clips[datum.clipIndex];
					var clipTime = clip.LoopToClipTime(datum.time);

					clip.SampleAdd(skeleton, clipTime, datum.blend);
				}
			}
		}

		[BurstCompile]
		struct ClearSamplingDataJob : IJobFor
		{
			public UnsafeArray<UnsafeList<SamplingDatum>> blendingDatas;

			public void Execute(int index)
			{
				blendingDatas[index].Length = 0;
			}
		}

		public struct SamplingDatum
		{
			public ushort clipIndex;
			public float blend;
			public float time;
		}

		public void EnsureCapacity(uint puppet, byte addedClips)
		{
			ref var capacity = ref _capacities.ElementAt((int)puppet);
			capacity += addedClips;
			ref var blendingData = ref _blendingDatas.ElementAt((int)puppet);
			if (blendingData.Capacity < capacity)
			{
				blendingData.SetCapacity(_capacities[(int)puppet]);
			}
		}
	}
}
