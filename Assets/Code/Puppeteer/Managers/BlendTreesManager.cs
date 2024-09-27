using KVD.Puppeteer.Data;
using KVD.Utils.DataStructures;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace KVD.Puppeteer.Managers
{
	public class BlendTreesManager
	{
		UnsafeArray<BlendTreeData> _blendTrees;
		UnsafeArray<float2> _parameters;
		UnsafeBitmask _takenBlendTrees;

		public static BlendTreesManager Instance { get; private set; }
		public UnsafeArray<BlendTreeData> BlendTrees => _blendTrees;

		public BlendTreesManager(int preAllocCount)
		{
			Instance = this;

			_blendTrees = new UnsafeArray<BlendTreeData>((uint)preAllocCount, Allocator.Domain);
			_takenBlendTrees = new UnsafeBitmask((uint)preAllocCount, Allocator.Domain);
			_parameters = new UnsafeArray<float2>((uint)preAllocCount, Allocator.Domain);
		}

		public ushort AddBlendTree(in BlendTreeData blendTreeData)
		{
			var slot = _takenBlendTrees.FirstZero();
			if (slot == -1)
			{
				return unchecked((ushort)-1);
			}
			var uSlot = (ushort)slot;
			_takenBlendTrees.Up(uSlot);

			_blendTrees[uSlot] = blendTreeData;

			_parameters[uSlot] = new float2(0, 0);

			EvaluateBlendTree(ref _blendTrees[uSlot], _parameters[uSlot]);

			return uSlot;
		}

		public void RemoveBlendTree(ushort blendTreeId)
		{
			ref var blendTree = ref _blendTrees[blendTreeId];
			for (var i = 0; i < blendTree.clips.Length; i++)
			{
				ClipsManager.Instance.UnregisterClip(blendTree.clips[i]);
			}

			blendTree.Dispose();

			_takenBlendTrees.Down(blendTreeId);
		}

		public void UpdateParameters(ushort blendTreeId, float2 parameter)
		{
			_parameters[blendTreeId] = parameter;
		}

		public JobHandle RunBlendTrees(JobHandle dependencies)
		{
			return new EvaluateTreesJob
			{
				blendTrees = _blendTrees,
				parameters = _parameters,
				takenBlendTrees = _takenBlendTrees
			}.Schedule(_takenBlendTrees.LastOne()+1, dependencies);
		}

		static void EvaluateBlendTree(ref BlendTreeData blendTree, in float2 parameter)
		{
			if (blendTree.type == BlendTreeData.Type.Space1D)
			{
				Evaluate1DBlendTree(ref blendTree, parameter);
			}
			else
			{
				blendTree.blends[0] = 1;
				for (var i = 0; i < blendTree.blends.Length; i++)
				{
					blendTree.blends[i] = 0;
				}
			}
		}

		static void Evaluate1DBlendTree(ref BlendTreeData blendTree, float2 parameter)
		{
			var x = parameter.x;
			blendTree.blends[0] = math.select(0f, 1f, x < blendTree.clipPositions[0].x);

			var previousX = blendTree.clipPositions[0].x;

			for (var i = 1; i < blendTree.clipPositions.Length; i++)
			{
				blendTree.blends[i] = 0;
				if (previousX <= x && x < blendTree.clipPositions[i].x)
				{
					var t = math.unlerp(previousX, blendTree.clipPositions[i].x, x);
					blendTree.blends[i] = math.lerp(0, 1, t);
					blendTree.blends[i-1] = 1 - blendTree.blends[i];
				}
				previousX = blendTree.clipPositions[i].x;
			}

			var lastIndex = blendTree.blends.Length - 1;
			blendTree.blends[lastIndex] = math.select(blendTree.blends[lastIndex], 1f, x >= blendTree.clipPositions[lastIndex].x);
		}

		[BurstCompile]
		struct EvaluateTreesJob : IJobFor
		{
			public UnsafeArray<BlendTreeData> blendTrees;
			public UnsafeArray<float2> parameters;
			public UnsafeBitmask takenBlendTrees;

			public void Execute(int index)
			{
				if (!takenBlendTrees[(uint)index])
				{
					return;
				}

				EvaluateBlendTree(ref blendTrees[index], parameters[index]);
			}
		}
	}
}
