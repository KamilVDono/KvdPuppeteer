using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using KVD.Utils.Maths;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace KVD.Puppeteer.Managers
{
	public class WorldOffsetsManager
	{
		TransformAccessArray _rootsAccessArray;
		UnsafeList<float3x4> _worldOffsets;

		public static WorldOffsetsManager Instance { get; private set; }

		public UnsafeArray<float3x4> WorldOffsets => _worldOffsets.AsUnsafeArray();

		public unsafe WorldOffsetsManager(int preAllocCount)
		{
			Instance = this;

			_rootsAccessArray = new TransformAccessArray(preAllocCount);

			_worldOffsets = new UnsafeList<float3x4>(preAllocCount, Allocator.Domain);
			_worldOffsets.Length = preAllocCount;
			var identity = float4x4.identity.orthonormal();
			UnsafeUtils.Fill(_worldOffsets.Ptr, identity, preAllocCount);
		}

		public void RegisterRoot(uint slot, Transform root)
		{
			if (_rootsAccessArray.length <= slot)
			{
				_rootsAccessArray.Add(root);
			}
			else
			{
				_rootsAccessArray[(int)slot] = root;
			}
		}

		public void UnregisterBones(uint slot)
		{
			_rootsAccessArray[(int)slot] = null;
			_worldOffsets[(int)slot] = float4x4.identity.orthonormal();
		}

		public JobHandle CollectWorldOffsets(JobHandle dependencies)
		{
			return new WriteOffsets
			{
				outOffsets = _worldOffsets.AsUnsafeArray()
			}.ScheduleReadOnly(_rootsAccessArray, 64, dependencies);
		}

		[BurstCompile]
		struct WriteOffsets : IJobParallelForTransform
		{
			[WriteOnly] public UnsafeArray<float3x4> outOffsets;

			public void Execute(int index, TransformAccess transform)
			{
				if (!transform.isValid)
				{
					return;
				}
				outOffsets[index] = ((float4x4)transform.localToWorldMatrix).orthonormal();
			}
		}
	}
}
