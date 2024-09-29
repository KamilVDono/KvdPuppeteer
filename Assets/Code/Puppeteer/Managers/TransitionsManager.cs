using System;
using KVD.Utils.DataStructures;
using KVD.Utils.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace KVD.Puppeteer.Managers
{
	public class TransitionsManager
	{
		UnsafeList<Transition> _transitions;

		public static TransitionsManager Instance { get; private set; }

		public TransitionsManager()
		{
			Instance = this;

			_transitions = new UnsafeList<Transition>(128, Allocator.Domain);
		}

		public void StartTransition(uint animator, uint toState, float duration)
		{
			if (FindFromIndex(animator, out var fromState))
			{
				var animatorTransitions = new TransitionAnimatorEquality(animator);
				foreach (var transitionIndex in _transitions.FindAllIndicesRevers(animatorTransitions))
				{
					if (_transitions[transitionIndex].fromState != toState)
					{
						StatesManager.Instance.RemoveState(_transitions[transitionIndex].fromState, false);
					}
					_transitions.RemoveAt(transitionIndex);
				}

				if (fromState != toState & duration > 0)
				{
					StatesManager.Instance.UpdateBlend(fromState, 1);
					StatesManager.Instance.UpdateBlend(toState, 0);

					var transition = new Transition(animator, fromState, toState, duration);
					_transitions.Add(transition);
				}
				else
				{
					StatesManager.Instance.UpdateBlend(toState, 1);
				}
			}
			else
			{
				StatesManager.Instance.UpdateBlend(toState, 1);
			}
			StatesManager.Instance.SetCurrentState(animator, toState);
		}

		public unsafe JobHandle RunTransitions()
		{
			var completedTransitions = new UnsafeBitmask((uint)_transitions.Length, Allocator.TempJob);

			var writer = StatesManager.Instance.GetBlendsWriter();
			var transitionsJob = new DoTransitionsJob
			{
				deltaTime = Time.deltaTime,
				transitions = _transitions.AsUnsafeArray(),
				completedTransitions = completedTransitions,
				blendsWriter = writer
			}.Schedule(_transitions.Length, default);

			return new RemoveCompletedTransitionsJob
			{
				transitions = (UnsafeList<Transition>*)UnsafeUtility.AddressOf(ref _transitions),
				completedTransitions = completedTransitions // Job will dispose it
			}.Schedule(transitionsJob);
		}

		public void UnregisterPuppet(uint animator)
		{
			var animatorTransitions = new TransitionAnimatorEquality(animator);
			foreach (var transitionIndex in _transitions.FindAllIndicesRevers(animatorTransitions))
			{
				_transitions.RemoveAt(transitionIndex);
			}
		}

		bool FindFromIndex(uint animator, out uint index)
		{
			index = StatesManager.Instance.GetCurrentState(animator);
			return index != StatesManager.InvalidHandle;
		}

		[BurstCompile]
		struct DoTransitionsJob : IJobFor
		{
			public float deltaTime;
			public UnsafeArray<Transition> transitions;
			public UnsafeBitmask completedTransitions;
			public StatesManager.BlendsWriter blendsWriter;

			public void Execute(int index)
			{
				ref var transition = ref transitions[index];
				transition.Update(deltaTime);

				blendsWriter.Update(transition.fromState, transition.fromBlend);
				blendsWriter.Update(transition.toState, transition.toBlend);

				if (transition.IsDone)
				{
					completedTransitions.Up(index);
				}
			}
		}

		[BurstCompile]
		unsafe struct RemoveCompletedTransitionsJob : IJob
		{
			[NativeDisableUnsafePtrRestriction] public UnsafeList<Transition>* transitions;
			public UnsafeBitmask completedTransitions;

			public void Execute()
			{
				for (var i = transitions->Length - 1; i >= 0; i--)
				{
					if (completedTransitions[i])
					{
						transitions->RemoveAt(i);
					}
				}
				completedTransitions.Dispose();
			}
		}

		struct Transition
		{
			public readonly uint animator;
			public readonly uint fromState;
			public float fromBlend;
			public readonly uint toState;
			public float toBlend;
			public readonly float duration;
			public float progressTime;

			public bool IsDone => progressTime >= duration;

			public Transition(uint animator, uint fromState, uint toState, float duration) : this()
			{
				this.animator = animator;
				this.fromState = fromState;
				this.toState = toState;
				this.fromBlend = 1;
				this.toBlend = 0;
				this.duration = duration;
				progressTime = 0;
			}

			public void Update(float deltaTime)
			{
				progressTime += deltaTime;
				var progression = math.saturate(progressTime / duration);
				fromBlend = 1 - progression;
				toBlend = progression;
			}
		}

		readonly struct TransitionAnimatorEquality : IEquatable<Transition>
		{
			public readonly uint animator;

			public TransitionAnimatorEquality(uint animator)
			{
				this.animator = animator;
			}

			public bool Equals(Transition other)
			{
				return animator == other.animator;
			}
		}
	}
}
