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

		static void EvaluateCartesianGradiantBand(ref BlendTreeData blendTree, float2 requestPoint)
		{
			var totalWeight = 0f;

			var clipPositions = blendTree.clipPositions;
			var weightsCount = clipPositions.Length;
			for (var i = 0; i < weightsCount; i++)
			{
				var clipPosition = clipPositions[i];
				var clipToSample = requestPoint - clipPosition;
				var weight = 1f;

				for (var j = 0; j < weightsCount; j++)
				{
					if (j == i)
					{
						continue;
					}

					var otherClipPosition = clipPositions[j];
					var clipToOther = otherClipPosition - clipPosition;

					var clipToOtherLengthSq = math.dot(clipToOther, clipToOther);
					var newWeight = math.dot(clipToSample, clipToOther) / clipToOtherLengthSq;
					newWeight = 1f - newWeight;
					newWeight = math.clamp(newWeight, 0f, 1f);

					weight = math.min(weight, newWeight);
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

		static void EvaluatePolarGradiantBand(ref BlendTreeData blendTree, float2 requestPoint)
		{
			const float kDirScale = 2f;

			var totalWeight = 0f;
			var requestLength = math.length(requestPoint);

			var clipPositions = blendTree.clipPositions;
			var weightsCount = clipPositions.Length;
			for (var i = 0; i < weightsCount; i++)
			{
				var clipPosition = clipPositions[i];
				var clipPositionLength = math.length(clipPosition);

				var weight = 1f;

				for (var j = 0; j < weightsCount; j++)
				{
					if (j == i)
					{
						continue;
					}

					var otherClipPosition = clipPositions[j];
					var otherClipPositionLength = math.length(otherClipPosition);

					var lengthsAverage = (otherClipPositionLength + clipPositionLength) * 0.5f;

					// Calc angle and mag for i -> sample
					var lengthClipToRequest = (requestLength - clipPositionLength) / lengthsAverage;
					var angleClipToRequest = SignedAngle(clipPosition, requestPoint);

					// Calc angle and mag for i -> j
					var lengthClipToOther = (otherClipPositionLength - clipPositionLength) / lengthsAverage;
					var angleClipToOther = SignedAngle(clipPosition, otherClipPosition);

					// Calc vec for i -> sample
					float2 polarClipToRequest;
					polarClipToRequest.x = lengthClipToRequest;
					polarClipToRequest.y = angleClipToRequest * kDirScale;

					// Calc vec for i -> j
					float2 polarClipToOther;
					polarClipToOther.x = lengthClipToOther;
					polarClipToOther.y = angleClipToOther * kDirScale;

					// Calc weight
					var clipToOtherLengthSq = math.dot(polarClipToOther, polarClipToOther);
					var newWeight = math.dot(polarClipToRequest, polarClipToOther) / clipToOtherLengthSq;
					newWeight = 1f - newWeight;
					newWeight = math.clamp(newWeight, 0f, 1f);

					weight = math.min(newWeight, weight);
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
