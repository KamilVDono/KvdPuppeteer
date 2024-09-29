using KVD.Puppeteer.Data;
using KVD.Utils.DataStructures;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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
			}.ScheduleParallel(_takenBlendTrees.LastOne()+1, 16, dependencies);
		}

		static void EvaluateBlendTree(ref BlendTreeData blendTree, in float2 parameter)
		{
			if (blendTree.type == BlendTreeData.Type.Space1D)
			{
				Evaluate1DBlendTree(ref blendTree, parameter);
			}
			else if (blendTree.type == BlendTreeData.Type.Space2DCartesianGradiantBand)
			{
				EvaluateCartesianGradiantBand(ref blendTree, parameter);
			}
			else if (blendTree.type == BlendTreeData.Type.Space2DPolarGradiantBand)
			{
				EvaluatePolarGradiantBand(ref blendTree, parameter);
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

		static void EvaluateCartesianGradiantBand(ref BlendTreeData blendTree, float2 parameter)
		{
			var totalWeight = 0f;

			var clipPositions = blendTree.clipPositions;
			var weightsCount = clipPositions.Length;
			for (var i = 0; i < weightsCount; i++)
			{
				var point_i = clipPositions[i];
				var vec_is = parameter - point_i;
				var weight = 1f;

				for (var j = 0; j < weightsCount; j++)
				{
					if (j == i)
					{
						continue;
					}

					var point_j = clipPositions[j];
					var vec_ij = point_j - point_i;

					var lensq_ij = math.dot(vec_ij, vec_ij);
					var new_weight = math.dot(vec_is, vec_ij) / lensq_ij;
					new_weight = 1f - new_weight;
					new_weight = math.clamp(new_weight, 0f, 1f);

					weight = math.min(weight, new_weight);
				}

				blendTree.blends[i] = weight;
				totalWeight += weight;
			}

			var weightReciprocal = math.rcp(totalWeight);
			for (var i = 0; i < weightsCount; i++)
			{
				blendTree.blends[i] *= weightReciprocal;
			}
		}

		static void EvaluatePolarGradiantBand(ref BlendTreeData blendTree, float2 parameter)
		{
			const float kDirScale = 2f;

			var totalWeight = 0f;
			var sample_mag = math.length(parameter);

			var clipPositions = blendTree.clipPositions;
			var weightsCount = clipPositions.Length;
			for (var i = 0; i < weightsCount; i++)
			{
				var point_i = clipPositions[i];
				var point_mag_i = math.length(point_i);

				var weight = 1f;

				for (var j = 0; j < weightsCount; j++)
				{
					if (j == i)
					{
						continue;
					}

					var point_j = clipPositions[j];
					var point_mag_j = math.length(point_j);

					var ij_avg_mag = (point_mag_j + point_mag_i) * 0.5f;

					// Calc angle and mag for i -> sample
					var mag_is = (sample_mag - point_mag_i) / ij_avg_mag;
					var angle_is = SignedAngle(point_i, parameter);

					// Calc angle and mag for i -> j
					var mag_ij = (point_mag_j - point_mag_i) / ij_avg_mag;
					var angle_ij = SignedAngle(point_i, point_j);

					// Calc vec for i -> sample
					float2 vec_is;
					vec_is.x = mag_is;
					vec_is.y = angle_is * kDirScale;

					// Calc vec for i -> j
					float2 vec_ij;
					vec_ij.x = mag_ij;
					vec_ij.y = angle_ij * kDirScale;

					// Calc weight
					var lensq_ij = math.dot(vec_ij, vec_ij);
					var new_weight = math.dot(vec_is, vec_ij) / lensq_ij;
					new_weight = 1f - new_weight;
					new_weight = math.clamp(new_weight, 0f, 1f);

					weight = math.min(new_weight, weight);
				}

				blendTree.blends[i] = weight;
				totalWeight += weight;
			}

			var weightReciprocal = math.rcp(totalWeight);
			for (var i = 0; i < weightsCount; i++)
			{
				blendTree.blends[i] *= weightReciprocal;
			}

			float SignedAngle(float2 a, float2 b)
			{
				return math.atan2(a.x * b.y - a.y * b.x, a.x * b.x + a.y * b.y);
			}
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
