using System.Collections.Generic;
using AclUnity;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;

namespace KVD.Puppeteer
{
	[ExecuteAlways]
	public unsafe class AnimationClipPlayer : MonoBehaviour
	{
		[SerializeField] AclClip _clipA;
		[SerializeField] AclClip _clipB;
		[SerializeField, Range(0, 1)] float _blend;
		public Transform root;

		public bool autoProgressTime = true;
		public float time;

		NativeArray<Qvvs> _bonesBuffer;
		TransformAccessArray _transformAccessArray;

		void Start()
		{
			_transformAccessArray = new TransformAccessArray(_clipA.TransformsCount);

			var breadthQueue = new Queue<Transform>();
			_transformAccessArray.Add(transform);

			breadthQueue.Enqueue(root);
			while (breadthQueue.Count > 0)
			{
				var bone = breadthQueue.Dequeue();
				_transformAccessArray.Add(bone);

				for (int i = 0; i < bone.childCount; i++)
				{
					var child = bone.GetChild(i);
					breadthQueue.Enqueue(child);
				}
			}

			_bonesBuffer = new NativeArray<Qvvs>(_transformAccessArray.length, Allocator.Persistent);
		}

		void OnDestroy()
		{
			_transformAccessArray.Dispose();
			_bonesBuffer.Dispose();
		}

		void Update()
		{
			if (autoProgressTime)
			{
				time += Time.deltaTime;
			}

			var clipTime = _clipA.LoopToClipTime(time);
			_clipA.Sample(_bonesBuffer, clipTime, _blend, true);

			clipTime = _clipB.LoopToClipTime(time);
			_clipB.Sample(_bonesBuffer, clipTime, 1-_blend, false);

			new WriteBonesDownJob
				{
					boneTransforms = _bonesBuffer
				}.Schedule(_transformAccessArray)
				.Complete();
		}

		[BurstCompile]
		struct WriteBonesDownJob : IJobParallelForTransform
		{
			[ReadOnly] public NativeArray<Qvvs> boneTransforms;

			public void Execute(int index, TransformAccess transform)
			{
				var boneTransform = boneTransforms[index];
				transform.SetLocalPositionAndRotation(boneTransform.position.xyz, boneTransform.rotation);
				transform.localScale = boneTransform.stretch;
			}
		}
	}
}