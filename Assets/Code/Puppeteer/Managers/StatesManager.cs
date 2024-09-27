using System;
using KVD.Puppeteer.Data;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace KVD.Puppeteer.Managers
{
	public class StatesManager
	{
		public const uint InvalidHandle = unchecked((uint)-1);

		OccupiedArray<AnimationState> _states;
		UnsafeArray<uint> _currentStates;

		public static StatesManager Instance { get; private set; }

		public unsafe StatesManager(int preAllocCount)
		{
			Instance = this;

			_states = new OccupiedArray<AnimationState>((uint)preAllocCount, Allocator.Domain);

			_currentStates = new UnsafeArray<uint>((uint)preAllocCount, Allocator.Domain);
			UnsafeUtils.Fill(_currentStates.Ptr, InvalidHandle, preAllocCount);
		}

		public uint AddClipState(uint puppet, ushort clipId)
		{
			var state = new AnimationState(AnimationState.AnimationType.Clip, clipId, puppet);
			if (!_states.TryInsert(state, out var slot))
			{
				return InvalidHandle;
			}
			return slot;
		}

		public uint AddBlendTreeState(uint puppet, ushort blendTreeId)
		{
			var state = new AnimationState(AnimationState.AnimationType.BlendTree, blendTreeId, puppet);
			if (!_states.TryInsert(state, out var slot))
			{
				return InvalidHandle;
			}
			return slot;
		}

		public void RemoveState(uint stateIndex)
		{
			ref readonly var state = ref _states[stateIndex];

			if (state.Type == AnimationState.AnimationType.Clip)
			{
				ClipsManager.Instance.UnregisterClip(state.data);
			}
			else
			{
				BlendTreesManager.Instance.RemoveBlendTree(state.data);
			}

			if (_currentStates[state.puppet] == stateIndex)
			{
				_currentStates[state.puppet] = InvalidHandle;
			}

			_states.Release(stateIndex);
		}

		public void UpdateBlend(uint state, float blend)
		{
			_states[state].blend = blend;
		}

		public void SetCurrentState(uint puppet, uint toState)
		{
			_currentStates[puppet] = toState;
		}

		public uint GetCurrentState(uint puppet)
		{
			return _currentStates[puppet];
		}

		public void UnregisterPuppet(uint puppet)
		{
			var search = new AnimationStatePuppetEquality(puppet);
			foreach (var index in _states.array.FindAllIndicesRevers(search))
			{
				_states.Release(index);
			}
			_currentStates[puppet] = InvalidHandle;
		}

		public BlendsWriter GetBlendsWriter()
		{
			return new BlendsWriter
			{
				states = _states.array
			};
		}

		public JobHandle FillSamplingData(JobHandle dependency)
		{
			var blendTrees = BlendTreesManager.Instance.BlendTrees;
			return new UpdateJob
			{
				deltaTime = Time.deltaTime,
				states = _states,
				blendTrees = blendTrees,

				clipsWriter = SamplingManager.Instance.GetClipsWriter(),
			}.Schedule(dependency);
		}

		public struct BlendsWriter
		{
			public UnsafeArray<AnimationState> states;

			public void Update(uint state, float blend)
			{
				states[state].blend = blend;
			}
		}

		struct UpdateJob : IJob
		{
			public float deltaTime;

			public OccupiedArray<AnimationState> states;
			public UnsafeArray<BlendTreeData> blendTrees;

			public SamplingManager.ClipsWriter clipsWriter;

			public void Execute()
			{
				for (var i = 0u; i < states.Length; i++)
				{
					if (!states.IsOccupied(i))
					{
						continue;
					}

					ref var state = ref states[i];
					state.time += deltaTime;

					if (state.Type == AnimationState.AnimationType.Clip)
					{
						if (state.blend > 0)
						{
							clipsWriter.AddClip(state.puppet, state.data, state.blend, state.time);
						}
					}
					else
					{
						var tree = blendTrees[state.data];
						for (var j = 0; j < tree.clips.Length; j++)
						{
							var clip = tree.clips[j];
							var blend = tree.blends[j] * state.blend;
							if (blend > 0)
							{
								clipsWriter.AddClip(state.puppet, clip, blend, state.time);
							}
						}
					}
				}
			}
		}

		readonly struct AnimationStatePuppetEquality : IEquatable<AnimationState>
		{
			public readonly uint puppet;

			public AnimationStatePuppetEquality(uint puppet)
			{
				this.puppet = puppet;
			}

			public bool Equals(AnimationState other)
			{
				return puppet == other.puppet;
			}
		}
	}
}
